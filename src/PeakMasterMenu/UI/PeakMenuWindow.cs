using System;
using System.Collections.Generic;
using PeakMasterMenu.UI.Tabs;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PeakMasterMenu.UI;

/// <summary>
/// 继承游戏原生 <see cref="MenuWindow"/> 的菜单窗口。
/// 只要把自己加入 <see cref="MenuWindow.AllActiveWindows"/>，游戏就会自动
/// 屏蔽玩家输入（<see cref="GUIManager.windowBlockingInput"/>）并解锁鼠标
/// （<see cref="GUIManager.windowShowingCursor"/>），无需自己写任何 Hook。
/// </summary>
[DefaultExecutionOrder(-100)]
internal sealed class PeakMenuWindow : MenuWindow
{
    private const float PanelWidth = 1100f;
    private const float PanelHeight = 640f;
    private const float HeaderHeight = 92f;
    private const float TabBarHeight = 56f;

    private readonly List<MenuTab> tabs = new();
    private readonly List<TabButton> tabButtons = new();
    private readonly List<RectTransform> pages = new();

    private RectTransform canvasRoot = null!;
    private RectTransform panelRoot = null!;
    private CanvasGroup panelGroup = null!;
    private RectTransform tabBar = null!;
    private RectTransform pageHost = null!;
    private ScrollRect scrollRect = null!;
    private int activeTab = -1;

    // ---------------------------------------------------------------- 基类配置

    public override bool openOnStart => false;

    public override bool selectOnOpen => false;

    public override bool closeOnPause => false;

    public override bool closeOnUICancel => false;

    public override bool blocksPlayerInput => true;

    public override bool showCursorWhileOpen => true;

    public override bool autoHideOnClose => true;

    /// <summary>窗口显示时被激活的根节点（整个 Canvas）。</summary>
    public override GameObject panel => canvasRoot.gameObject;

    // ---------------------------------------------------------------- 生命周期

    private void Awake()
    {
        EnsureEventSystem();
        BuildCanvas();
        BuildTabs();
    }

    public override void Initialize()
    {
        // 此时 OnOpen 紧接着就会调用 OnShown，所以这里不重复通知页签。
        SelectTab(0, notify: false);
    }

    public override void OnOpen()
    {
        base.OnOpen();

        ApplyPanelSettings();
        EnsureActiveTab();
        tabs[activeTab].OnShown();
    }

    public override void Update()
    {
        try
        {
            // base.Update 会把自己压入导航栈（手柄导航用），并处理基类的关闭快捷键。
            base.Update();
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"更新菜单窗口状态时出错: {e.Message}");
        }

        if (!isOpen || activeTab < 0)
        {
            return;
        }

        try
        {
            tabs[activeTab].Tick();
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"刷新「{tabs[activeTab].Title}」页签时出错: {e}");
        }
    }

    /// <summary>
    /// 放在 LateUpdate 里处理 Esc：
    /// 此时输入采样（<see cref="CharacterInput.Sample"/>）已经完成，而
    /// <see cref="GUIManager.UpdatePaused"/> 还没执行，因此可以顺手把这次
    /// Esc 的暂停标记清掉，避免菜单关闭后又弹出游戏暂停菜单。
    /// </summary>
    private void LateUpdate()
    {
        if (!isOpen || !Plugin.CloseOnEsc.Value)
        {
            return;
        }

        if (!Input.GetKeyDown(KeyCode.Escape))
        {
            return;
        }

        ClearPauseInput();
        Close();
    }

    // ---------------------------------------------------------------- 对外接口

    /// <summary>打开 / 关闭菜单。</summary>
    internal void Toggle()
    {
        if (isOpen)
        {
            Close();
        }
        else
        {
            Open();
        }
    }

    /// <summary>把「界面」页里调好的缩放 / 透明度 / 位置重新应用到窗口上。</summary>
    internal void ApplyPanelSettings()
    {
        if (panelRoot == null)
        {
            return;
        }

        panelRoot.localScale = Vector3.one * Mathf.Clamp(Plugin.PanelScale.Value, 0.4f, 2f);
        panelRoot.anchoredPosition = new Vector2(Plugin.PanelOffsetX.Value, Plugin.PanelOffsetY.Value);

        if (panelGroup != null)
        {
            panelGroup.alpha = Mathf.Clamp(Plugin.PanelOpacity.Value, 0.1f, 1f);
        }
    }

    /// <summary>若窗口尚未初始化，切回第一页。</summary>
    private void EnsureActiveTab()
    {
        if (activeTab < 0 && tabs.Count > 0)
        {
            SelectTab(0);
        }
    }

    // ---------------------------------------------------------------- 构建界面

    private void BuildCanvas()
    {
        canvasRoot = UiFactory.Node("Canvas", transform);
        var canvas = canvasRoot.gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 30000;

        var scaler = canvasRoot.gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        canvasRoot.gameObject.AddComponent<GraphicRaycaster>();

        // 半透明遮罩：既压暗游戏画面，也挡住背后的误点击。
        var dim = UiFactory.Panel(canvasRoot, null, new Color(0f, 0f, 0f, 0.55f), true, "Dim");
        UiFactory.Stretch(dim.rectTransform);

        panelRoot = UiFactory.Node("Panel", canvasRoot);
        panelRoot.anchorMin = new Vector2(0.5f, 0.5f);
        panelRoot.anchorMax = new Vector2(0.5f, 0.5f);
        panelRoot.pivot = new Vector2(0.5f, 0.5f);
        panelRoot.anchoredPosition = new Vector2(Plugin.PanelOffsetX.Value, Plugin.PanelOffsetY.Value);
        panelRoot.sizeDelta = new Vector2(PanelWidth, PanelHeight);

        var background = panelRoot.gameObject.AddComponent<Image>();
        background.sprite = UiFactory.PanelSprite;
        background.type = Image.Type.Sliced;
        background.color = UiFactory.PanelBg;

        // 透明度 / 缩放统一交给 CanvasGroup，拖拽改的是 anchoredPosition。
        panelGroup = panelRoot.gameObject.AddComponent<CanvasGroup>();

        BuildHeader(panelRoot);
        BuildTabBar(panelRoot);
        BuildBody(panelRoot);
    }

    private void BuildHeader(RectTransform parent)
    {
        var header = UiFactory.Panel(parent, null, UiFactory.HeaderBg, true, "Header");
        header.rectTransform.anchorMin = new Vector2(0f, 1f);
        header.rectTransform.anchorMax = new Vector2(1f, 1f);
        header.rectTransform.pivot = new Vector2(0.5f, 1f);
        header.rectTransform.sizeDelta = new Vector2(0f, HeaderHeight);
        header.rectTransform.anchoredPosition = Vector2.zero;

        // 拖动标题栏即可挪动整个菜单。
        header.gameObject.AddComponent<PanelDragHandler>().Target = panelRoot;

        var title = UiFactory.Label(header.transform, "PEAK 菜单", 32f, TextAlignmentOptions.Left, UiFactory.TextMain);
        title.rectTransform.anchorMin = new Vector2(0f, 0.5f);
        title.rectTransform.anchorMax = new Vector2(0f, 0.5f);
        title.rectTransform.pivot = new Vector2(0f, 0.5f);
        title.rectTransform.sizeDelta = new Vector2(600f, 42f);
        title.rectTransform.anchoredPosition = new Vector2(28f, 14f);

        var hint = UiFactory.Label(
            header.transform,
            $"按住这里可以拖动窗口　·　按 {Plugin.ToggleKey.Value} 开关菜单" + (Plugin.CloseOnEsc.Value ? "，Esc 关闭" : string.Empty),
            18f,
            TextAlignmentOptions.Left,
            UiFactory.TextMuted);
        hint.rectTransform.anchorMin = new Vector2(0f, 0.5f);
        hint.rectTransform.anchorMax = new Vector2(0f, 0.5f);
        hint.rectTransform.pivot = new Vector2(0f, 0.5f);
        hint.rectTransform.sizeDelta = new Vector2(900f, 26f);
        hint.rectTransform.anchoredPosition = new Vector2(28f, -18f);

        var close = UiFactory.Button(
            header.transform,
            "关闭",
            Close,
            22f,
            UiFactory.ButtonBg,
            UiFactory.TextMain);
        var closeRt = close.image.rectTransform;
        closeRt.anchorMin = new Vector2(1f, 0.5f);
        closeRt.anchorMax = new Vector2(1f, 0.5f);
        closeRt.pivot = new Vector2(1f, 0.5f);
        closeRt.sizeDelta = new Vector2(104f, 44f);
        closeRt.anchoredPosition = new Vector2(-20f, 0f);
    }

    private void BuildTabBar(RectTransform parent)
    {
        var bar = UiFactory.Panel(parent, null, UiFactory.TabBg, true, "TabBar");
        tabBar = bar.rectTransform;
        tabBar.anchorMin = new Vector2(0f, 1f);
        tabBar.anchorMax = new Vector2(1f, 1f);
        tabBar.pivot = new Vector2(0.5f, 1f);
        tabBar.sizeDelta = new Vector2(0f, TabBarHeight);
        tabBar.anchoredPosition = new Vector2(0f, -HeaderHeight);

        var layout = bar.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(14, 14, 8, 8);
        layout.spacing = 8f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = true;
    }

    private void BuildBody(RectTransform parent)
    {
        var body = UiFactory.Node("Body", parent);
        body.anchorMin = Vector2.zero;
        body.anchorMax = Vector2.one;
        body.offsetMin = new Vector2(14f, 16f);
        body.offsetMax = new Vector2(-14f, -(HeaderHeight + TabBarHeight + 10f));

        var content = UiFactory.ScrollView(body, out scrollRect);
        UiFactory.Stretch((RectTransform)scrollRect.transform);
        pageHost = content;
    }

    /// <summary>
    /// 页签列表。
    ///
    /// 这里只列真正占一个页签的类：攀爬（<see cref="ClimbTab"/>）、时间（<see cref="WorldTab"/>）
    /// 的内容分别由移动 / 大厅页接在里面，不再单独成页；
    /// 绘制（<see cref="DrawTab"/>）单独占一页，方便直接找到。
    /// </summary>
    private void BuildTabs()
    {
        tabs.Add(new ItemsTab());   // 物品
        tabs.Add(new AbilityTab()); // 移动（含攀爬）
        tabs.Add(new PlayerTab());  // 生存（含保命模式）
        tabs.Add(new PlayersTab()); // 传送
        tabs.Add(new DrawTab());    // 绘制
        tabs.Add(new LobbyTab());   // 大厅（含时间）
        tabs.Add(new HudTab());     // 界面

        for (var i = 0; i < tabs.Count; i++)
        {
            var tab = tabs[i];

            // 页签按钮
            var index = i;
            var button = UiFactory.Button(
                tabBar,
                tab.Title,
                () => SelectTab(index),
                21f,
                UiFactory.TabBg,
                UiFactory.TextMuted);
            tabButtons.Add(new TabButton(button));

            // 页签内容
            var page = UiFactory.List(pageHost, $"Page.{tab.Title}");
            tab.Attach(page);
            page.gameObject.SetActive(false);
            pages.Add(page);
        }

        SelectTab(0);
    }

    // ---------------------------------------------------------------- 页签切换

    private void SelectTab(int index, bool notify = true)
    {
        if (index < 0 || index >= tabs.Count)
        {
            return;
        }

        for (var i = 0; i < pages.Count; i++)
        {
            var isActive = i == index;
            pages[i].gameObject.SetActive(isActive);
            tabButtons[i].SetActive(isActive);
        }

        activeTab = index;

        if (scrollRect != null)
        {
            scrollRect.verticalNormalizedPosition = 1f;
        }

        if (notify)
        {
            try
            {
                tabs[index].OnShown();
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"打开「{tabs[index].Title}」页签时出错: {e}");
            }
        }
    }

    // ---------------------------------------------------------------- 工具

    private static void ClearPauseInput()
    {
        if (Character.localCharacter != null)
        {
            Character.localCharacter.input.pauseWasPressed = false;
        }
    }

    /// <summary>
    /// 兜底：正常运行时游戏一定已经存在 EventSystem，这里只是为了防御极端情况。
    /// </summary>
    private static void EnsureEventSystem()
    {
        if (EventSystem.current != null)
        {
            return;
        }

        var go = new GameObject("PeakMasterMenu.EventSystem");
        DontDestroyOnLoad(go);
        go.AddComponent<EventSystem>();
        go.AddComponent<StandaloneInputModule>();
        Plugin.Log.LogWarning("场景中缺少 EventSystem，已创建一个备用事件系统。");
    }

    /// <summary>
    /// 拖动标题栏挪动整个菜单。
    /// 位移是按父节点的局部坐标算的，所以菜单自身的缩放不会影响拖动手感。
    /// </summary>
    private sealed class PanelDragHandler : MonoBehaviour, IPointerDownHandler, IDragHandler, IEndDragHandler
    {
        internal RectTransform Target = null!;

        private bool dragging;
        private Vector2 grabLocal;
        private Vector2 grabPosition;

        public void OnPointerDown(PointerEventData eventData)
        {
            var target = Target;
            if (target == null)
            {
                return;
            }

            var parent = target.parent as RectTransform;
            if (parent == null)
            {
                return;
            }

            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    parent, eventData.position, eventData.pressEventCamera, out var local))
            {
                grabLocal = local;
                grabPosition = target.anchoredPosition;
                dragging = true;
            }
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (!dragging || Target == null)
            {
                return;
            }

            var parent = Target.parent as RectTransform;
            if (parent == null)
            {
                return;
            }

            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    parent, eventData.position, eventData.pressEventCamera, out var local))
            {
                return;
            }

            Target.anchoredPosition = grabPosition + (local - grabLocal);
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (!dragging || Target == null)
            {
                return;
            }

            dragging = false;
            Plugin.PanelOffsetX.Value = Target.anchoredPosition.x;
            Plugin.PanelOffsetY.Value = Target.anchoredPosition.y;
        }
    }

    private sealed class TabButton
    {
        private readonly Image background;
        private readonly TextMeshProUGUI label;
        private static readonly Color ActiveBg = UiFactory.TabActiveBg;
        private static readonly Color IdleBg = UiFactory.TabBg;
        private static readonly Color ActiveText = UiFactory.TextOnBrand;
        private static readonly Color IdleText = UiFactory.TextMuted;

        internal TabButton(Button button)
        {
            background = button.image;
            label = button.GetComponentInChildren<TextMeshProUGUI>();
        }

        internal void SetActive(bool active)
        {
            background.color = active ? ActiveBg : IdleBg;
            if (label != null)
            {
                label.color = active ? ActiveText : IdleText;
            }
        }
    }
}
