using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace PeakMasterMenu.UI;

/// <summary>
/// 纯代码构建 UGUI 控件的工厂。
/// 字体直接复用游戏自身的 TMP 字体，因此在任何语言下都能正确显示中文。
/// </summary>
internal static class UiFactory
{
    internal const float RowHeight = 54f;

    // ---- 配色：贴近 PEAK 原生的暖色纸质感 ----
    internal static readonly Color PanelBg = new Color(0.086f, 0.078f, 0.067f, 0.965f);
    internal static readonly Color HeaderBg = new Color(0.157f, 0.137f, 0.114f, 1f);
    internal static readonly Color TabBg = new Color(0.145f, 0.129f, 0.110f, 0.95f);
    internal static readonly Color TabActiveBg = new Color(0.902f, 0.639f, 0.196f, 1f);
    internal static readonly Color ButtonBg = new Color(0.212f, 0.192f, 0.161f, 1f);
    internal static readonly Color OnBg = new Color(0.286f, 0.545f, 0.271f, 1f);
    internal static readonly Color OffBg = new Color(0.267f, 0.247f, 0.224f, 1f);
    internal static readonly Color TextMain = new Color(0.949f, 0.925f, 0.855f, 1f);
    internal static readonly Color TextMuted = new Color(0.678f, 0.643f, 0.580f, 1f);
    internal static readonly Color TextOnBrand = new Color(0.102f, 0.086f, 0.067f, 1f);

    private static TMP_FontAsset? font;
    private static Sprite? panelSprite;
    private static Sprite? buttonSprite;
    private static Sprite? pillSprite;
    private static Sprite? markerSprite;

    /// <summary>游戏自身的 TMP 字体（含中文回退表）。</summary>
    internal static TMP_FontAsset Font
    {
        get
        {
            if (font != null)
            {
                return font;
            }

            if (FontFallbackSwapper.instance != null && FontFallbackSwapper.instance.mainBaseFont != null)
            {
                font = FontFallbackSwapper.instance.mainBaseFont;
                EnsureChineseFallback(font);
                return font;
            }

            if (TMP_Settings.defaultFontAsset != null)
            {
                font = TMP_Settings.defaultFontAsset;
                EnsureChineseFallback(font);
                return font;
            }

            font = TMP_FontAsset.CreateFontAsset(Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));
            return font;
        }
    }

    /// <summary>
    /// 把游戏自带的简体中文字体补进回退表。
    /// 只增加字形来源、不替换原有字体，因此不会影响游戏本体的显示。
    /// </summary>
    private static void EnsureChineseFallback(TMP_FontAsset target)
    {
        var swapper = FontFallbackSwapper.instance;
        if (swapper == null || swapper.simplifiedChineseFont == null)
        {
            return;
        }

        if (target.fallbackFontAssetTable == null)
        {
            return;
        }

        if (!target.fallbackFontAssetTable.Contains(swapper.simplifiedChineseFont))
        {
            target.fallbackFontAssetTable.Add(swapper.simplifiedChineseFont);
        }
    }

    internal static Sprite PanelSprite => panelSprite ??= MakeRoundedSprite(48, 14);

    internal static Sprite ButtonSprite => buttonSprite ??= MakeRoundedSprite(32, 8);

    internal static Sprite PillSprite => pillSprite ??= MakeRoundedSprite(32, 16);

    /// <summary>
    /// 屏幕标记专用的小圆角底。
    /// 半径必须比标记框的一半高度小，否则九宫格拉伸时上下边框会互相挤在一起
    /// （圆形胶囊贴图放在 24 像素高的框里就是这样）。
    /// </summary>
    internal static Sprite MarkerSprite => markerSprite ??= MakeRoundedSprite(24, 8);

    // ---------------------------------------------------------------- 基础构件

    /// <summary>创建一个带 RectTransform 的空节点。</summary>
    internal static RectTransform Node(string name, Transform? parent = null)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        if (parent != null)
        {
            rt.SetParent(parent, false);
        }

        rt.localScale = Vector3.one;
        return rt;
    }

    /// <summary>创建一张图片。</summary>
    internal static Image Panel(
        Transform parent,
        Sprite? sprite = null,
        Color? color = null,
        bool raycastTarget = true,
        string name = "Panel")
    {
        var rt = Node(name, parent);
        var img = rt.gameObject.AddComponent<Image>();
        img.sprite = sprite;
        img.color = color ?? PanelBg;
        if (sprite != null)
        {
            img.type = Image.Type.Sliced;
        }

        img.raycastTarget = raycastTarget;
        return img;
    }

    /// <summary>创建一段文本。</summary>
    internal static TextMeshProUGUI Label(
        Transform parent,
        string text,
        float size = 24f,
        TextAlignmentOptions align = TextAlignmentOptions.Left,
        Color? color = null)
    {
        var rt = Node("Label", parent);
        var label = rt.gameObject.AddComponent<TextMeshProUGUI>();
        label.font = Font;
        label.text = text;
        label.fontSize = size;
        label.alignment = align;
        label.color = color ?? TextMain;
        label.raycastTarget = false;
        label.overflowMode = TextOverflowModes.Ellipsis;
        return label;
    }

    /// <summary>把节点的锚点拉伸铺满父节点。</summary>
    internal static void Stretch(RectTransform rt, float padX = 0f, float padY = 0f)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(padX, padY);
        rt.offsetMax = new Vector2(-padX, -padY);
    }

    // ---------------------------------------------------------------- 行容器

    /// <summary>在滚动内容区里创建一个固定高度的行。</summary>
    internal static RectTransform Row(Transform parent, float height = RowHeight, string name = "Row")
    {
        var rt = Node(name, parent);
        var layout = rt.gameObject.AddComponent<LayoutElement>();
        layout.preferredHeight = height;
        layout.minHeight = height;
        return rt;
    }

    /// <summary>分组标题。</summary>
    internal static TextMeshProUGUI Section(Transform parent, string title)
    {
        var rt = Row(parent, 42f, "Section");
        var label = Label(rt, title, 22f, TextAlignmentOptions.Left, TabActiveBg);
        Stretch(label.rectTransform, 6f, 0f);
        return label;
    }

    /// <summary>说明性文字。</summary>
    internal static TextMeshProUGUI Hint(Transform parent, string text)
    {
        var rt = Row(parent, 34f, "Hint");
        var label = Label(rt, text, 18f, TextAlignmentOptions.Left, TextMuted);
        Stretch(label.rectTransform, 6f, 0f);
        return label;
    }

    // ---------------------------------------------------------------- 交互控件

    /// <summary>创建一块可点击的行（带背景与居中文字）。</summary>
    internal static Button Button(
        Transform parent,
        string label,
        UnityAction? onClick,
        float fontSize = 24f,
        Color? background = null,
        Color? textColor = null)
    {
        var img = Panel(parent, ButtonSprite, background ?? ButtonBg, name: "Button");
        var button = img.gameObject.AddComponent<Button>();
        button.targetGraphic = img;
        ApplyButtonColors(button);

        if (onClick != null)
        {
            button.onClick.AddListener(onClick);
        }

        var text = Label(img.transform, label, fontSize, TextAlignmentOptions.Center, textColor ?? TextMain);
        Stretch(text.rectTransform, 10f, 2f);
        return button;
    }

    /// <summary>创建一个可点击的动作按钮行。</summary>
    internal static Button ActionButton(
        Transform parent,
        string label,
        UnityAction onClick,
        float height = RowHeight,
        Color? background = null)
    {
        var rt = Row(parent, height, "Action");
        var button = Button(rt, label, onClick, 24f, background);
        Stretch(button.image.rectTransform);
        return button;
    }

    /// <summary>创建一个开关行，点击整行切换。</summary>
    internal static Button Switch(Transform parent, string label, bool initial, UnityAction<bool> onChanged)
    {
        var state = initial;

        var rt = Row(parent, RowHeight, "Switch");
        var bg = Panel(rt, ButtonSprite, ButtonBg);
        Stretch(bg.rectTransform);

        var button = bg.gameObject.AddComponent<Button>();
        button.targetGraphic = bg;

        var text = Label(bg.transform, label, 24f, TextAlignmentOptions.Left, TextMain);
        Stretch(text.rectTransform, 18f, 2f);
        text.rectTransform.offsetMax = new Vector2(-140f, text.rectTransform.offsetMax.y);

        var chip = Panel(bg.transform, PillSprite, OffBg, raycastTarget: false, name: "Chip");
        chip.rectTransform.anchorMin = new Vector2(1f, 0.5f);
        chip.rectTransform.anchorMax = new Vector2(1f, 0.5f);
        chip.rectTransform.pivot = new Vector2(1f, 0.5f);
        chip.rectTransform.sizeDelta = new Vector2(104f, 34f);
        chip.rectTransform.anchoredPosition = new Vector2(-14f, 0f);

        var chipText = Label(chip.transform, "关", 20f, TextAlignmentOptions.Center, TextMuted);
        Stretch(chipText.rectTransform, 4f, 2f);

        void Refresh()
        {
            chip.color = state ? OnBg : OffBg;
            chipText.text = state ? "开" : "关";
            chipText.color = state ? TextMain : TextMuted;
        }

        Refresh();

        button.onClick.AddListener(() =>
        {
            state = !state;
            Refresh();
            onChanged(state);
        });

        return button;
    }

    /// <summary>创建一个滑条行。</summary>
    internal static Slider SliderRow(
        Transform parent,
        string label,
        float min,
        float max,
        float value,
        UnityAction<float> onChanged,
        string format = "0.##")
    {
        var rt = Row(parent, RowHeight, "Slider");
        var bg = Panel(rt, ButtonSprite, ButtonBg, raycastTarget: false);
        Stretch(bg.rectTransform);

        var text = Label(bg.transform, label, 22f, TextAlignmentOptions.Left, TextMain);
        text.rectTransform.anchorMin = new Vector2(0f, 0.5f);
        text.rectTransform.anchorMax = new Vector2(0f, 0.5f);
        text.rectTransform.pivot = new Vector2(0f, 0.5f);
        text.rectTransform.sizeDelta = new Vector2(300f, 40f);
        text.rectTransform.anchoredPosition = new Vector2(18f, 0f);

        var valueText = Label(bg.transform, value.ToString(format), 20f, TextAlignmentOptions.Right, TextMuted);
        valueText.rectTransform.anchorMin = new Vector2(1f, 0.5f);
        valueText.rectTransform.anchorMax = new Vector2(1f, 0.5f);
        valueText.rectTransform.pivot = new Vector2(1f, 0.5f);
        valueText.rectTransform.sizeDelta = new Vector2(120f, 34f);
        valueText.rectTransform.anchoredPosition = new Vector2(-14f, 0f);

        var sliderHolder = Node("Slider", bg.transform);
        sliderHolder.anchorMin = new Vector2(0f, 0.5f);
        sliderHolder.anchorMax = new Vector2(1f, 0.5f);
        sliderHolder.pivot = new Vector2(0.5f, 0.5f);
        sliderHolder.offsetMin = new Vector2(330f, -14f);
        sliderHolder.offsetMax = new Vector2(-150f, 14f);

        var track = Panel(sliderHolder, ButtonSprite, new Color(0.13f, 0.12f, 0.11f, 1f), name: "Track");
        Stretch(track.rectTransform);

        var fillArea = Node("Fill Area", sliderHolder);
        Stretch(fillArea, 12f, 0f);

        var fill = Panel(fillArea, ButtonSprite, TabActiveBg, raycastTarget: false, name: "Fill");
        fill.rectTransform.anchorMin = Vector2.zero;
        fill.rectTransform.anchorMax = Vector2.one;
        fill.rectTransform.sizeDelta = Vector2.zero;
        fill.rectTransform.anchoredPosition = Vector2.zero;

        var handleArea = Node("Handle Area", sliderHolder);
        Stretch(handleArea, 12f, 0f);

        var handle = Panel(handleArea, PillSprite, TextMain, name: "Handle");
        handle.rectTransform.anchorMin = new Vector2(0f, 0.5f);
        handle.rectTransform.anchorMax = new Vector2(0f, 0.5f);
        handle.rectTransform.sizeDelta = new Vector2(22f, 34f);

        var slider = sliderHolder.gameObject.AddComponent<Slider>();
        slider.fillRect = fill.rectTransform;
        slider.handleRect = handle.rectTransform;
        slider.targetGraphic = handle;
        slider.direction = Slider.Direction.LeftToRight;
        slider.minValue = min;
        slider.maxValue = max;
        slider.wholeNumbers = false;
        slider.value = value;

        slider.onValueChanged.AddListener(v =>
        {
            valueText.text = v.ToString(format);
            onChanged(v);
        });

        return slider;
    }

    /// <summary>创建一个竖排滚动视图，返回内容容器。</summary>
    internal static RectTransform ScrollView(Transform parent, out ScrollRect scrollRect)
    {
        var root = Node("ScrollView", parent);
        scrollRect = root.gameObject.AddComponent<ScrollRect>();

        var viewport = Node("Viewport", root);
        Stretch(viewport);
        var viewportImage = viewport.gameObject.AddComponent<Image>();
        viewportImage.color = new Color(0f, 0f, 0f, 0f);
        viewportImage.raycastTarget = true;
        viewport.gameObject.AddComponent<RectMask2D>();

        var content = Node("Content", viewport);
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(1f, 1f);
        content.pivot = new Vector2(0.5f, 1f);
        content.anchoredPosition = Vector2.zero;
        content.sizeDelta = Vector2.zero;

        var layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(12, 12, 12, 12);
        layout.spacing = 8f;
        layout.childAlignment = TextAnchor.UpperLeft;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        var fitter = content.gameObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        scrollRect.viewport = viewport;
        scrollRect.content = content;
        scrollRect.horizontal = false;
        scrollRect.vertical = true;
        scrollRect.movementType = ScrollRect.MovementType.Clamped;

        // 滚轮交给 WheelScroll：它先把一格的增量收敛成固定距离，再转发回 ScrollRect 处理。
        // 这里必须留 1：转发过去的增量会被这个系数再乘一次，设 0 就全被乘没了（滚不动）。
        scrollRect.scrollSensitivity = 1f;
        viewport.gameObject.AddComponent<WheelScroll>().Target = scrollRect;

        return content;
    }

    // ---------------------------------------------------------------- 复合容器

    /// <summary>
    /// 创建一个垂直排列、高度自适应的子列表。
    /// 用于把一整组控件当作单个元素塞进父级布局里。
    /// </summary>
    internal static RectTransform List(Transform parent, string name = "List", float spacing = 8f)
    {
        var rt = Node(name, parent);

        var layout = rt.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(0, 0, 0, 0);
        layout.spacing = spacing;
        layout.childAlignment = TextAnchor.UpperLeft;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        var fitter = rt.gameObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        return rt;
    }

    /// <summary>创建一行横向排列的按钮容器。</summary>
    internal static RectTransform HorizontalRow(Transform parent, float height = 46f, float spacing = 8f, string name = "Buttons")
    {
        var rt = Node(name, parent);

        var element = rt.gameObject.AddComponent<LayoutElement>();
        element.preferredHeight = height;
        element.minHeight = height;

        var layout = rt.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(0, 0, 0, 0);
        layout.spacing = spacing;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = true;

        return rt;
    }

    // ---------------------------------------------------------------- 数据行

    /// <summary>只读信息行：左边标题、右边数值。返回数值文本以便刷新。</summary>
    internal static TextMeshProUGUI ValueRow(Transform parent, string label, string value, float height = 40f)
    {
        var rt = Row(parent, height, "ValueRow");

        var title = Label(rt, label, 21f, TextAlignmentOptions.Left, TextMain);
        title.rectTransform.anchorMin = new Vector2(0f, 0.5f);
        title.rectTransform.anchorMax = new Vector2(0f, 0.5f);
        title.rectTransform.pivot = new Vector2(0f, 0.5f);
        title.rectTransform.sizeDelta = new Vector2(420f, 32f);
        title.rectTransform.anchoredPosition = new Vector2(18f, 0f);

        var val = Label(rt, value, 21f, TextAlignmentOptions.Right, TextMuted);
        val.rectTransform.anchorMin = new Vector2(0f, 0.5f);
        val.rectTransform.anchorMax = new Vector2(1f, 0.5f);
        val.rectTransform.pivot = new Vector2(1f, 0.5f);
        val.rectTransform.offsetMin = new Vector2(460f, -16f);
        val.rectTransform.offsetMax = new Vector2(-18f, 16f);
        return val;
    }

    /// <summary>
    /// 可增减的数值行。返回一个刷新委托，外部数据变化时调用它重新读取显示值。
    /// </summary>
    internal static Action StepperRow(
        Transform parent,
        string label,
        Func<int> getValue,
        Func<int> getMax,
        Action<int> setValue)
    {
        var rt = Row(parent, RowHeight, "Stepper");
        var bg = Panel(rt, ButtonSprite, ButtonBg, raycastTarget: false);
        Stretch(bg.rectTransform);

        var title = Label(bg.transform, label, 24f, TextAlignmentOptions.Left, TextMain);
        title.rectTransform.anchorMin = new Vector2(0f, 0.5f);
        title.rectTransform.anchorMax = new Vector2(0f, 0.5f);
        title.rectTransform.pivot = new Vector2(0f, 0.5f);
        title.rectTransform.sizeDelta = new Vector2(380f, 36f);
        title.rectTransform.anchoredPosition = new Vector2(18f, 0f);

        var valueText = Label(bg.transform, "-", 22f, TextAlignmentOptions.Center, TabActiveBg);
        valueText.rectTransform.anchorMin = new Vector2(1f, 0.5f);
        valueText.rectTransform.anchorMax = new Vector2(1f, 0.5f);
        valueText.rectTransform.pivot = new Vector2(1f, 0.5f);
        valueText.rectTransform.sizeDelta = new Vector2(150f, 34f);
        valueText.rectTransform.anchoredPosition = new Vector2(-146f, 0f);

        var plus = Button(bg.transform, "+", null, 26f, OnBg, TextMain);
        AnchorRight(plus.image.rectTransform, new Vector2(56f, 38f), -14f);

        var minus = Button(bg.transform, "-", null, 26f, ButtonBg, TextMain);
        AnchorRight(minus.image.rectTransform, new Vector2(56f, 38f), -78f);

        void Refresh()
        {
            var max = Mathf.Max(0, getMax());
            var value = Mathf.Clamp(getValue(), 0, max);
            valueText.text = max > 0 ? $"{value} / {max}" : value.ToString();
            minus.interactable = value > 0;
            plus.interactable = value < max;
        }

        minus.onClick.AddListener(() =>
        {
            setValue(Mathf.Max(0, getValue() - 1));
            Refresh();
        });

        plus.onClick.AddListener(() =>
        {
            setValue(Mathf.Min(getMax(), getValue() + 1));
            Refresh();
        });

        Refresh();
        return Refresh;
    }

    /// <summary>把一个节点贴到父节点右侧中部。</summary>
    internal static void AnchorRight(RectTransform rt, Vector2 size, float rightOffset)
    {
        rt.anchorMin = new Vector2(1f, 0.5f);
        rt.anchorMax = new Vector2(1f, 0.5f);
        rt.pivot = new Vector2(1f, 0.5f);
        rt.sizeDelta = size;
        rt.anchoredPosition = new Vector2(rightOffset, 0f);
    }

    // ---------------------------------------------------------------- 物品图标

    private static readonly Dictionary<Texture2D, Sprite> iconSprites = new();

    /// <summary>
    /// 把游戏的 Texture2D 图标转换成 Sprite。
    /// 同一张贴图只会转换一次并缓存，避免每次打开菜单都产生新的 Sprite 对象。
    /// </summary>
    internal static Sprite? IconSprite(Texture2D? texture)
    {
        if (texture == null)
        {
            return null;
        }

        if (iconSprites.TryGetValue(texture, out var cached) && cached != null)
        {
            return cached;
        }

        try
        {
            var sprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, texture.width, texture.height),
                new Vector2(0.5f, 0.5f),
                100f,
                0,
                SpriteMeshType.FullRect);
            sprite.name = "PeakMasterMenu.Icon." + texture.name;
            iconSprites[texture] = sprite;
            return sprite;
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"转换图标失败（{texture.name}）: {e.Message}");
            return null;
        }
    }

    /// <summary>带图标的动作按钮：左侧是物品图标，右侧是名称。可额外挂一个右键回调。</summary>
    internal static Button IconButton(
        Transform parent,
        Sprite? icon,
        string label,
        UnityAction onClick,
        float height = 48f,
        UnityAction? onRightClick = null,
        Color? background = null)
    {
        var rt = Row(parent, height, "IconAction");
        var image = Panel(rt, ButtonSprite, background ?? ButtonBg, name: "Button");
        Stretch(image.rectTransform);

        var button = image.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        ApplyButtonColors(button);
        if (onClick != null)
        {
            button.onClick.AddListener(onClick);
        }

        if (onRightClick != null)
        {
            image.gameObject.AddComponent<RightClickHandler>().Handler = onRightClick;
        }

        var textLeft = 16f;
        if (icon != null)
        {
            var glyphSize = height - 14f;
            var glyphNode = Node("Icon", image.transform);
            glyphNode.anchorMin = new Vector2(0f, 0.5f);
            glyphNode.anchorMax = new Vector2(0f, 0.5f);
            glyphNode.pivot = new Vector2(0f, 0.5f);
            glyphNode.sizeDelta = new Vector2(glyphSize, glyphSize);
            glyphNode.anchoredPosition = new Vector2(9f, 0f);

            var glyph = glyphNode.gameObject.AddComponent<Image>();
            glyph.sprite = icon;
            glyph.preserveAspect = true;
            glyph.raycastTarget = false;
            textLeft = 9f + glyphSize + 14f;
        }

        var text = Label(image.transform, label, 21f, TextAlignmentOptions.Left, TextMain);
        text.rectTransform.anchorMin = Vector2.zero;
        text.rectTransform.anchorMax = Vector2.one;
        text.rectTransform.offsetMin = new Vector2(textLeft, 2f);
        text.rectTransform.offsetMax = new Vector2(-12f, -2f);

        return button;
    }

    /// <summary>
    /// 正方形图标格子：上面是物品图标，下面是名字。
    /// 配合 <see cref="GridRow"/> 使用就能拼出「一排好几个」的物品网格。
    /// </summary>
    internal static Button IconTile(
        Transform parent,
        Sprite? icon,
        string label,
        UnityAction onClick,
        UnityAction? onRightClick,
        float size,
        bool highlighted,
        Color? accent = null)
    {
        var node = Node("Tile", parent);

        var element = node.gameObject.AddComponent<LayoutElement>();
        element.minWidth = size;
        element.preferredWidth = size;
        element.minHeight = size;
        element.preferredHeight = size;

        var image = Panel(node, ButtonSprite, highlighted ? accent ?? TabActiveBg : ButtonBg, name: "Button");
        Stretch(image.rectTransform);

        var button = image.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        ApplyButtonColors(button);
        button.onClick.AddListener(onClick);

        if (onRightClick != null)
        {
            image.gameObject.AddComponent<RightClickHandler>().Handler = onRightClick;
        }

        const float textHeight = 40f;

        if (icon != null)
        {
            var glyphNode = Node("Icon", image.transform);
            glyphNode.anchorMin = new Vector2(0f, 1f);
            glyphNode.anchorMax = new Vector2(1f, 1f);
            glyphNode.pivot = new Vector2(0.5f, 1f);
            glyphNode.offsetMin = new Vector2(12f, 0f);
            glyphNode.offsetMax = new Vector2(-12f, -12f);
            glyphNode.sizeDelta = new Vector2(glyphNode.sizeDelta.x, size - textHeight - 24f);

            var glyph = glyphNode.gameObject.AddComponent<Image>();
            glyph.sprite = icon;
            glyph.preserveAspect = true;
            glyph.raycastTarget = false;
        }

        var text = Label(
            image.transform,
            label,
            17f,
            TextAlignmentOptions.Center,
            highlighted ? TextOnBrand : TextMain);
        text.overflowMode = TextOverflowModes.Ellipsis;
        text.rectTransform.anchorMin = new Vector2(0f, 0f);
        text.rectTransform.anchorMax = new Vector2(1f, 0f);
        text.rectTransform.pivot = new Vector2(0.5f, 0f);
        text.rectTransform.offsetMin = new Vector2(8f, 8f);
        text.rectTransform.offsetMax = new Vector2(-8f, 8f + textHeight);

        return button;
    }

    /// <summary>
    /// 一行网格容器。和 <see cref="HorizontalRow"/> 的区别是不拉伸子物体，
    /// 这样每个格子都能保持正方形（宽度就是 <paramref name="cellHeight"/>）。
    /// </summary>
    internal static RectTransform GridRow(Transform parent, float cellHeight, float spacing, string name = "GridRow")
    {
        var rt = Node(name, parent);

        var element = rt.gameObject.AddComponent<LayoutElement>();
        element.minHeight = cellHeight;
        element.preferredHeight = cellHeight;

        var layout = rt.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(0, 0, 0, 0);
        layout.spacing = spacing;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = true;

        return rt;
    }

    /// <summary>
    /// 互斥的按钮组，用作分类切换。
    /// 返回一个刷新委托：当选中项在外部被改变时调用它，可以同步高亮状态。
    /// </summary>
    internal static Action ChipGroup(
        Transform parent,
        IReadOnlyList<string> labels,
        int perRow,
        Func<int> getSelected,
        Action<int> onSelected)
    {
        var entries = new List<(Button Button, TextMeshProUGUI Text)>();

        for (var start = 0; start < labels.Count; start += perRow)
        {
            var row = HorizontalRow(parent, 44f, 8f, "Chips");
            var end = Mathf.Min(start + perRow, labels.Count);

            for (var i = start; i < end; i++)
            {
                var index = i;
                var chip = Button(row, labels[index], () =>
                {
                    onSelected(index);
                    Refresh();
                }, 20f, TabBg, TextMuted);

                entries.Add((chip, chip.GetComponentInChildren<TextMeshProUGUI>()));
            }

            // 补齐空位，让最后一行与上面的行保持同样的按钮宽度。
            for (var i = end; i < start + perRow; i++)
            {
                var spacer = Node("Spacer", row);
                var element = spacer.gameObject.AddComponent<LayoutElement>();
                element.minWidth = 0f;
                element.preferredWidth = 0f;
            }
        }

        void Refresh()
        {
            var selected = getSelected();
            for (var i = 0; i < entries.Count; i++)
            {
                var active = i == selected;
                entries[i].Button.image.color = active ? TabActiveBg : TabBg;
                if (entries[i].Text != null)
                {
                    entries[i].Text.color = active ? TextOnBrand : TextMuted;
                }
            }
        }

        Refresh();
        return Refresh;
    }

    private static void ApplyButtonColors(Button button)
    {
        var colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1.2f, 1.2f, 1.2f, 1f);
        colors.pressedColor = new Color(0.82f, 0.82f, 0.82f, 1f);
        colors.selectedColor = Color.white;
        colors.disabledColor = new Color(0.6f, 0.6f, 0.6f, 0.6f);
        colors.fadeDuration = 0.05f;
        button.colors = colors;
    }

    // ---------------------------------------------------------------- 搜索框

    /// <summary>
    /// 单行搜索框。TMP_InputField 需要手工搭出 Viewport / 文本 / 占位符三层结构，
    /// 任何一步失败都返回 null，调用方要做好降级。
    /// </summary>
    internal static TMP_InputField? SearchField(
        Transform parent,
        string placeholder,
        UnityAction<string> onChanged,
        float height = 44f)
    {
        try
        {
            var rt = Row(parent, height, "Search");
            var background = Panel(rt, ButtonSprite, ButtonBg, name: "Input");
            Stretch(background.rectTransform);

            var viewport = Node("Text Area", background.transform);
            Stretch(viewport, 14f, 6f);
            viewport.gameObject.AddComponent<RectMask2D>();

            var text = Label(viewport, string.Empty, 21f, TextAlignmentOptions.Left, TextMain);
            Stretch(text.rectTransform);

            var hint = Label(viewport, placeholder, 21f, TextAlignmentOptions.Left, TextMuted);
            Stretch(hint.rectTransform);

            var input = background.gameObject.AddComponent<TMP_InputField>();
            input.targetGraphic = background;
            input.textViewport = viewport;
            input.textComponent = text;
            input.placeholder = hint;
            input.lineType = TMP_InputField.LineType.SingleLine;
            input.richText = false;
            input.restoreOriginalTextOnEscape = false;
            input.onValueChanged.AddListener(onChanged);
            return input;
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"创建搜索框失败: {e.Message}");
            return null;
        }
    }

    // ---------------------------------------------------------------- 圆角贴图

    /// <summary>用代码生成一张九宫格圆角贴图，避免依赖任何美术资源。</summary>
    private static Sprite MakeRoundedSprite(int size, int radius)
    {
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            name = $"PeakMasterMenu.Rounded.{size}.{radius}",
        };

        var pixels = new Color32[size * size];
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var alpha = CornerAlpha(x, y, size, radius);
                pixels[(y * size) + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(alpha * 255f));
            }
        }

        texture.SetPixels32(pixels);
        texture.Apply();

        var border = new Vector4(radius, radius, radius, radius);
        return Sprite.Create(
            texture,
            new Rect(0f, 0f, size, size),
            new Vector2(0.5f, 0.5f),
            100f,
            0,
            SpriteMeshType.FullRect,
            border);
    }

    /// <summary>计算单像素的圆角覆盖率（0~1）。</summary>
    private static float CornerAlpha(int x, int y, int size, int radius)
    {
        var left = x;
        var right = size - 1 - x;
        var bottom = y;
        var top = size - 1 - y;

        var dx = 0f;
        var dy = 0f;

        if (left < radius)
        {
            dx = radius - left - 0.5f;
        }
        else if (right < radius)
        {
            dx = radius - right - 0.5f;
        }

        if (bottom < radius)
        {
            dy = radius - bottom - 0.5f;
        }
        else if (top < radius)
        {
            dy = radius - top - 0.5f;
        }

        if (dx <= 0f || dy <= 0f)
        {
            return 1f;
        }

        var distance = Mathf.Sqrt((dx * dx) + (dy * dy));
        return Mathf.Clamp01(radius - distance + 0.5f);
    }
}
