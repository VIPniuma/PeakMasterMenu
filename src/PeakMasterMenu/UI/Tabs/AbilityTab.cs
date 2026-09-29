using UnityEngine.UI;

namespace PeakMasterMenu.UI.Tabs;

/// <summary>
/// 移动页：跳跃、速度、飞行、穿墙、隐身，再往下接着攀爬（墙面 / 藤蔓 / 绳索）与队友互动。
///
/// 攀爬本来是一个独立页签，但它调的也是「怎么移动」，和飞行 / 穿墙是同一件事，
/// 所以并到这里由 <see cref="ClimbTab"/> 顺序接在后面（见 <see cref="BuildInto"/>）。
/// </summary>
internal sealed class AbilityTab : MenuTab
{
    private readonly ClimbTab climb = new();

    private Slider speedSlider = null!;
    private Slider jumpSlider = null!;
    private Slider flySlider = null!;

    internal override string Title => "移动";

    protected override void Build()
    {
        UiFactory.Section(Content, "跳跃");
        UiFactory.Switch(Content, "无限二段跳", Cheats.ExtraJump, value => Cheats.ExtraJump = value);
        jumpSlider = UiFactory.SliderRow(
            Content,
            "跳跃力度倍率",
            0.5f,
            3f,
            Cheats.JumpMultiplier,
            value => Cheats.JumpMultiplier = value,
            "0.00x");

        UiFactory.Section(Content, "移动速度");
        speedSlider = UiFactory.SliderRow(
            Content,
            "移动速度倍率",
            0.5f,
            3f,
            Cheats.SpeedMultiplier,
            value => Cheats.SpeedMultiplier = value,
            "0.00x");
        UiFactory.ActionButton(Content, "重置为默认倍率", ResetMultipliers);
        UiFactory.Hint(Content, "倍率以乘法叠加，不会覆盖其它 Mod 或游戏本身的改动。");

        UiFactory.Section(Content, "飞行");
        UiFactory.Switch(Content, "自由飞行", Cheats.Fly, value => Cheats.Fly = value);
        flySlider = UiFactory.SliderRow(
            Content,
            "飞行速度",
            4f,
            40f,
            Cheats.FlySpeed,
            value => Cheats.FlySpeed = value,
            "0");
        UiFactory.Hint(Content, "W / A / S / D 平移，空格上升，左 Ctrl 或 C 下降。菜单打开时飞行输入会自动让位给输入框。");

        UiFactory.Section(Content, "穿墙");
        UiFactory.Switch(Content, "关闭角色碰撞（穿墙）", Cheats.NoClip, value => Cheats.NoClip = value);
        UiFactory.Hint(Content, "单独开启时会原地悬停，配合飞行才能穿过墙壁。关闭开关后碰撞会立刻恢复。");

        UiFactory.Section(Content, "隐蔽");
        UiFactory.Switch(Content, "隐身（敌人不锁定你）", Cheats.Stealth, value => Cheats.Stealth = value);
        UiFactory.Hint(Content, "对甲虫、蝎子、青蛙这类「Mob」敌人有效；已经咬上来的敌人会立刻放弃目标。");

        // 攀爬那几组直接接在后面：墙面 / 藤蔓 / 绳索 / 攀爬辅助 / 体力恢复。
        climb.BuildInto(Content);

        UiFactory.Section(Content, "队友互动");
        UiFactory.Switch(
            Content,
            "扛起活着的队友（对着队友按住 E）",
            Cheats.CarryLivingPlayers,
            value => Cheats.CarryLivingPlayers = value);
        UiFactory.Switch(
            Content,
            "扛人时自己的背包不掉落",
            Cheats.KeepBackpackWhenCarrying,
            value => Cheats.KeepBackpackWhenCarrying = value);
        UiFactory.Hint(Content, "原版只能扛晕倒的队友。打开后活着的队友也能扛，而且你自己背上的背包会留在身上。");
    }

    internal override void OnShown()
    {
        climb.OnShown();
    }

    internal override void Tick()
    {
        climb.Tick();
    }

    private void ResetMultipliers()
    {
        Cheats.SpeedMultiplier = 1f;
        Cheats.JumpMultiplier = 1f;
        Cheats.FlySpeed = 14f;

        if (speedSlider != null)
        {
            speedSlider.value = 1f;
        }

        if (jumpSlider != null)
        {
            jumpSlider.value = 1f;
        }

        if (flySlider != null)
        {
            flySlider.value = 14f;
        }
    }
}
