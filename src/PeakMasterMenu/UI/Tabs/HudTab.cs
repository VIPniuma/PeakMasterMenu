using System;
using UnityEngine.UI;

namespace PeakMasterMenu.UI.Tabs;

/// <summary>
/// 界面页：菜单窗口自身的外观，以及常驻屏幕的 HUD（坐标 / 延迟、体力条、地图、时间）。
///
/// 屏幕绘制那几组（队友标记、地面物品、怪物、行李箱、雕像）单独占一个页签，
/// 见 <see cref="DrawTab"/>。
/// </summary>
internal sealed class HudTab : MenuTab
{
    private Slider opacitySlider = null!;
    private Slider scaleSlider = null!;

    internal override string Title => "界面";

    protected override void Build()
    {
        UiFactory.Section(Content, "菜单窗口");
        opacitySlider = UiFactory.SliderRow(
            Content,
            "菜单透明度",
            0.35f,
            1f,
            Plugin.PanelOpacity.Value,
            value =>
            {
                Plugin.PanelOpacity.Value = value;
                Plugin.RefreshPanel();
            },
            "0.00");
        scaleSlider = UiFactory.SliderRow(
            Content,
            "菜单缩放",
            0.6f,
            1.6f,
            Plugin.PanelScale.Value,
            value =>
            {
                Plugin.PanelScale.Value = value;
                Plugin.RefreshPanel();
            },
            "0.00x");
        UiFactory.ActionButton(Content, "窗口移回屏幕中央", ResetPosition);
        UiFactory.Hint(Content, "按住窗口顶部标题栏就能拖动整个菜单，松手后位置会自动保存。");

        UiFactory.Section(Content, "屏幕 HUD");
        UiFactory.Switch(
            Content,
            "显示坐标 / 延迟",
            Plugin.ShowHudInfo.Value,
            value => Plugin.ShowHudInfo.Value = value);
        UiFactory.Switch(
            Content,
            "显示附近队友体力条",
            Plugin.ShowStaminaBars.Value,
            value => Plugin.ShowStaminaBars.Value = value);
        UiFactory.SliderRow(
            Content,
            "队友体力条数量上限",
            0f,
            8f,
            Plugin.TeammateBarLimit.Value,
            value => Plugin.TeammateBarLimit.Value = (int)Math.Round(value),
            "0");
        UiFactory.SliderRow(
            Content,
            "队友体力条显示距离",
            1f,
            200f,
            Plugin.TeammateBarProximity.Value,
            value => Plugin.TeammateBarProximity.Value = value,
            "0m");
        UiFactory.SliderRow(
            Content,
            "队友体力条缩放",
            0.2f,
            1f,
            Plugin.TeammateBarScale.Value,
            value => Plugin.TeammateBarScale.Value = value,
            "0.00x");
        UiFactory.Switch(
            Content,
            "显示队友背包格子",
            Plugin.ShowTeammateInventory.Value,
            value => Plugin.ShowTeammateInventory.Value = value);
        UiFactory.Switch(
            Content,
            "体力条上显示数值",
            Plugin.ShowStaminaInfo.Value,
            value => Plugin.ShowStaminaInfo.Value = value);
        UiFactory.SliderRow(
            Content,
            "体力条数值字号",
            8f,
            40f,
            Plugin.StaminaInfoFontSize.Value,
            value => Plugin.StaminaInfoFontSize.Value = value,
            "0");
        UiFactory.Hint(
            Content,
            "队友体力条是照着雷霆商店的 PeakStatsEx（MIT 许可）复刻的："
            + "直接复制游戏原生那条体力条来显示队友，所以外形、配色、进出场动画和自己那条完全一致，"
            + "数字按「每 6 像素一格」换算，条多长数字就多大。"
            + "「数量上限」和「显示距离」共同决定同时显示几条。");
        UiFactory.Switch(
            Content,
            "右上角显示本局地图",
            Plugin.ShowMapInfo.Value,
            value => Plugin.ShowMapInfo.Value = value);
        UiFactory.Switch(
            Content,
            "正上方显示游戏时间",
            Plugin.ShowClock.Value,
            value => Plugin.ShowClock.Value = value);
        UiFactory.Section(Content, "物品提示框");
        UiFactory.Switch(
            Content,
            "显示物品提示框",
            ItemTooltipConfig.Enabled.Value,
            value => ApplyTooltip(() => ItemTooltipConfig.Enabled.Value = value));
        UiFactory.Switch(
            Content,
            "手持物品时也显示",
            ItemTooltipConfig.ShowWhileHolding.Value,
            value => ApplyTooltip(() => ItemTooltipConfig.ShowWhileHolding.Value = value));
        UiFactory.Switch(
            Content,
            "显示物品描述",
            ItemTooltipConfig.ShowDescription.Value,
            value => ApplyTooltip(() => ItemTooltipConfig.ShowDescription.Value = value));
        UiFactory.Switch(
            Content,
            "显示物品数值",
            ItemTooltipConfig.ShowModifiers.Value,
            value => ApplyTooltip(() => ItemTooltipConfig.ShowModifiers.Value = value));
        UiFactory.SliderRow(
            Content,
            "提示框缩放",
            0.5f,
            3f,
            ItemTooltipConfig.Scale.Value,
            value => ApplyTooltip(() => ItemTooltipConfig.Scale.Value = value),
            "0.00x");
        UiFactory.SliderRow(
            Content,
            "提示框横向位置",
            -960f,
            960f,
            ItemTooltipConfig.OffsetX.Value,
            value => ApplyTooltip(() => ItemTooltipConfig.OffsetX.Value = value),
            "0");
        UiFactory.SliderRow(
            Content,
            "提示框纵向位置",
            -540f,
            540f,
            ItemTooltipConfig.OffsetY.Value,
            value => ApplyTooltip(() => ItemTooltipConfig.OffsetY.Value = value),
            "0");
        UiFactory.Hint(
            Content,
            "悬停或手持物品时显示它的图标、类型、描述和数值。"
            + "描述文字来自 BepInEx/config/peak-item-tooltip.descriptions.zh-CN.json，"
            + "那是个普通文本文件，可以自己改，改完会自动重载；"
            + "背景透明度、描边、字号这些在配置文件里调。");

        UiFactory.Hint(
            Content,
            "这些都是常驻 HUD，菜单关掉之后仍然显示。"
            + "画在目标头上的那些（队友标记 / 地面物品 / 怪物 / 行李箱 / 雕像）全部移到了「绘制」页。");
    }

    /// <summary>改完配置立刻喊提示框重画，不然要等下一次自动重载。</summary>
    private static void ApplyTooltip(Action apply)
    {
        apply();
        ItemTooltip.MarkDirty();
    }

    private void ResetPosition()
    {
        Plugin.PanelOffsetX.Value = 0f;
        Plugin.PanelOffsetY.Value = 0f;
        Plugin.RefreshPanel();
    }
}
