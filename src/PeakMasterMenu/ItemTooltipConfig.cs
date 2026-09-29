using BepInEx.Configuration;

namespace PeakMasterMenu;

/// <summary>
/// 物品提示框的全部可调项。
///
/// 这一整块功能照搬自 <b>Peak Item Tooltip</b>（作者 knekly），
/// 所以分组名、条目名和默认值都刻意跟原版保持一致 ——
/// 想把两边调成一模一样的效果，对着数值抄就行了。
/// </summary>
internal static class ItemTooltipConfig
{
    /// <summary>描述文件用哪一份：Auto / zh-CN / en。</summary>
    internal static ConfigEntry<string> Language = null!;

    /// <summary>提示框总开关。</summary>
    internal static ConfigEntry<bool> Enabled = null!;

    /// <summary>cfg 或描述 JSON 改动后自动重载。</summary>
    internal static ConfigEntry<bool> HotReload = null!;

    /// <summary>手里拿着东西时也显示。</summary>
    internal static ConfigEntry<bool> ShowWhileHolding = null!;

    internal static ConfigEntry<float> OffsetX = null!;
    internal static ConfigEntry<float> OffsetY = null!;
    internal static ConfigEntry<float> Scale = null!;
    internal static ConfigEntry<float> Width = null!;
    internal static ConfigEntry<float> BackgroundOpacity = null!;
    internal static ConfigEntry<string> BorderColour = null!;
    internal static ConfigEntry<float> BorderOpacity = null!;

    internal static ConfigEntry<bool> ShowIcon = null!;
    internal static ConfigEntry<bool> ShowName = null!;
    internal static ConfigEntry<bool> ShowType = null!;
    internal static ConfigEntry<bool> ShowDescription = null!;
    internal static ConfigEntry<bool> ShowModifiers = null!;

    internal static ConfigEntry<float> IconSize = null!;
    internal static ConfigEntry<float> NameFontSize = null!;
    internal static ConfigEntry<float> TypeFontSize = null!;
    internal static ConfigEntry<float> DescriptionFontSize = null!;
    internal static ConfigEntry<float> ModifierFontSize = null!;

    /// <summary>当前是否用中文那份描述文件（原版 <c>Zh.IsActive()</c> 的等价实现）。</summary>
    internal static bool Chinese
    {
        get
        {
            switch (Language.Value)
            {
                case "zh-CN":
                    return true;
                case "en":
                    return false;
                default:
                    // 枚举里 9 就是简中，跟游戏当前语言走。
                    return global::LocalizedText.CURRENT_LANGUAGE == (global::LocalizedText.Language)9;
            }
        }
    }

    internal static void Bind(ConfigFile config)
    {
        const string widget = "ItemTooltip";
        const string fields = "ItemTooltip.Fields";

        Language = config.Bind(
            widget,
            "Language",
            "Auto",
            "物品描述用哪一份：Auto（跟随游戏语言）、zh-CN 或 en。");

        Enabled = config.Bind(
            widget,
            "Enabled",
            true,
            "是否显示物品提示框（悬停，或按下面的开关在手持时显示）。");

        HotReload = config.Bind(
            widget,
            "HotReload",
            true,
            "改完 cfg 或物品描述 JSON 后自动重载，不用重启游戏。");

        ShowWhileHolding = config.Bind(
            widget,
            "ShowWhileHolding",
            true,
            "手里拿着东西时也显示提示框；关掉就只在把准星对准物品时显示。");

        OffsetX = config.Bind(widget, "OffsetX", -710f, "提示框横向位置，0 为屏幕中心。");
        OffsetY = config.Bind(widget, "OffsetY", -220f, "提示框纵向位置，0 为屏幕中心。");

        Scale = config.Bind(
            widget,
            "Scale",
            1.15f,
            new ConfigDescription("提示框整体缩放。", new AcceptableValueRange<float>(0.1f, 5f)));

        Width = config.Bind(widget, "Width", 320f, "提示框宽度。");

        BackgroundOpacity = config.Bind(
            widget,
            "BackgroundOpacity",
            0.75f,
            new ConfigDescription("背景不透明度。", new AcceptableValueRange<float>(0f, 1f)));

        BorderColour = config.Bind(widget, "BorderColour", "#FFFFFF", "描边颜色，格式 #RRGGBB。");

        BorderOpacity = config.Bind(
            widget,
            "BorderOpacity",
            1f,
            new ConfigDescription("描边不透明度。", new AcceptableValueRange<float>(0f, 1f)));

        ShowIcon = config.Bind(fields, "ShowIcon", true, "显示物品图标。");
        ShowName = config.Bind(fields, "ShowName", true, "显示物品名称。");
        ShowType = config.Bind(fields, "ShowType", true, "显示物品类型（神秘 / 消耗品 / 可部署……）。");
        ShowDescription = config.Bind(fields, "ShowDescription", true, "显示物品描述。");
        ShowModifiers = config.Bind(fields, "ShowModifiers", true, "显示物品数值（饥饿 / 中毒 / 额外耐力 / 石化等）。");

        IconSize = config.Bind(fields, "IconSize", 48f, "图标边长。");
        NameFontSize = config.Bind(fields, "NameFontSize", 22f, "名称字号。");
        TypeFontSize = config.Bind(fields, "TypeFontSize", 15f, "类型字号。");
        DescriptionFontSize = config.Bind(fields, "DescriptionFontSize", 16f, "描述字号。");
        ModifierFontSize = config.Bind(fields, "ModifierFontSize", 16f, "数值字号。");
    }
}
