using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.UI.ProceduralImage;

namespace PeakMasterMenu.UI;

/// <summary>
/// 屏幕上的物品提示框 —— 照搬自 <b>Peak Item Tooltip</b>（作者 knekly）的 <c>Widget</c>。
///
/// 结构（自下而上）：Canvas → 圆角黑底（带描边）→ 竖排布局：
/// 「图标 + 名称」一行，下面依次是类型、描述、数值。
/// 触发条件也和原版一致：优先显示手里拿着的物品，没拿就显示准星悬停的物品。
///
/// 描述文本来自 <see cref="ItemDescriptions"/>（和原版共用同一份 JSON），
/// 数值来自 <see cref="ItemModifiers"/>。
/// </summary>
internal sealed class ItemTooltip : MonoBehaviour
{
    /// <summary>同一个物品的内容多久刷一次（数值会因为状态变化）。</summary>
    private const float ContentRefreshInterval = 0.25f;

    /// <summary>多久检查一次 cfg / 描述文件有没有被改动。</summary>
    private const float MaintenanceInterval = 1f;

    private static readonly Color TypeFallbackColour = new Color(0.7f, 0.7f, 0.7f, 1f);

    private static readonly Color DefaultBorderColour = Color.white;

    private static readonly Color DescriptionColour = new Color(0.85f, 0.85f, 0.85f, 1f);

    private ItemDescriptions descriptions = null!;

    private GameObject? rootGo;
    private RectTransform root = null!;
    private ProceduralImage background = null!;
    private ProceduralImage border = null!;
    private RawImage icon = null!;
    private GameObject iconGo = null!;
    private LayoutElement iconLayout = null!;
    private TextMeshProUGUI nameText = null!;
    private TextMeshProUGUI typeText = null!;
    private TextMeshProUGUI descriptionText = null!;
    private TextMeshProUGUI modifierText = null!;

    private bool built;
    private Item? lastItem;
    private float nextContentRefresh;
    private float nextMaintenance;
    private bool configDirty;
    private DateTime configWriteUtc;

    /// <summary>建一个常驻的提示框对象。</summary>
    internal static ItemTooltip Create()
    {
        var go = new GameObject("PeakMasterMenu.ItemTooltip");
        DontDestroyOnLoad(go);
        return go.AddComponent<ItemTooltip>();
    }

    /// <summary>当前的提示框实例，菜单里改完配置就靠它立刻重画。</summary>
    private static ItemTooltip? Current;

    /// <summary>菜单里改动了配置，喊一声让下一帧立刻按新配置重画。</summary>
    internal static void MarkDirty()
    {
        if (Current != null)
        {
            Current.configDirty = true;
        }
    }

    private void Awake()
    {
        Current = this;
        descriptions = new ItemDescriptions();
        configWriteUtc = SafeConfigWriteTime();
    }

    private void OnDestroy()
    {
        if (Current == this)
        {
            Current = null;
        }
    }

    private void Update()
    {
        try
        {
            if (Time.unscaledTime >= nextMaintenance)
            {
                nextMaintenance = Time.unscaledTime + MaintenanceInterval;

                if (descriptions.MaybeReload())
                {
                    configDirty = true;
                }

                MaybeReloadConfig();
            }

            if (!ItemTooltipConfig.Enabled.Value)
            {
                Hide();
                lastItem = null;
                return;
            }

            var item = ResolveTargetItem();
            if (item == null)
            {
                Hide();
                lastItem = null;
                configDirty = false;
                return;
            }

            var changed = item != lastItem || Time.unscaledTime >= nextContentRefresh;
            if (!changed && !configDirty)
            {
                return;
            }

            lastItem = item;
            nextContentRefresh = Time.unscaledTime + ContentRefreshInterval;
            configDirty = false;

            Show(item);
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"刷新物品提示框失败: {e.Message}");
        }
    }

    /// <summary>手里拿着东西就优先看手里的，否则看准星悬停的物品（与游戏原版逻辑一致）。</summary>
    private static Item? ResolveTargetItem()
    {
        var interaction = Interaction.instance;
        if (interaction == null)
        {
            return null;
        }

        if (ItemTooltipConfig.ShowWhileHolding.Value)
        {
            var local = Character.localCharacter;
            var held = local != null && local.data != null ? local.data.currentItem : null;
            if (held != null)
            {
                return held;
            }
        }

        return interaction.currentHovered as Item;
    }

    private void Show(Item item)
    {
        EnsureBuilt();

        var ui = item.UIData;
        var key = ui != null ? ui.itemName : null;

        // 描述数据里没有这个物品就不显示 —— 和原版一样，
        // 免得弹出一个只有名字的空壳。
        if (!descriptions.TryGet(key, out var entry))
        {
            Hide();
            return;
        }

        Texture2D? texture = null;
        try
        {
            texture = ui != null ? ui.GetIcon() : null;
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"取物品图标失败: {e.Message}");
        }

        icon.texture = texture;
        nameText.text = item.GetItemName(null!);
        typeText.text = FormatTypes(entry.Types);
        descriptionText.text = entry.Description;
        modifierText.text = ItemModifiers.Format(item);

        ApplyConfig();
        SetVisible(true);
    }

    private void Hide()
    {
        if (!built)
        {
            return;
        }

        SetVisible(false);
    }

    private void SetVisible(bool visible)
    {
        if (rootGo != null)
        {
            rootGo.SetActive(visible);
        }
    }

    /// <summary>把物品类型拼成 <c>&lt;color=#…&gt;消耗品&lt;/color&gt; / …</c>。</summary>
    private string FormatTypes(List<string> types)
    {
        if (types.Count == 0)
        {
            return string.Empty;
        }

        var sb = new StringBuilder();

        for (var i = 0; i < types.Count; i++)
        {
            if (i > 0)
            {
                sb.Append(" / ");
            }

            var colour = descriptions.TryGetTypeColour(types[i], out var parsed) ? parsed : TypeFallbackColour;
            sb.Append("<color=#").Append(ColorUtility.ToHtmlStringRGB(colour)).Append('>')
              .Append(TypeName(types[i]))
              .Append("</color>");
        }

        return sb.ToString();
    }

    private static string TypeName(string type)
    {
        if (!ItemTooltipConfig.Chinese)
        {
            return type;
        }

        return type.ToUpperInvariant() switch
        {
            "MYSTICAL" => "神秘",
            "CONSUMABLE" => "消耗品",
            "DEPLOYABLE" => "可部署",
            "UNUSED" => "未使用",
            "EQUIPMENT" => "装备",
            "MISC" => "杂物",
            _ => type,
        };
    }

    // ---------------------------------------------------------------- 界面构建

    private void EnsureBuilt()
    {
        if (built)
        {
            return;
        }

        var canvasGo = new GameObject("PeakItemTooltipCanvas");
        canvasGo.transform.SetParent(transform, false);

        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 5000;

        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        // 背板：圆角黑底，位置和缩放由配置决定。
        rootGo = new GameObject("Widget");
        rootGo.transform.SetParent(canvasGo.transform, false);
        root = rootGo.AddComponent<RectTransform>();
        root.anchorMin = new Vector2(0.5f, 0.5f);
        root.anchorMax = new Vector2(0.5f, 0.5f);
        root.pivot = new Vector2(0.5f, 0.5f);

        background = rootGo.AddComponent<ProceduralImage>();
        background.color = new Color(0f, 0f, 0f, 0.75f);
        background.raycastTarget = false;
        rootGo.AddComponent<FreeModifier>().Radius = new Vector4(12f, 12f, 12f, 12f);

        var layout = rootGo.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(12, 12, 10, 10);
        layout.spacing = 6f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        layout.childAlignment = TextAnchor.UpperLeft;

        var fitter = rootGo.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        // 第一行：图标 + 名称，横向居中。
        var headerGo = new GameObject("Header");
        headerGo.transform.SetParent(root, false);
        headerGo.AddComponent<RectTransform>();
        var header = headerGo.AddComponent<HorizontalLayoutGroup>();
        header.spacing = 8f;
        header.childControlWidth = true;
        header.childControlHeight = true;
        header.childForceExpandWidth = false;
        header.childForceExpandHeight = false;
        header.childAlignment = TextAnchor.MiddleCenter;

        var iconGoLocal = new GameObject("Icon");
        iconGoLocal.transform.SetParent(headerGo.transform, false);
        iconGoLocal.AddComponent<RectTransform>();
        icon = iconGoLocal.AddComponent<RawImage>();
        icon.raycastTarget = false;
        iconLayout = iconGoLocal.AddComponent<LayoutElement>();

        nameText = MakeText("Name", headerGo.transform, 22f, FontStyles.Normal, Color.white);
        nameText.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

        typeText = MakeText("Type", root, 15f, FontStyles.Italic, TypeFallbackColour);
        descriptionText = MakeText("Description", root, 16f, FontStyles.Normal, DescriptionColour);
        modifierText = MakeText("Modifiers", root, 16f, FontStyles.Normal, Color.white);
        modifierText.richText = true;

        // 描边：铺满整个背板，但不参与布局。
        var borderGo = new GameObject("Border");
        borderGo.transform.SetParent(root, false);
        var borderRect = borderGo.AddComponent<RectTransform>();
        borderRect.anchorMin = Vector2.zero;
        borderRect.anchorMax = Vector2.one;
        borderRect.offsetMin = Vector2.zero;
        borderRect.offsetMax = Vector2.zero;
        borderGo.AddComponent<LayoutElement>().ignoreLayout = true;
        border = borderGo.AddComponent<ProceduralImage>();
        border.color = DefaultBorderColour;
        border.BorderWidth = 2f;
        border.raycastTarget = false;
        borderGo.AddComponent<FreeModifier>().Radius = new Vector4(12f, 12f, 12f, 12f);

        iconGo = iconGoLocal;
        built = true;
        SetVisible(false);
    }

    private static TextMeshProUGUI MakeText(string name, Transform parent, float size, FontStyles style, Color colour)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<RectTransform>();

        var text = go.AddComponent<TextMeshProUGUI>();
        text.font = UiFactory.Font;
        text.fontSize = size;
        text.fontStyle = style;
        text.color = colour;
        text.raycastTarget = false;
        text.richText = true;
        text.textWrappingMode = TextWrappingModes.Normal;
        return text;
    }

    /// <summary>把配置里的外观项应用一遍（每次显示都会走一次）。</summary>
    private void ApplyConfig()
    {
        if (!built)
        {
            return;
        }

        root.anchoredPosition = new Vector2(ItemTooltipConfig.OffsetX.Value, ItemTooltipConfig.OffsetY.Value);

        var scale = ItemTooltipConfig.Scale.Value;
        root.localScale = new Vector3(scale, scale, 1f);

        root.sizeDelta = new Vector2(ItemTooltipConfig.Width.Value, root.sizeDelta.y);

        var backgroundColour = background.color;
        backgroundColour.a = ItemTooltipConfig.BackgroundOpacity.Value;
        background.color = backgroundColour;

        var borderColour = ColorUtility.TryParseHtmlString(ItemTooltipConfig.BorderColour.Value, out var parsed)
            ? parsed
            : DefaultBorderColour;
        borderColour.a = ItemTooltipConfig.BorderOpacity.Value;
        border.color = borderColour;

        iconGo.SetActive(ItemTooltipConfig.ShowIcon.Value && icon.texture != null);
        nameText.gameObject.SetActive(ItemTooltipConfig.ShowName.Value);
        typeText.gameObject.SetActive(ItemTooltipConfig.ShowType.Value && !string.IsNullOrEmpty(typeText.text));
        descriptionText.gameObject.SetActive(ItemTooltipConfig.ShowDescription.Value && !string.IsNullOrEmpty(descriptionText.text));
        modifierText.gameObject.SetActive(ItemTooltipConfig.ShowModifiers.Value && !string.IsNullOrEmpty(modifierText.text));

        iconLayout.preferredWidth = ItemTooltipConfig.IconSize.Value;
        iconLayout.preferredHeight = ItemTooltipConfig.IconSize.Value;

        nameText.fontSize = ItemTooltipConfig.NameFontSize.Value;
        typeText.fontSize = ItemTooltipConfig.TypeFontSize.Value;
        descriptionText.fontSize = ItemTooltipConfig.DescriptionFontSize.Value;
        modifierText.fontSize = ItemTooltipConfig.ModifierFontSize.Value;

        LayoutRebuilder.ForceRebuildLayoutImmediate(root);
    }

    // ---------------------------------------------------------------- 热重载

    private DateTime SafeConfigWriteTime()
    {
        try
        {
            var path = Plugin.Instance != null ? Plugin.Instance.Config.ConfigFilePath : null;
            return !string.IsNullOrEmpty(path) && File.Exists(path)
                ? File.GetLastWriteTimeUtc(path)
                : DateTime.MinValue;
        }
        catch
        {
            return DateTime.MinValue;
        }
    }

    /// <summary>cfg 被外部改动过就重新读一遍（照着原版做的，方便一边改一边看效果）。</summary>
    private void MaybeReloadConfig()
    {
        var writeTime = SafeConfigWriteTime();
        if (writeTime == configWriteUtc)
        {
            return;
        }

        configWriteUtc = writeTime;

        try
        {
            Plugin.Instance?.Config.Reload();
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"重载物品提示框配置失败: {e.Message}");
        }

        configDirty = true;
    }
}
