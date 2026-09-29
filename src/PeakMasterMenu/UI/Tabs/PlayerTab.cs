using System;
using PeakMasterMenu.UI;

namespace PeakMasterMenu.UI.Tabs;

/// <summary>
/// 生存页：伤害、异常状态、体力、保命模式与收尾用的危险操作。
/// 所有开关关掉菜单后依然持续生效。
/// </summary>
internal sealed class PlayerTab : MenuTab
{
    internal override string Title => "生存";

    protected override void Build()
    {
        UiFactory.Section(Content, "保护");
        UiFactory.Switch(Content, "无敌（免疫一切伤害与异常）", Cheats.GodMode, value => Cheats.GodMode = value);
        UiFactory.Switch(Content, "无饥饿", Cheats.NoHunger, value => Cheats.NoHunger = value);
        UiFactory.Switch(Content, "免疫寒冷 · 中毒 · 诅咒", Cheats.ImmuneStatus, value => Cheats.ImmuneStatus = value);
        UiFactory.Switch(Content, "免疫坠落伤害", Cheats.NoFallDamage, value => Cheats.NoFallDamage = value);
        UiFactory.Hint(Content, "游戏里所有伤害其实都是一种「异常状态」，所以「无敌」会连同寒冷、中毒、诅咒、孢子一起挡掉。");

        UiFactory.Section(Content, "保命模式");
        UiFactory.ActionButton(Content, "立即关闭保命模式", SaveMode.Disable);
        UiFactory.Hint(
            Content,
            $"自己倒下后屏幕中央会弹出「按 {Plugin.SaveMeKey.Value} 紧急救援」。按下即复活并开启无敌 + 无限体力，"
            + $"屏幕右侧会出现状态提示，再按一次 {Plugin.SaveMeKey.Value} 关闭。"
            + "这个键只在这两种情况下才会被拦截，其它时候它依旧是游戏原本的功能。");

        UiFactory.Section(Content, "体力");
        UiFactory.Switch(Content, "无限体力", Cheats.InfiniteStamina, value => Cheats.InfiniteStamina = value);
        UiFactory.ActionButton(Content, "立刻回满体力", FillStamina);
        UiFactory.ActionButton(Content, "补满额外体力条", Cheats.FillExtraStamina);
        UiFactory.Hint(Content, "「额外体力」就是吃蘑菇汤 / 拿到士气加成时多出来的那条，上限是满格。");

        UiFactory.Section(Content, "恢复");
        UiFactory.ActionButton(Content, "清除所有负面状态", ClearStatus);
        UiFactory.ActionButton(Content, "解除石化", ClearPetrify);
        UiFactory.Hint(Content, "只影响你自己，不会动到队友身上的状态。");

        UiFactory.Section(Content, "死亡");
        UiFactory.Switch(
            Content,
            "死亡 / 晕倒时不掉落物品",
            Cheats.KeepItemsOnDeath,
            value => Cheats.KeepItemsOnDeath = value);
        UiFactory.Hint(
            Content,
            "打开后，死亡、晕倒、以及被复活时都不会把手上的物品和背包丢到地上。"
            + "与「保命模式」配合使用，倒下再爬起来装备都还在。");

        UiFactory.Section(Content, "危险操作");
        UiFactory.ActionButton(Content, "一键关闭所有作弊开关", ResetCheats);
        UiFactory.Hint(Content, "只关开关，不会重置滑块倍率。");
    }

    /// <summary>把本模组加的所有作弊开关关掉（不动滑块倍率），保命模式连带一起收。</summary>
    private static void ResetCheats()
    {
        Cheats.GodMode = false;
        Cheats.NoHunger = false;
        Cheats.ImmuneStatus = false;
        Cheats.NoFallDamage = false;
        Cheats.InfiniteStamina = false;
        Cheats.ExtraJump = false;
        Cheats.Fly = false;
        Cheats.NoClip = false;
        Cheats.Stealth = false;
        Cheats.NoSlip = false;
        Cheats.NoItemConsume = false;
        Cheats.GentleDrop = false;
        Cheats.KeepItemsOnDeath = false;
        Cheats.CarryLivingPlayers = false;
        Cheats.KeepBackpackWhenCarrying = false;
        Cheats.DrawItems = false;
        Cheats.DrawItemFilter.Clear();
        Cheats.DrawMobs = false;

        // 保命模式连着无敌和无限体力，一并关掉。
        SaveMode.Disable();
    }

    private static void FillStamina()
    {
        var character = Character.localCharacter;
        if (character == null || character.data == null)
        {
            return;
        }

        try
        {
            // 只补主条，额外体力条保持原样（另有单独的「补满额外体力条」按钮）。
            character.data.currentStamina = character.GetMaxStamina();
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"恢复体力失败: {e}");
        }
    }

    private static void ClearStatus()
    {
        var character = Character.localCharacter;
        if (character == null || character.refs == null || character.refs.afflictions == null)
        {
            return;
        }

        try
        {
            character.refs.afflictions.RemoveAllThorns();
            character.refs.afflictions.ClearAllStatus(excludeCurse: false, excludePetrify: false);
            character.data.SetPetrify(0);
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"清除状态失败: {e}");
        }
    }

    private static void ClearPetrify()
    {
        var character = Character.localCharacter;
        if (character == null || character.data == null)
        {
            return;
        }

        try
        {
            character.data.SetPetrify(0);
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"解除石化失败: {e}");
        }
    }
}
