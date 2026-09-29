using UnityEngine.UI;

namespace PeakMasterMenu.UI.Tabs;

/// <summary>
/// 攀爬那几组控件：墙面 / 藤蔓 / 绳索 / 攀爬辅助 / 体力恢复。
///
/// 游戏里「攀爬」其实分成三套彼此独立的实现（墙面 / 藤蔓 / 绳索），
/// 所以速度和体力消耗都拆开分别调节。
///
/// 本类不再单独占一个页签：它的内容由 <see cref="AbilityTab"/>（移动页）
/// 通过 <see cref="MenuTab.BuildInto"/> 接在飞行 / 穿墙后面。
/// </summary>
internal sealed class ClimbTab : MenuTab
{
    private Slider wallSpeed = null!;
    private Slider wallStamina = null!;
    private Slider vineSpeed = null!;
    private Slider vineStamina = null!;
    private Slider ropeSpeed = null!;
    private Slider ropeStamina = null!;
    private Slider staminaRegen = null!;

    internal override string Title => "攀爬";

    protected override void Build()
    {
        UiFactory.Section(Content, "墙面 / 攀爬把手");
        wallSpeed = UiFactory.SliderRow(
            Content,
            "攀爬速度倍率",
            0.2f,
            5f,
            Cheats.WallClimbSpeed,
            value => Cheats.WallClimbSpeed = value,
            "0.00x");
        wallStamina = UiFactory.SliderRow(
            Content,
            "体力消耗倍率",
            0f,
            3f,
            Cheats.WallClimbStamina,
            value => Cheats.WallClimbStamina = value,
            "0.00x");

        UiFactory.Section(Content, "藤蔓");
        vineSpeed = UiFactory.SliderRow(
            Content,
            "攀爬速度倍率",
            0.2f,
            5f,
            Cheats.VineClimbSpeed,
            value => Cheats.VineClimbSpeed = value,
            "0.00x");
        vineStamina = UiFactory.SliderRow(
            Content,
            "体力消耗倍率",
            0f,
            3f,
            Cheats.VineClimbStamina,
            value => Cheats.VineClimbStamina = value,
            "0.00x");

        UiFactory.Section(Content, "绳索");
        ropeSpeed = UiFactory.SliderRow(
            Content,
            "攀爬速度倍率",
            0.2f,
            5f,
            Cheats.RopeClimbSpeed,
            value => Cheats.RopeClimbSpeed = value,
            "0.00x");
        ropeStamina = UiFactory.SliderRow(
            Content,
            "体力消耗倍率",
            0f,
            3f,
            Cheats.RopeClimbStamina,
            value => Cheats.RopeClimbStamina = value,
            "0.00x");

        UiFactory.Section(Content, "攀爬辅助");
        UiFactory.Switch(Content, "攀爬不滑落（手不会松）", Cheats.NoSlip, value => Cheats.NoSlip = value);

        UiFactory.Section(Content, "体力恢复");
        staminaRegen = UiFactory.SliderRow(
            Content,
            "体力恢复速度",
            1f,
            5f,
            Cheats.StaminaRegen,
            value => Cheats.StaminaRegen = value,
            "0.00x");
        UiFactory.ActionButton(Content, "全部重置为默认", ResetAll);

        UiFactory.Hint(Content, "体力消耗倍率调到 0 就是该种攀爬不再消耗体力；1.00x 为游戏原始数值。");
        UiFactory.Hint(Content, "体力恢复速度是额外补充，只在没有攀爬、没有倒下时生效。");
    }

    private void ResetAll()
    {
        Cheats.WallClimbSpeed = 1f;
        Cheats.VineClimbSpeed = 1f;
        Cheats.RopeClimbSpeed = 1f;
        Cheats.WallClimbStamina = 1f;
        Cheats.VineClimbStamina = 1f;
        Cheats.RopeClimbStamina = 1f;
        Cheats.StaminaRegen = 1f;
        Cheats.NoSlip = false;

        wallSpeed.value = 1f;
        wallStamina.value = 1f;
        vineSpeed.value = 1f;
        vineStamina.value = 1f;
        ropeSpeed.value = 1f;
        ropeStamina.value = 1f;
        staminaRegen.value = 1f;
    }
}
