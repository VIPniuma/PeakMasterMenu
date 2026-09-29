using System;
using System.Collections.Generic;
using System.Text;
using Peak;
using Photon.Pun;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Zorro.Core;

namespace PeakMasterMenu.UI;

/// <summary>
/// 常驻屏幕的叠加层。它有自己的 Canvas（排序在菜单之下），菜单关着时也照常显示。
///
/// 包含：
/// - 左上角：坐标 / 延迟
/// - 右上角：本局地图组成与当前所在群系（实时跟随）
/// - 正上方：游戏内时间（看天亮天黑）
/// - 屏幕中央：倒下时的求援提示
/// - 屏幕右侧：保命模式状态
/// - 世界空间：队友、地面物品与生物的位置标记
/// </summary>
internal sealed class HudOverlay : MonoBehaviour
{
    /// <summary>同时绘制的物品标记上限，避免物品多的时候刷屏。</summary>
    private const int MaxItemMarkers = 80;

    /// <summary>同时绘制的行李箱 / 宝箱标记上限。</summary>
    private const int MaxLuggageMarkers = 60;

    /// <summary>同时绘制的雕像标记上限。</summary>
    private const int MaxStatueMarkers = 40;

    /// <summary>同时绘制的生物标记上限（超过时只留离自己最近的那批）。</summary>
    private const int MaxMobMarkers = 100;

    /// <summary>
    /// 物品 / 行李箱 / 雕像这类"小标记"的固定屏幕尺寸与字号。
    /// 单位是画布参考分辨率（1920×1080）下的像素，和距离完全无关：
    /// 远处和近处看到的都是同样大小。
    /// </summary>
    private static readonly Vector2 SmallMarkerSize = new Vector2(150f, 24f);

    private const float SmallMarkerFontSize = 14f;

    private static readonly Color MarkerBg = new Color(0.086f, 0.078f, 0.067f, 0.85f);
    private static readonly Color ItemMarkerBg = new Color(0.075f, 0.11f, 0.14f, 0.82f);
    private static readonly Color MobMarkerBg = new Color(0.36f, 0.07f, 0.06f, 0.86f);
    private static readonly Color LuggageMarkerBg = new Color(0.44f, 0.32f, 0.05f, 0.85f);
    private static readonly Color StatueMarkerBg = new Color(0.18f, 0.21f, 0.34f, 0.85f);

    /// <summary>保命模式徽章：深墨绿卡片 + 一条亮绿竖条。</summary>
    private static readonly Color SaveBadgeBg = new Color(0.07f, 0.15f, 0.10f, 0.9f);
    private static readonly Color SaveBadgeAccent = new Color(0.45f, 0.8f, 0.38f, 1f);

    /// <summary>徽章尺寸与内边距（画布参考分辨率下的像素）。</summary>
    private static readonly Vector2 SaveBadgeSize = new Vector2(360f, 110f);
    private const float SaveBadgePadding = 12f;
    private const float SaveBadgeTextLeft = 24f;
    private const float SaveBadgeTextRight = 14f;

    private sealed class Marker
    {
        internal RectTransform Root = null!;
        internal TextMeshProUGUI Label = null!;
    }

    private readonly Dictionary<Character, Marker> markers = new();
    private readonly Dictionary<Item, Marker> itemMarkers = new();

    /// <summary>
    /// 生物标记按 <see cref="CreatureKey"/> 折出来的 key 索引（Character 类的生物就是那个
    /// <see cref="Character"/>，其余是 PhotonView），这样同一只生物不管扫到的是挂在哪一层
    /// 子物体上的组件，都只会有一个标记。
    /// </summary>
    private readonly Dictionary<Component, Marker> mobMarkers = new();

    private readonly Dictionary<Luggage, Marker> luggageMarkers = new();

    /// <summary>
    /// 雕像标记的 key 用 <see cref="Component"/>：既可能是雕像本身（<c>ScoutStatue</c>），
    /// 也可能是雕像手里抱着的那颗护符（<c>Item</c>）。用 Component 还顺带保留了
    /// Unity 那套"已销毁对象 == null"的判断，清理更准。
    /// </summary>
    private readonly Dictionary<Component, Marker> statueMarkers = new();

    /// <summary>队友 + 本地假人的临时表，避免每帧都新建 List。</summary>
    private readonly List<Character> teammateBuffer = new();

    private readonly HashSet<Character> seenCharacters = new();
    private readonly HashSet<Item> seenItems = new();
    private readonly HashSet<Component> seenMobs = new();
    private readonly HashSet<Luggage> seenLuggage = new();
    private readonly HashSet<Component> seenStatues = new();
    private readonly List<Character> staleCharacters = new();
    private readonly List<Item> staleItems = new();
    private readonly List<Component> staleMobs = new();
    private readonly List<Luggage> staleLuggage = new();
    private readonly List<Component> staleStatues = new();
    private readonly List<Component> mobBuffer = new();
    private readonly List<Component> nearMobs = new();

    /// <summary>扫到生物时就顺手算好的显示名：画的时候只查表，不再去碰可能已经销毁的对象。</summary>
    private readonly Dictionary<Component, string> mobLabels = new();
    private readonly List<Item> itemBuffer = new();
    private readonly List<Item> nearItems = new();
    private readonly List<Luggage> luggageBuffer = new();
    private readonly List<Component> statueBuffer = new();

    /// <summary>扫描时算好的位置 / 文字，绘制时只查表，不再去碰可能已经销毁的对象。</summary>
    private readonly Dictionary<Component, StatuePose> statuePoses = new();

    /// <summary>扫护符用的临时物品表，避免每轮扫描都新建 List。</summary>
    private readonly List<Item> statueItemScratch = new();

    private float nextMobScan;
    private float nextItemScan;
    private float nextLuggageScan;
    private float nextStatueScan;
    private float nextFilterReport;

    private RectTransform root = null!;
    private TextMeshProUGUI info = null!;
    private TextMeshProUGUI mapText = null!;
    private TextMeshProUGUI clockText = null!;
    private RectTransform savePromptRoot = null!;
    private TextMeshProUGUI savePrompt = null!;
    private RectTransform saveBadgeRoot = null!;
    private TextMeshProUGUI saveBadgeTitle = null!;
    private TextMeshProUGUI saveBadgeHint = null!;
    private TextMeshProUGUI saveBadgeClose = null!;

    private float nextInfoRefresh;
    private float nextMapRefresh;
    private float nextClockRefresh;
    private string lastInfo = string.Empty;
    private string lastMap = string.Empty;
    private string lastClock = string.Empty;
    private string lastFilterReport = string.Empty;
    private bool warned;

    internal static HudOverlay Create()
    {
        var go = new GameObject("PeakMasterMenu.Hud");
        UnityEngine.Object.DontDestroyOnLoad(go);
        return go.AddComponent<HudOverlay>();
    }

    private void Awake()
    {
        var canvasGo = new GameObject("Canvas", typeof(RectTransform));
        canvasGo.transform.SetParent(transform, false);

        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 20000;

        // 跟随分辨率缩放，保证 4K 下不会缩成一小块。
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        root = (RectTransform)canvasGo.transform;
        root.anchorMin = Vector2.zero;
        root.anchorMax = Vector2.one;
        root.offsetMin = Vector2.zero;
        root.offsetMax = Vector2.zero;

        BuildInfo();
        BuildMap();
        BuildClock();
        BuildSaveMode();
    }

    // ---------------------------------------------------------------- 构建

    private void BuildInfo()
    {
        info = UiFactory.Label(root, string.Empty, 18f, TextAlignmentOptions.TopLeft, UiFactory.TextMain);
        info.overflowMode = TextOverflowModes.Overflow;
        info.rectTransform.anchorMin = new Vector2(0f, 1f);
        info.rectTransform.anchorMax = new Vector2(0f, 1f);
        info.rectTransform.pivot = new Vector2(0f, 1f);
        info.rectTransform.sizeDelta = new Vector2(640f, 220f);
        info.rectTransform.anchoredPosition = new Vector2(18f, -14f);
    }

    private void BuildMap()
    {
        mapText = UiFactory.Label(root, string.Empty, 20f, TextAlignmentOptions.TopRight, UiFactory.TextMain);
        mapText.overflowMode = TextOverflowModes.Overflow;
        mapText.rectTransform.anchorMin = new Vector2(1f, 1f);
        mapText.rectTransform.anchorMax = new Vector2(1f, 1f);
        mapText.rectTransform.pivot = new Vector2(1f, 1f);
        mapText.rectTransform.sizeDelta = new Vector2(720f, 130f);
        mapText.rectTransform.anchoredPosition = new Vector2(-18f, -14f);
    }

    private void BuildClock()
    {
        clockText = UiFactory.Label(root, string.Empty, 22f, TextAlignmentOptions.Top, UiFactory.TextMain);
        clockText.overflowMode = TextOverflowModes.Overflow;
        clockText.rectTransform.anchorMin = new Vector2(0.5f, 1f);
        clockText.rectTransform.anchorMax = new Vector2(0.5f, 1f);
        clockText.rectTransform.pivot = new Vector2(0.5f, 1f);
        clockText.rectTransform.sizeDelta = new Vector2(520f, 90f);
        clockText.rectTransform.anchoredPosition = new Vector2(0f, -12f);
    }

    private void BuildSaveMode()
    {
        var promptBg = UiFactory.Panel(
            root,
            UiFactory.PillSprite,
            new Color(0.42f, 0.09f, 0.08f, 0.86f),
            false,
            "SavePrompt");
        savePromptRoot = promptBg.rectTransform;
        savePromptRoot.anchorMin = new Vector2(0.5f, 0.5f);
        savePromptRoot.anchorMax = new Vector2(0.5f, 0.5f);
        savePromptRoot.pivot = new Vector2(0.5f, 0.5f);
        savePromptRoot.sizeDelta = new Vector2(880f, 104f);
        savePromptRoot.anchoredPosition = new Vector2(0f, 150f);

        savePrompt = UiFactory.Label(promptBg.transform, string.Empty, 25f, TextAlignmentOptions.Center, UiFactory.TextMain);
        // Label 默认是 Ellipsis，长句子会被吃掉尾巴，这里改成按实际宽度排。
        savePrompt.overflowMode = TextOverflowModes.Overflow;
        UiFactory.Stretch(savePrompt.rectTransform, 20f, 10f);

        // 右侧的保命模式卡片：圆角矩形，左边一条亮绿竖条表示"开着"。
        var badgeBg = UiFactory.Panel(
            root,
            UiFactory.PanelSprite,
            SaveBadgeBg,
            false,
            "SaveBadge");
        saveBadgeRoot = badgeBg.rectTransform;
        UiFactory.AnchorRight(saveBadgeRoot, SaveBadgeSize, -18f);

        var accent = UiFactory.Panel(badgeBg.transform, UiFactory.PillSprite, SaveBadgeAccent, false, "Accent");
        var accentRect = accent.rectTransform;
        accentRect.anchorMin = new Vector2(0f, 0f);
        accentRect.anchorMax = new Vector2(0f, 1f);
        accentRect.pivot = new Vector2(0f, 0.5f);
        accentRect.sizeDelta = new Vector2(6f, -SaveBadgePadding * 2f);
        accentRect.anchoredPosition = new Vector2(SaveBadgePadding, 0f);

        saveBadgeTitle = UiFactory.Label(badgeBg.transform, "保命模式已开启", 24f, TextAlignmentOptions.Left, UiFactory.TextMain);
        AnchorBadgeRow(saveBadgeTitle, 10f, 34f);

        saveBadgeHint = UiFactory.Label(badgeBg.transform, "无敌 + 无限体力", 18f, TextAlignmentOptions.Left, UiFactory.TextMuted);
        AnchorBadgeRow(saveBadgeHint, 46f, 24f);

        saveBadgeClose = UiFactory.Label(badgeBg.transform, string.Empty, 19f, TextAlignmentOptions.Left, SaveBadgeAccent);
        AnchorBadgeRow(saveBadgeClose, 72f, 26f);
    }

    /// <summary>
    /// 把卡片里的一行文字贴到顶部往下 <paramref name="topOffset"/> 处。
    /// 顺手关掉 <see cref="TextOverflowModes.Ellipsis"/>：这里每行都是我们自己排好位置的，
    /// 被截断成「…」反而看不到关键信息。
    /// </summary>
    private static void AnchorBadgeRow(TextMeshProUGUI label, float topOffset, float height)
    {
        label.overflowMode = TextOverflowModes.Overflow;

        var rt = label.rectTransform;
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.offsetMin = new Vector2(SaveBadgeTextLeft, 0f);
        rt.offsetMax = new Vector2(-SaveBadgeTextRight, -topOffset);
        rt.sizeDelta = new Vector2(rt.sizeDelta.x, height);
    }

    // ---------------------------------------------------------------- 每帧刷新

    private void LateUpdate()
    {
        try
        {
            UpdateInfo();
            UpdateMap();
            UpdateClock();
            UpdateSaveMode();
            UpdateMarkers();
            UpdateItemMarkers();
            UpdateMobMarkers();
            UpdateLuggageMarkers();
            UpdateStatueMarkers();
        }
        catch (Exception e)
        {
            if (warned)
            {
                return;
            }

            warned = true;
            Plugin.Log.LogWarning($"HUD 刷新失败，已停止重复报错: {e}");
        }
    }

    // ---------------------------------------------------------------- 信息栏

    private void UpdateInfo()
    {
        if (info == null)
        {
            return;
        }

        var local = Character.localCharacter;
        if (!Plugin.ShowHudInfo.Value || local == null)
        {
            SetText(info, string.Empty, ref lastInfo);
            return;
        }

        // 坐标每 50 毫秒刷一次，跟得上跑动。
        if (!Tick(ref nextInfoRefresh, 0.05f))
        {
            return;
        }

        // 位置必须取躯干刚体：角色根节点的 transform 是出生点，永远不动。
        var position = CharacterBody.Position(local);
        var text = $"坐标  X {position.x:F1}   Y {position.y:F1}   Z {position.z:F1}\n"
            + $"延迟  {Ping()} ms";

        SetText(info, text, ref lastInfo);
    }

    private void UpdateMap()
    {
        if (mapText == null)
        {
            return;
        }

        if (!Plugin.ShowMapInfo.Value)
        {
            SetText(mapText, string.Empty, ref lastMap);
            return;
        }

        // 群系会随着攀爬切换，刷新要跟得上。
        if (!Tick(ref nextMapRefresh, 0.25f))
        {
            return;
        }

        MapHandler? handler = null;
        try
        {
            handler = Singleton<MapHandler>.Instance;
        }
        catch (Exception)
        {
            // 还没进图时忽略。
        }

        // 还没进图（机场 / 加载中）就什么都不显示，别写个「未知」占着右上角。
        if (handler == null || !MapHandler.ExistsAndInitialized)
        {
            SetText(mapText, string.Empty, ref lastMap);
            return;
        }

        var builder = new StringBuilder("本局地图  ");

        // 这局地图的群系列表就是现读的：每天的地图都是随机重排的。
        var biomes = handler.biomes;
        if (biomes != null && biomes.Count > 0)
        {
            for (var i = 0; i < biomes.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append(" · ");
                }

                builder.Append(BiomeNames.Of(biomes[i]));
            }
        }
        else
        {
            builder.Append("未知");
        }

        try
        {
            // 当前所在地：直接问游戏，随人物的攀爬进度实时变化。
            builder.Append('\n').Append("当前位置  ").Append(BiomeNames.Of(handler.GetCurrentBiome()));
        }
        catch (Exception)
        {
            // 关卡还没准备好时忽略。
        }

        SetText(mapText, builder.ToString(), ref lastMap);
    }

    private void UpdateClock()
    {
        if (clockText == null)
        {
            return;
        }

        var manager = DayNightManager.instance;
        if (!Plugin.ShowClock.Value || manager == null)
        {
            SetText(clockText, string.Empty, ref lastClock);
            return;
        }

        if (!Tick(ref nextClockRefresh, 0.25f))
        {
            return;
        }

        var time = manager.timeOfDay % 24f;
        if (time < 0f)
        {
            time += 24f;
        }

        var hours = Mathf.FloorToInt(time);
        var minutes = Mathf.FloorToInt((time - hours) * 60f);
        var isDay = time >= manager.dayStart && time < manager.dayEnd;

        float remaining;
        if (isDay)
        {
            remaining = manager.dayEnd - time;
        }
        else if (time >= manager.dayEnd)
        {
            remaining = 24f - time + manager.dayStart;
        }
        else
        {
            remaining = manager.dayStart - time;
        }

        var remainingText = remaining >= 1f
            ? $"{(isDay ? "天黑" : "天亮")}还有 {remaining:0.#} 小时"
            : $"{(isDay ? "天黑" : "天亮")}还有 {Mathf.Max(0f, remaining * 60f):0} 分钟";

        var text = $"第 {manager.dayCount} 天　{hours:D2}:{minutes:D2}\n{(isDay ? "白天" : "夜晚")}　{remainingText}";
        SetText(clockText, text, ref lastClock);
    }

    /// <summary>
    /// 每块文字各自一个节流计时器。
    ///
    /// 以前三块文字共用一个计时器：每帧第一个调用者（左上角坐标）把时间戳推到
    /// 0.25 秒之后，后面的地图和时间就永远拿不到刷新机会，一直停在空字符串上。
    /// </summary>
    private static bool Tick(ref float next, float interval)
    {
        if (Time.unscaledTime < next)
        {
            return false;
        }

        next = Time.unscaledTime + interval;
        return true;
    }

    private void SetText(TextMeshProUGUI target, string text, ref string cache)
    {
        if (text == cache)
        {
            return;
        }

        cache = text;
        target.text = text;
    }

    // ---------------------------------------------------------------- 保命模式

    private void UpdateSaveMode()
    {
        if (savePromptRoot != null)
        {
            var show = SaveMode.Prompt;
            savePromptRoot.gameObject.SetActive(show);

            if (show && savePrompt != null)
            {
                savePrompt.text = $"你已倒下　按 {Plugin.SaveMeKey.Value} 紧急救援\n（清空异常状态就地起身 + 无敌 + 无限体力）";
            }
        }

        if (saveBadgeRoot != null)
        {
            var active = SaveMode.Active;
            saveBadgeRoot.gameObject.SetActive(active);

            if (active && saveBadgeHint != null)
            {
                saveBadgeHint.text = "无敌 + 无限体力";
            }

            if (active && saveBadgeClose != null)
            {
                saveBadgeClose.text = $"按 {Plugin.SaveMeKey.Value} 关闭";
            }
        }
    }

    // ---------------------------------------------------------------- 队友标记

    /// <summary>
    /// 要画的「队友」：真队友 + 本地假人。
    ///
    /// 假人（见 <see cref="Dummies"/>）不是真正的联机队友，<c>PlayerHandler</c> 里没有它，
    /// 但显示得算上它 —— 不然这个假人就没法拿来测标记和体力条了。
    /// </summary>
    private List<Character> Teammates()
    {
        teammateBuffer.Clear();

        try
        {
            var characters = PlayerHandler.GetAllPlayerCharacters();
            if (characters != null)
            {
                teammateBuffer.AddRange(characters);
            }
        }
        catch (Exception)
        {
            // 没进房间时读不到，当成只有假人。
        }

        teammateBuffer.AddRange(Dummies.Characters);
        return teammateBuffer;
    }

    private void UpdateMarkers()
    {
        var local = Character.localCharacter;
        if (!Plugin.ShowTeammateMarkers.Value || local == null)
        {
            HideMarkers();
            return;
        }

        var camera = ResolveCamera();
        if (camera == null)
        {
            HideMarkers();
            return;
        }

        seenCharacters.Clear();

        foreach (var character in Teammates())
        {
            if (character == null || ReferenceEquals(character, local) || character.data == null)
            {
                continue;
            }

            seenCharacters.Add(character);

            var marker = GetMarker(character);
            var world = CharacterBody.MarkerAnchor(character, 0.8f);
            var screen = camera.WorldToScreenPoint(world);

            if (screen.z <= 0f
                || screen.x < -300f || screen.x > Screen.width + 300f
                || screen.y < -300f || screen.y > Screen.height + 300f)
            {
                marker.Root.gameObject.SetActive(false);
                continue;
            }

            marker.Root.gameObject.SetActive(true);
            marker.Root.anchoredPosition = ScreenToCanvas(screen);

            var distance = Vector3.Distance(CharacterBody.Position(local), world);
            marker.Label.text = $"{Name(character)}  {distance:F0}m";
        }

        staleCharacters.Clear();
        foreach (var pair in markers)
        {
            if (!seenCharacters.Contains(pair.Key))
            {
                staleCharacters.Add(pair.Key);
            }
        }

        foreach (var character in staleCharacters)
        {
            if (markers.TryGetValue(character, out var marker) && marker.Root != null)
            {
                Destroy(marker.Root.gameObject);
            }

            markers.Remove(character);
        }
    }

    private void HideMarkers()
    {
        foreach (var pair in markers)
        {
            if (pair.Value.Root != null)
            {
                pair.Value.Root.gameObject.SetActive(false);
            }
        }
    }

    private Marker GetMarker(Character character)
    {
        if (markers.TryGetValue(character, out var existing) && existing.Root != null)
        {
            return existing;
        }

        var marker = CreateMarker("Marker", new Vector2(240f, 30f), 17f, MarkerBg);
        markers[character] = marker;
        return marker;
    }

    // ---------------------------------------------------------------- 物品标记

    private void UpdateItemMarkers()
    {
        var local = Character.localCharacter;
        if (!Cheats.DrawItems || local == null)
        {
            HideItemMarkers();
            return;
        }

        var camera = ResolveCamera();
        if (camera == null)
        {
            HideItemMarkers();
            return;
        }

        RefreshItemBuffer();

        seenItems.Clear();
        nearItems.Clear();

        var range = Mathf.Max(5f, Cheats.DrawDistance);
        var origin = CharacterBody.Position(local);
        var groundTotal = 0;
        var matchedTotal = 0;

        foreach (var item in itemBuffer)
        {
            if (item == null || item.itemState != ItemState.Ground)
            {
                continue;
            }

            groundTotal++;

            // 默认一件都不画，勾选了哪些就只画哪些。
            if (!MatchesDrawFilter(item))
            {
                continue;
            }

            matchedTotal++;

            if (Vector3.Distance(origin, item.transform.position) > range)
            {
                continue;
            }

            nearItems.Add(item);
        }

        // 近的优先，超过上限时保留离自己最近的那批。
        nearItems.Sort((a, b) => Vector3.Distance(origin, a.transform.position)
            .CompareTo(Vector3.Distance(origin, b.transform.position)));

        var drawn = 0;
        foreach (var item in nearItems)
        {
            if (drawn >= MaxItemMarkers)
            {
                break;
            }

            var world = item.transform.position;
            var screen = camera.WorldToScreenPoint(world + (Vector3.up * 0.3f));

            drawn++;
            seenItems.Add(item);

            var marker = GetItemMarker(item);

            if (screen.z <= 0f
                || screen.x < -200f || screen.x > Screen.width + 200f
                || screen.y < -200f || screen.y > Screen.height + 200f)
            {
                marker.Root.gameObject.SetActive(false);
                continue;
            }

            marker.Root.gameObject.SetActive(true);
            marker.Root.anchoredPosition = ScreenToCanvas(screen);
            marker.Label.text = $"{ItemCatalog.DisplayName(item)}  {Vector3.Distance(origin, world):F0}m";
        }

        ReportFilterState(drawn, groundTotal, matchedTotal);

        staleItems.Clear();
        foreach (var pair in itemMarkers)
        {
            if (!seenItems.Contains(pair.Key))
            {
                staleItems.Add(pair.Key);
            }
        }

        foreach (var item in staleItems)
        {
            if (itemMarkers.TryGetValue(item, out var marker) && marker.Root != null)
            {
                Destroy(marker.Root.gameObject);
            }

            itemMarkers.Remove(item);
        }
    }

    /// <summary>
    /// 定期收集场景里的物品。
    ///
    /// 刻意不用 <c>Item.ALL_ITEMS</c>：那个列表由游戏自己维护，
    /// <c>GameHandler.OnSceneLoaded</c> 在切场景时会把它整个清空，清空之后
    /// 场景里明明还躺着物品也扫不到，绘制就永远是空的。物品是在 <c>Awake</c> 里
    /// 注册进去的，被清掉之后不会自动补回来。
    /// </summary>
    private void RefreshItemBuffer()
    {
        if (Time.unscaledTime < nextItemScan)
        {
            return;
        }

        nextItemScan = Time.unscaledTime + 1.5f;
        itemBuffer.Clear();

        try
        {
            itemBuffer.AddRange(FindObjectsByType<Item>(FindObjectsSortMode.None));
        }
        catch (Exception)
        {
            // 场景切换的瞬间可能失败，下一轮再扫。
        }

        // 兜底：把游戏列表里还活着、还显示着的物品补进来（去重）。
        var all = Item.ALL_ITEMS;
        if (all == null)
        {
            return;
        }

        foreach (var item in all)
        {
            if (item != null && item.isActiveAndEnabled && !itemBuffer.Contains(item))
            {
                itemBuffer.Add(item);
            }
        }
    }

    /// <summary>
    /// 勾选了才画。勾选表里存的是目录里的预制体名，场景里的克隆体名字可能不一样，
    /// 所以三种写法都认一遍：目录键、物体原名、去掉 "(Clone)" 后的名字。
    /// </summary>
    private static bool MatchesDrawFilter(Item item)
    {
        if (Cheats.DrawItemFilter.Count == 0)
        {
            return false;
        }

        if (Cheats.DrawItemFilter.Contains(ItemCatalog.Key(item)))
        {
            return true;
        }

        var name = item.name;
        if (Cheats.DrawItemFilter.Contains(name))
        {
            return true;
        }

        var index = name.IndexOf("(Clone)", StringComparison.Ordinal);
        return index > 0 && Cheats.DrawItemFilter.Contains(name.Substring(0, index).Trim());
    }

    /// <summary>
    /// 打开了绘制却没画出东西时，往日志里留一条线索（数字有变化时最多每两秒报一次），
    /// 方便判断是没勾选、离得太远，还是物品压根没扫到。
    /// </summary>
    private void ReportFilterState(int drawn, int groundTotal, int matchedTotal)
    {
        if (!Cheats.DrawItems || drawn > 0)
        {
            lastFilterReport = string.Empty;
            return;
        }

        var signature = $"{itemBuffer.Count}|{groundTotal}|{matchedTotal}|{Item.ALL_ITEMS.Count}";
        if (signature == lastFilterReport || Time.unscaledTime < nextFilterReport)
        {
            return;
        }

        lastFilterReport = signature;
        nextFilterReport = Time.unscaledTime + 2f;

        if (Cheats.DrawItemFilter.Count == 0)
        {
            Plugin.Log.LogInfo($"绘制：还没有勾选任何物品，所以屏幕上一件都不画（扫到 {groundTotal} 件地面物品）。");
            return;
        }

        Plugin.Log.LogWarning(
            $"绘制：勾选了 {Cheats.DrawItemFilter.Count} 种，扫到 {itemBuffer.Count} 件物品"
            + $"（其中地面 {groundTotal} 件，匹配勾选 {matchedTotal} 件），"
            + $"{Cheats.DrawDistance:F0} 米内一件都没画出来。"
            + $"地面样例「{SampleGroundName()}」，勾选样例「{SampleKey()}」。");
    }

    private string SampleGroundName()
    {
        foreach (var item in itemBuffer)
        {
            if (item != null && item.itemState == ItemState.Ground)
            {
                return item.name;
            }
        }

        return "无";
    }

    private static string SampleKey()
    {
        foreach (var key in Cheats.DrawItemFilter)
        {
            return key;
        }

        return "无";
    }

    private void HideItemMarkers()
    {
        foreach (var pair in itemMarkers)
        {
            if (pair.Value.Root != null)
            {
                pair.Value.Root.gameObject.SetActive(false);
            }
        }
    }

    private Marker GetItemMarker(Item item)
    {
        if (itemMarkers.TryGetValue(item, out var existing) && existing.Root != null)
        {
            return existing;
        }

        // 屏幕空间的固定尺寸：不随距离缩放（远近距离看到的都是同样大小），
        // 而且刻意做得小，远处物品多的时候才不会糊住半个屏幕。
        var marker = CreateMarker("ItemMarker", SmallMarkerSize, SmallMarkerFontSize, ItemMarkerBg);
        itemMarkers[item] = marker;
        return marker;
    }

    // ---------------------------------------------------------------- 行李箱 / 雕像标记

    /// <summary>
    /// 行李箱（复活宝箱也是 <see cref="Luggage"/> 的子类）和雕像都不是 <c>Item</c>，
    /// 所以它们不在物品目录里、也没法被勾选表筛出来，单独给两个开关。
    /// </summary>
    private void UpdateLuggageMarkers()
    {
        var local = Character.localCharacter;
        var camera = ResolveCamera();
        if (!Cheats.DrawLuggage || local == null || camera == null)
        {
            HidePooledMarkers(luggageMarkers);
            return;
        }

        RefreshLuggageBuffer();

        DrawPooledMarkers(
            luggageMarkers,
            seenLuggage,
            staleLuggage,
            luggageBuffer,
            LuggageAnchor,
            LuggageLabel,
            MaxLuggageMarkers,
            "LuggageMarker",
            LuggageMarkerBg,
            camera,
            CharacterBody.Position(local),
            Mathf.Max(5f, Cheats.DrawDistance));
    }

    private void UpdateStatueMarkers()
    {
        var local = Character.localCharacter;
        var camera = ResolveCamera();
        if (!Cheats.DrawStatues || local == null || camera == null)
        {
            HidePooledMarkers(statueMarkers);
            return;
        }

        RefreshStatueBuffer();

        DrawPooledMarkers(
            statueMarkers,
            seenStatues,
            staleStatues,
            statueBuffer,
            StatueAnchorOf,
            StatueLabelOf,
            MaxStatueMarkers,
            "StatueMarker",
            StatueMarkerBg,
            camera,
            CharacterBody.Position(local),
            Mathf.Max(5f, Cheats.DrawDistance));
    }

    private void RefreshLuggageBuffer()
    {
        if (Time.unscaledTime < nextLuggageScan)
        {
            return;
        }

        nextLuggageScan = Time.unscaledTime + 1.5f;
        luggageBuffer.Clear();

        try
        {
            luggageBuffer.AddRange(FindObjectsByType<Luggage>(FindObjectsSortMode.None));
        }
        catch (Exception)
        {
            // 场景切换的瞬间可能失败，下一轮再扫。
        }

        // 兜底：游戏自己维护的列表里还活着的也补进来（去重）。
        foreach (var luggage in Luggage.ALL_LUGGAGE)
        {
            if (luggage != null && !luggageBuffer.Contains(luggage))
            {
                luggageBuffer.Add(luggage);
            }
        }
    }

    /// <summary>
    /// 定期收集"雕像"标记。两类东西都算：
    ///
    /// 1. <see cref="ScoutStatue"/>（石头童军：插 4 个护符换童军的荣耀的那种石像）；
    /// 2. 石像手里抱着的那颗护符本身 —— 世界里还有"抱着护符的石像"身上并没有
    ///    <c>ScoutStatue</c> 组件（那颗护符是单独的生成物），只按组件扫是扫不到的。
    ///    把护符也标出来，标记正好落在石像手上，标签直接就是护符名字（童军的野心……）。
    ///
    /// <c>FindObjectsInactive.Include</c> 是必须的：游戏同一时刻只激活玩家所在的那一段地图
    /// （<c>MapHandler</c> 会把其它分段整个 SetActive(false)），只找激活对象的话，
    /// 别的生物群落的雕像永远画不出来。
    /// </summary>
    private void RefreshStatueBuffer()
    {
        if (Time.unscaledTime < nextStatueScan)
        {
            return;
        }

        nextStatueScan = Time.unscaledTime + 1.5f;
        statueBuffer.Clear();
        statuePoses.Clear();

        try
        {
            foreach (var statue in FindObjectsByType<ScoutStatue>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (statue == null)
                {
                    continue;
                }

                var name = SafeName(statue.GetName, statue.gameObject.name);
                var held = HeldAmuletName(statue);
                AddStatue(
                    statue,
                    statue.transform.position + (Vector3.up * 1.2f),
                    held == null ? name : $"{name}（{held}）");
            }

            AddAmuletStatues();
        }
        catch (Exception)
        {
            // 场景切换的瞬间可能失败，下一轮再扫。
        }
    }

    /// <summary>石像手里那颗护符的名字：护符由 master 生成在 <c>gemSpot</c> 上。</summary>
    private string? HeldAmuletName(ScoutStatue statue)
    {
        var spot = statue.gemSpot;
        if (spot == null)
        {
            return null;
        }

        var position = spot.position;
        var closest = 1.5f;   // 米：再远就不算"拿在手里"了
        Item? found = null;

        foreach (var item in ScannableItems())
        {
            if (item == null || item.itemState != ItemState.Ground)
            {
                continue;
            }

            var distance = Vector3.Distance(position, item.transform.position);
            if (distance < closest)
            {
                closest = distance;
                found = item;
            }
        }

        return found == null ? null : SafeName(found.GetName, found.gameObject.name);
    }

    /// <summary>
    /// 把"抱着护符的石像"也标出来：这类石像身上没有 <c>ScoutStatue</c> 组件，
    /// 只能顺着它手里那颗护符把它找出来。
    /// </summary>
    private void AddAmuletStatues()
    {
        foreach (var item in ScannableItems())
        {
            if (item == null || item.itemState != ItemState.Ground || !item.TryGetComponent<AmuletBase>(out _))
            {
                continue;
            }

            // 已经在某座 ScoutStatue 的标签里写过了，就别再叠一个标记。
            if (NearExistingStatue(item.transform.position, 3f))
            {
                continue;
            }

            AddStatue(item, item.transform.position, SafeName(item.GetName, item.gameObject.name));
        }
    }

    /// <summary>场景里的物品（连停用分段里的一起）：护符常常挂在还没加载的分段里。</summary>
    private List<Item> ScannableItems()
    {
        statueItemScratch.Clear();

        try
        {
            statueItemScratch.AddRange(FindObjectsByType<Item>(FindObjectsInactive.Include, FindObjectsSortMode.None));
        }
        catch (Exception)
        {
            // 场景切换的瞬间可能失败，下一轮再扫。
        }

        return statueItemScratch;
    }

    private bool NearExistingStatue(Vector3 position, float radius)
    {
        foreach (var pose in statuePoses.Values)
        {
            if (Vector3.Distance(pose.Anchor, position) <= radius)
            {
                return true;
            }
        }

        return false;
    }

    private void AddStatue(Component owner, Vector3 anchor, string label)
    {
        if (owner == null || statuePoses.Count >= MaxStatueMarkers * 5)
        {
            return;
        }

        statueBuffer.Add(owner);
        statuePoses[owner] = new StatuePose { Anchor = anchor, Label = label };
    }

    private static Vector3 LuggageAnchor(Luggage luggage)
    {
        try
        {
            // 行李箱是个大箱子，用整体包围盒中心才不会偏到箱底。
            return luggage.Center();
        }
        catch (Exception)
        {
            return luggage.transform.position + (Vector3.up * 0.6f);
        }
    }

    private static string LuggageLabel(Luggage luggage)
    {
        var name = SafeName(luggage.GetName, luggage.gameObject.name);
        return luggage.IsOpen ? name + "（已打开）" : name;
    }

    private Vector3 StatueAnchorOf(Component owner)
    {
        return statuePoses.TryGetValue(owner, out var pose) ? pose.Anchor : Vector3.zero;
    }

    private string StatueLabelOf(Component owner)
    {
        return statuePoses.TryGetValue(owner, out var pose) ? pose.Label : string.Empty;
    }

    /// <summary>一次扫描算出来的一个雕像标记（位置 / 文字都提前定好）。</summary>
    private sealed class StatuePose
    {
        internal Vector3 Anchor;

        internal string Label = string.Empty;
    }

    /// <summary>
    /// 通用的「一批目标 → 固定大小标记」逻辑：行李箱、雕像这类数量不多、
    /// 不需要勾选表的目标都走这里。
    /// </summary>
    private void DrawPooledMarkers<T>(
        Dictionary<T, Marker> pool,
        HashSet<T> seen,
        List<T> stale,
        List<T> targets,
        Func<T, Vector3> anchorOf,
        Func<T, string> labelOf,
        int max,
        string nodeName,
        Color background,
        Camera camera,
        Vector3 origin,
        float range)
        where T : Component
    {
        seen.Clear();

        var drawn = 0;
        foreach (var target in targets)
        {
            if (target == null || drawn >= max)
            {
                continue;
            }

            var world = anchorOf(target);
            var distance = Vector3.Distance(origin, world);
            if (distance > range)
            {
                continue;
            }

            drawn++;
            seen.Add(target);

            var marker = GetPooledMarker(pool, target, nodeName, background);
            var screen = camera.WorldToScreenPoint(world);

            if (screen.z <= 0f
                || screen.x < -200f || screen.x > Screen.width + 200f
                || screen.y < -200f || screen.y > Screen.height + 200f)
            {
                marker.Root.gameObject.SetActive(false);
                continue;
            }

            marker.Root.gameObject.SetActive(true);
            marker.Root.anchoredPosition = ScreenToCanvas(screen);
            marker.Label.text = $"{labelOf(target)}  {distance:F0}m";
        }

        stale.Clear();
        foreach (var pair in pool)
        {
            if (pair.Key == null || !seen.Contains(pair.Key))
            {
                stale.Add(pair.Key!);
            }
        }

        foreach (var target in stale)
        {
            if (target != null && pool.TryGetValue(target, out var marker) && marker.Root != null)
            {
                Destroy(marker.Root.gameObject);
            }

            pool.Remove(target!);
        }
    }

    private Marker GetPooledMarker<T>(Dictionary<T, Marker> pool, T key, string nodeName, Color background)
        where T : Component
    {
        if (pool.TryGetValue(key, out var existing) && existing.Root != null)
        {
            return existing;
        }

        var marker = CreateMarker(nodeName, SmallMarkerSize, SmallMarkerFontSize, background);
        pool[key] = marker;
        return marker;
    }

    private static void HidePooledMarkers<T>(Dictionary<T, Marker> pool)
        where T : Component
    {
        foreach (var pair in pool)
        {
            if (pair.Value.Root != null)
            {
                pair.Value.Root.gameObject.SetActive(false);
            }
        }
    }

    // ---------------------------------------------------------------- 生物标记

    /// <summary>
    /// 会被标记的生物类型。
    ///
    /// 光扫 <see cref="Mob"/> 是不够的 —— 那是之前只画得出甲虫 / 蝎子 / 青蛙舌的原因：
    /// PEAK 里只有这三个继承了 <c>Mob</c>，蜜蜂群、蘑菇僵尸、风滚草、蜘蛛、蚁狮、秃鹰、
    /// 水豚这些各有各的基类（<c>MonoBehaviourPun</c> / <c>MonoBehaviour</c>…），
    /// 游戏里既没有统一的父类也没有统一的接口（<c>MobManager</c> 收的也只是 <c>Mob</c>），
    /// 所以只能把类型列出来一个一个扫。
    ///
    /// <c>FindObjectsByType</c> 是认派生类的，所以列 <see cref="Mob"/> 一项就能覆盖它的三个子类。
    /// </summary>
    private static readonly Type[] CreatureTypes =
    {
        typeof(Mob),                     // 甲虫 / 蝎子 / 青蛙舌
        typeof(BeeSwarm),
        typeof(MushroomZombie),
        typeof(TumbleWeed),
        typeof(Spider),
        typeof(Antlion),
        typeof(Condor),
        typeof(BotBoar),
        typeof(Capybara),
        typeof(VenusFlyTrap),
        typeof(SlipperyJellyfish),
        typeof(Skelleton),
        typeof(BingBong),
        typeof(Scoutmaster),
        typeof(Peak.Island.VoiceThief),  // 偷声音的乌鸦
    };

    /// <summary>
    /// 标记上的显示名。按 <c>GetType()</c> 查（拿到的是实际类型，比如 <c>Mob</c> 上会得到
    /// <c>Beetle</c> / <c>Scorpion</c>），查不到就退回物体名 —— 别的模组塞进来的生物也能画出来。
    /// </summary>
    private static readonly Dictionary<Type, string> CreatureNames = new Dictionary<Type, string>
    {
        { typeof(Beetle), "甲虫" },
        { typeof(Scorpion), "蝎子" },
        { typeof(FrogTongue), "青蛙舌" },
        { typeof(BeeSwarm), "蜜蜂群" },
        { typeof(MushroomZombie), "蘑菇僵尸" },
        { typeof(TumbleWeed), "风滚草" },
        { typeof(Spider), "蜘蛛" },
        { typeof(Antlion), "蚁狮" },
        { typeof(Condor), "秃鹰" },
        { typeof(BotBoar), "野猪机器人" },
        { typeof(Capybara), "水豚" },
        { typeof(VenusFlyTrap), "食人花" },
        { typeof(SlipperyJellyfish), "水母" },
        { typeof(Skelleton), "骷髅" },
        { typeof(BingBong), "BingBong" },
        { typeof(Scoutmaster), "童子军大师" },
        { typeof(Peak.Island.VoiceThief), "乌鸦" },
    };

    private void UpdateMobMarkers()
    {
        var local = Character.localCharacter;
        if (!Cheats.DrawMobs || local == null)
        {
            HideMobMarkers();
            return;
        }

        var camera = ResolveCamera();
        if (camera == null)
        {
            HideMobMarkers();
            return;
        }

        RefreshMobBuffer();

        seenMobs.Clear();
        nearMobs.Clear();

        var range = Mathf.Max(5f, Cheats.DrawMobDistance);
        var rangeSquared = range * range;
        var origin = CharacterBody.Position(local);

        foreach (var mob in mobBuffer)
        {
            if (mob == null || !mob.gameObject.activeInHierarchy)
            {
                continue;
            }

            if ((MobAnchor(mob) - origin).sqrMagnitude > rangeSquared)
            {
                continue;
            }

            nearMobs.Add(mob);
        }

        // 太多的时候只留最近的，别把屏幕糊满。
        if (nearMobs.Count > MaxMobMarkers)
        {
            nearMobs.Sort((a, b) => (MobAnchor(a) - origin).sqrMagnitude
                .CompareTo((MobAnchor(b) - origin).sqrMagnitude));
            nearMobs.RemoveRange(MaxMobMarkers, nearMobs.Count - MaxMobMarkers);
        }

        foreach (var mob in nearMobs)
        {
            seenMobs.Add(mob);

            var world = MobAnchor(mob);
            var distance = Vector3.Distance(origin, world);

            var marker = GetMobMarker(mob);
            var screen = camera.WorldToScreenPoint(world);

            if (screen.z <= 0f
                || screen.x < -200f || screen.x > Screen.width + 200f
                || screen.y < -200f || screen.y > Screen.height + 200f)
            {
                marker.Root.gameObject.SetActive(false);
                continue;
            }

            marker.Root.gameObject.SetActive(true);
            marker.Root.anchoredPosition = ScreenToCanvas(screen);
            marker.Label.text = $"{MobLabel(mob)}  {distance:F0}m";
        }

        staleMobs.Clear();
        foreach (var pair in mobMarkers)
        {
            if (pair.Key == null || !seenMobs.Contains(pair.Key))
            {
                staleMobs.Add(pair.Key!);
            }
        }

        foreach (var mob in staleMobs)
        {
            if (mob != null && mobMarkers.TryGetValue(mob, out var marker) && marker.Root != null)
            {
                Destroy(marker.Root.gameObject);
            }

            mobMarkers.Remove(mob!);
        }
    }

    /// <summary>
    /// 定期把场景里的生物扫一遍。
    ///
    /// 一种类型一次 <c>FindObjectsByType</c>，一轮十几次，所以跟物品 / 行李箱 / 雕像
    /// 一个节奏（1.5 秒一轮），中间那些帧直接复用上一轮的结果。
    /// </summary>
    private void RefreshMobBuffer()
    {
        if (Time.unscaledTime < nextMobScan)
        {
            return;
        }

        nextMobScan = Time.unscaledTime + 1.5f;
        mobBuffer.Clear();
        mobLabels.Clear();

        foreach (var type in CreatureTypes)
        {
            try
            {
                foreach (var found in FindObjectsByType(type, FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                {
                    if (found is not Component creature)
                    {
                        continue;
                    }

                    var key = CreatureKey(creature);
                    if (key == null || mobLabels.ContainsKey(key))
                    {
                        continue;
                    }

                    mobBuffer.Add(key);
                    mobLabels[key] = CreatureName(creature);
                }
            }
            catch (Exception)
            {
                // 场景切换的瞬间可能失败，下一轮再扫。
            }
        }
    }

    /// <summary>
    /// 一只生物的统一 key。
    ///
    /// 有一半的"怪"本身就是 <see cref="Character"/>（蘑菇僵尸、野猪机器人、骷髅、童子军大师…），
    /// 这类生物的根节点是出生点、装完就再也不动了，位置只能走 <see cref="CharacterBody"/>，
    /// 所以直接拿 Character 当 key。
    /// 其余生物（甲虫、蜘蛛、秃鹰…）是普通刚体，用 <see cref="PhotonView"/> 当 key 就够了：
    /// 生物都是联网对象，视图挂在根节点上，扫到哪一层子物体的组件都会折到同一个 key。
    /// 这里刻意不用 <c>transform.root</c> —— 地图生成会把一大堆生物挂到同一个父节点下，
    /// 那样整张图的生物会被折成一个标记。
    /// </summary>
    private static Component? CreatureKey(Component creature)
    {
        try
        {
            var character = creature.GetComponent<Character>();
            if (character == null)
            {
                character = creature.GetComponentInParent<Character>();
            }

            if (character != null)
            {
                return character;
            }

            var view = creature.GetComponentInParent<PhotonView>(true);
            if (view != null)
            {
                return view;
            }
        }
        catch (Exception)
        {
            // 忽略，退回组件本身。
        }

        return creature;
    }

    /// <summary>
    /// 标记的挂点。
    ///
    /// key 落在 <see cref="Character"/> 上时坐标必须取躯干刚体：这类生物的根节点是出生点，
    /// 读 <c>transform.position</c> 会永远画在它出生的地方 —— 看起来就是"标记不跟着它走"。
    /// </summary>
    private static Vector3 MobAnchor(Component mob)
    {
        var character = mob as Character;
        var origin = character != null ? CharacterBody.Position(character) : mob.transform.position;
        return origin + (Vector3.up * 1.2f);
    }

    /// <summary>标记上显示的名字：先查表，查不到就用物体名（去掉 "(Clone)"）。</summary>
    private static string CreatureName(Component creature)
    {
        return CreatureNames.TryGetValue(creature.GetType(), out var name)
            ? name
            : TrimClone(creature.gameObject.name, "生物");
    }

    private string MobLabel(Component mob)
    {
        return mobLabels.TryGetValue(mob, out var label)
            ? label
            : TrimClone(mob.gameObject.name, "生物");
    }

    private void HideMobMarkers()
    {
        foreach (var pair in mobMarkers)
        {
            if (pair.Value.Root != null)
            {
                pair.Value.Root.gameObject.SetActive(false);
            }
        }
    }

    private Marker GetMobMarker(Component mob)
    {
        if (mobMarkers.TryGetValue(mob, out var existing) && existing.Root != null)
        {
            return existing;
        }

        var marker = CreateMarker("MobMarker", new Vector2(210f, 28f), 16f, MobMarkerBg);
        mobMarkers[mob] = marker;
        return marker;
    }

    // ---------------------------------------------------------------- 工具

    /// <summary>
    /// 把 <see cref="Camera.WorldToScreenPoint"/> 得到的屏幕像素坐标转换成画布局部坐标。
    /// Canvas 用了 ScaleWithScreenSize（参考分辨率 1920×1080），画布的单位和屏幕像素
    /// 并不是 1:1，直接把像素值当 anchoredPosition 用会让标记随分辨率 / 视角漂移。
    /// </summary>
    private Vector2 ScreenToCanvas(Vector3 screenPoint)
    {
        if (root != null
            && RectTransformUtility.ScreenPointToLocalPointInRectangle(root, screenPoint, null, out var local))
        {
            return local;
        }

        return new Vector2(screenPoint.x, screenPoint.y);
    }

    /// <summary>
    /// 造一个固定屏幕尺寸的标记（圆角底 + 一行字）。
    ///
    /// 两个坑：
    /// 1. 文字直接挂在节点上，而不是挂在底图下面，免得被兄弟层级的绘制顺序坑到；
    /// 2. 用 Overflow 而不是 Ellipsis —— 标记框只有二十来个像素高，
    ///    只要行高比框高一点点，Ellipsis 会把整行都截掉，屏幕上就只剩下一个空黑框。
    /// </summary>
    private Marker CreateMarker(string name, Vector2 size, float fontSize, Color background)
    {
        var node = UiFactory.Node(name, root);
        node.anchorMin = new Vector2(0.5f, 0.5f);
        node.anchorMax = new Vector2(0.5f, 0.5f);
        node.pivot = new Vector2(0.5f, 0f);
        node.sizeDelta = size;

        var bg = UiFactory.Panel(node, UiFactory.MarkerSprite, background, false, "Bg");
        UiFactory.Stretch(bg.rectTransform);

        var label = UiFactory.Label(node, string.Empty, fontSize, TextAlignmentOptions.Center, UiFactory.TextMain);
        UiFactory.Stretch(label.rectTransform, 5f, 0f);
        label.overflowMode = TextOverflowModes.Overflow;

        return new Marker { Root = node, Label = label };
    }

    /// <summary>优先用游戏本地化过的名字，取不到就退回物体名。</summary>
    private static string SafeName(Func<string> getName, string fallback)
    {
        try
        {
            var name = getName();
            if (!string.IsNullOrEmpty(name))
            {
                return name;
            }
        }
        catch (Exception)
        {
            // 本地化还没准备好时忽略。
        }

        return TrimClone(fallback);
    }

    /// <summary>去掉网络实例化对象名字里的 "(Clone)"。</summary>
    private static string TrimClone(string name, string fallback = "目标")
    {
        var index = name.IndexOf("(Clone)", StringComparison.Ordinal);
        if (index > 0)
        {
            name = name.Substring(0, index).Trim();
        }

        return string.IsNullOrEmpty(name) ? fallback : name;
    }

    private static Camera? ResolveCamera()
    {
        var main = MainCamera.instance;
        if (main != null)
        {
            var camera = main.GetComponent<Camera>();
            if (camera == null)
            {
                camera = main.GetComponentInChildren<Camera>();
            }

            if (camera != null)
            {
                return camera;
            }
        }

        return Camera.main;
    }

    private static string Ping()
    {
        try
        {
            return PhotonNetwork.GetPing().ToString();
        }
        catch (Exception)
        {
            return "-";
        }
    }

    /// <summary>
    /// 标记 / 体力条上的名字。
    ///
    /// 顺序：本模组刷的假人（名字自己记着，最准）→ 角色的 <c>characterName</c> → Photon 昵称。
    /// 真人队友的 <c>characterName</c> 是昵称过一遍脏话过滤之后才异步写上的，
    /// 过滤服务还没回话的时候是空的，这时候必须退回昵称，否则一屋子人全显示「队友」，谁也认不出谁。
    /// </summary>
    private static string Name(Character character)
    {
        try
        {
            var name = Dummies.DisplayName(character);

            if (string.IsNullOrEmpty(name))
            {
                name = character.characterName;
            }

            if (string.IsNullOrEmpty(name) || name == "Bot")
            {
                name = NickName(character);
            }

            return string.IsNullOrEmpty(name) ? "队友" : name;
        }
        catch (Exception)
        {
            return "队友";
        }
    }

    /// <summary>Photon 昵称；读不到（没联机 / 机器人 / 视图号 0）返回空。</summary>
    private static string? NickName(Character character)
    {
        try
        {
            var view = character.photonView;
            var owner = view != null ? view.Owner : null;
            var nick = owner != null ? owner.NickName : null;
            return string.IsNullOrEmpty(nick) ? null : nick;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
