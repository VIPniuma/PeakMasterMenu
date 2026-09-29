using System;
using System.Collections.Generic;
using Peak;
using Peak.Afflictions;
using PeakMasterMenu.UI;
using UnityEngine;

namespace PeakMasterMenu;

/// <summary>
/// 手持物品的「数值」摘要。
///
/// PEAK 的物品没有一张现成的数值表：一件物品会产生什么效果，全靠挂在它（含子物体）身上的
/// <see cref="ItemActionBase"/> 组件（使用 / 食用时执行）和 <c>ItemCooking</c> 上的
/// 烹饪行为来说明。这里读的就是游戏运行时同一份数据（也就是游戏真正执行的那些数值），
/// 再翻成一行行看得懂的中文，例如「中毒 -50 · 寒冷 +10 · 饱食 +20」。
///
/// 注意：物品效果组件在子物体上，必须用 <c>GetComponentsInChildren</c> 才能拿全，
/// 这一点和 <c>Item.GetItemActions()</c> 保持一致。
///
/// 刻度：游戏内部状态量是 0~1 的分数（<c>CharacterAfflictions.currentStatuses</c>，
/// 体力条满格 = 1）。界面上统一按「满体力 = 100」显示，所以凡是状态量、每次 / 每秒
/// 变化量都要过 <see cref="StatusScale"/>；倍率（攀爬倍率、移速倍率…）和个数（荆棘、
/// 次数）不是状态量，保持原值。
/// </summary>
internal static class HeldItemStats
{
    /// <summary>体力条满格在游戏里是 1.0，界面上按 100 显示。</summary>
    private const float StatusScale = 100f;

    private static readonly List<ItemActionBase> actionBuffer = new();
    private static readonly List<string> parts = new();
    private static readonly HashSet<string> seenParts = new();

    /// <summary>
    /// 物品在游戏里真正显示的名字（本地化 + 生/熟/烤糊/碳化前缀），
    /// 用的就是 <c>Item.GetItemName(ItemInstanceData)</c>——背包格子显示的也是它。
    /// </summary>
    internal static string DisplayName(Item? item)
    {
        if (item == null)
        {
            return string.Empty;
        }

        try
        {
            var name = item.GetItemName(item.data);
            if (!string.IsNullOrWhiteSpace(name))
            {
                return name;
            }
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"读取物品名称失败: {e.Message}");
        }

        return ItemCatalog.DisplayName(item);
    }

    /// <summary>拼出手持物品的数值行；没有任何可读数值时返回空串。</summary>
    internal static string Describe(Item? item)
    {
        if (item == null)
        {
            return string.Empty;
        }

        try
        {
            parts.Clear();
            seenParts.Clear();

            AppendUsage(item);
            AppendActions(item);
            AppendCooking(item);

            return parts.Count == 0 ? string.Empty : string.Join(" · ", parts);
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"读取手持物品数值失败: {e.Message}");
            return string.Empty;
        }
    }

    // ---------------------------------------------------------------- 用量 / 燃料

    private static void AppendUsage(Item item)
    {
        if (item.HasData(DataEntryKey.ItemUses))
        {
            var uses = item.GetData<OptionableIntItemData>(DataEntryKey.ItemUses);
            if (uses.HasData)
            {
                // totalUses 就是这件物品的最大次数（游戏里那个 X/Y 的分母）。
                Add(item.totalUses > 0 ? $"剩余 {uses.Value}/{item.totalUses}" : $"剩余 {uses.Value}");
            }
        }
        else if (item.HasData(DataEntryKey.UseRemainingPercentage))
        {
            var remaining = item.GetData<FloatItemData>(DataEntryKey.UseRemainingPercentage);
            if (remaining.Value < 0.999f)
            {
                Add($"剩余 {remaining.Value * 100f:0}%");
            }
        }

        if (item.HasData(DataEntryKey.Fuel))
        {
            var fuel = item.GetData<FloatItemData>(DataEntryKey.Fuel);
            Add($"燃料 {fuel.Value * 100f:0}%");
        }
    }

    // ---------------------------------------------------------------- 使用效果

    private static void AppendActions(Item item)
    {
        actionBuffer.Clear();

        // 和 Item.GetItemActions() 一样：效果组件挂在根物体或子物体上，得连子物体一起收。
        item.GetComponentsInChildren(true, actionBuffer);

        foreach (var action in actionBuffer)
        {
            AppendAction(action);
        }
    }

    private static void AppendAction(ItemActionBase? action)
    {
        switch (action)
        {
            case Action_ApplyAffliction apply:
                Add(DescribeAffliction(apply.affliction));
                if (apply.extraAfflictions != null)
                {
                    foreach (var extra in apply.extraAfflictions)
                    {
                        Add(DescribeAffliction(extra));
                    }
                }

                break;

            case Action_ModifyStatus modify:
                Add($"{StatusName(modify.statusType)} {SignedStatus(modify.changeAmount)}");
                break;

            case Action_RestoreHunger hunger:
                Add($"饱食 +{Scaled(hunger.restorationAmount)}");
                break;

            case Action_GiveExtraStamina stamina:
                Add($"额外体力 +{Scaled(stamina.amount)}");
                break;

            case Action_InflictPoison poison:
                Add($"中毒 +{Scaled(poison.poisonPerSecond)}/秒");
                break;

            case Action_ClearAllStatus:
                Add("清除异常状态");
                break;

            case Action_Numb numb:
                Add($"麻木 +{Scaled(numb.numbAmount)}");
                break;

            case Action_AddOrRemoveThorns thorns:
                Add($"荆棘 {Signed(thorns.thornCount)}");
                break;

            case Action_ApplyInfiniteStamina:
                Add("无限体力");
                break;

            case Action_ApplySuperJump:
                Add("超级跳");
                break;

            case Action_BecomeSkeleton:
                Add("变成骷髅");
                break;

            case Action_MoraleBoost:
                Add("士气加成");
                break;

            case Action_LaunchPlayer:
                Add("击飞");
                break;

            case Action_WarpRandomly:
            case Action_WarpToBiome:
            case Action_WarpToRandomPlayer:
                Add("传送");
                break;

            case Action_HealingGem gem:
                Add(DescribeAffliction(gem.healingAffliction));
                Add(DescribeAffliction(gem.invincibilityAffliction));
                break;
        }
    }

    // ---------------------------------------------------------------- 烹饪效果

    private static void AppendCooking(Item item)
    {
        ItemCooking? cooking;
        try
        {
            cooking = item.GetComponentInChildren<ItemCooking>(true);
        }
        catch (Exception)
        {
            return;
        }

        var behaviors = cooking != null ? cooking.additionalCookingBehaviors : null;
        if (behaviors == null)
        {
            return;
        }

        foreach (var behavior in behaviors)
        {
            switch (behavior)
            {
                case CookingBehavior_AdjustStatusInstantly instantly:
                    Add($"烤后 {StatusName(instantly.statusType)} {SignedStatus(instantly.amount)}");
                    break;

                case CookingBehavior_ChangeAfflictionTime change:
                    Add($"烤后 {DescribeAffliction(change.action != null ? change.action.affliction : null)}");
                    break;

                case CookingBehavior_RunActions run:
                    if (run.actionsToRun != null)
                    {
                        foreach (var action in run.actionsToRun)
                        {
                            AppendAction(action);
                        }
                    }

                    break;

                case CookingBehavior_AddPoisonOnUse poison:
                    if (poison.onConsume)
                    {
                        Add("食用后中毒");
                    }
                    else if (poison.onUse)
                    {
                        Add("使用后中毒");
                    }

                    break;
            }
        }
    }

    // ---------------------------------------------------------------- 状态名映射

    private static string DescribeAffliction(Affliction? affliction)
    {
        switch (affliction)
        {
            case null:
                return string.Empty;

            case Affliction_AdjustStatus status:
                return $"{StatusName(status.statusType)} {SignedStatus(status.statusAmount)}";

            case Affliction_AdjustColdOverTime cold:
                return $"寒冷 {SignedStatus(cold.statusPerSecond)}/秒";

            case Affliction_AdjustDrowsyOverTime drowsy:
                return $"困倦 {SignedStatus(drowsy.statusPerSecond)}/秒";

            case Affliction_AdjustStatusOverTime overTime:
                return $"状态 {SignedStatus(overTime.statusPerSecond)}/秒";

            case Affliction_PoisonOverTime poison:
                return $"中毒 {SignedStatus(poison.statusPerSecond)}/秒";

            case Affliction_ZombieBite bite:
                return $"僵尸啃咬 {SignedStatus(bite.statusPerSecond)}/秒";

            case Affliction_HealAll:
                return "治愈 " + StatusList(Affliction_HealAll.statusesToHeal);

            case Affliction_AddBonusStamina stamina:
                return $"额外体力 +{Scaled(stamina.staminaAmount)}";

            case Affliction_Exhaustion exhaustion:
                return $"力竭 {SignedStatus(exhaustion.drainAmount)}";

            case Affliction_ClimbingChalk chalk:
                return $"攀爬体力 x{chalk.climbStaminaMultiplier:0.##}";

            case Affliction_FasterBoi faster:
                return $"加速 x{faster.moveSpeedMod:0.##}";

            case Affliction_DoubleJumpAmulet:
                return "二段跳";

            case Affliction_LowGravity gravity:
                return $"低重力 {gravity.lowGravAmount}";

            case Affliction_Invincibility:
                return "无敌";

            case Affliction_BingBongShield:
                return "BingBong 护盾";

            case Affliction_InfiniteStamina:
                return "无限体力";

            case Affliction_NoHunger:
                return "不再饥饿";

            case Affliction_Sunscreen:
                return "防晒";

            case Affliction_Blind:
                return "致盲";

            case Affliction_Numb:
                return "麻木";

            case Affliction_PreventPoisonHealing:
                return "无法解毒";

            case Affliction_ClearAllStatus:
                return "清除异常状态";

            case Affliction_Chaos chaos:
                return $"混乱 {SignedStatus(chaos.statusAmountAverage)}";

            case Affliction_Glowing:
                return "发光";

            case Affliction_RadiateInfiniteStam:
                return "范围无限体力";

            case Affliction_MassSuperJump:
                return "范围超级跳";

            default:
                return TrimPrefix(affliction.GetType().Name, "Affliction_");
        }
    }

    internal static string StatusName(CharacterAfflictions.STATUSTYPE type)
    {
        switch (type)
        {
            case CharacterAfflictions.STATUSTYPE.Injury:
                return "受伤";
            case CharacterAfflictions.STATUSTYPE.Hunger:
                return "饥饿";
            case CharacterAfflictions.STATUSTYPE.Cold:
                return "寒冷";
            case CharacterAfflictions.STATUSTYPE.Poison:
                return "中毒";
            case CharacterAfflictions.STATUSTYPE.Crab:
                return "螃蟹";
            case CharacterAfflictions.STATUSTYPE.Curse:
                return "诅咒";
            case CharacterAfflictions.STATUSTYPE.Drowsy:
                return "困倦";
            case CharacterAfflictions.STATUSTYPE.Weight:
                return "负重";
            case CharacterAfflictions.STATUSTYPE.Hot:
                return "炎热";
            case CharacterAfflictions.STATUSTYPE.Thorns:
                return "荆棘";
            case CharacterAfflictions.STATUSTYPE.Spores:
                return "孢子";
            case CharacterAfflictions.STATUSTYPE.Web:
                return "蛛网";
            case CharacterAfflictions.STATUSTYPE.Arrow:
                return "箭伤";
            case CharacterAfflictions.STATUSTYPE.Petrify:
                return "石化";
            case CharacterAfflictions.STATUSTYPE.FlyTrap:
                return "捕蝇草";
            default:
                return type.ToString();
        }
    }

    private static string StatusList(CharacterAfflictions.STATUSTYPE[]? statuses)
    {
        if (statuses == null || statuses.Length == 0)
        {
            return "异常状态";
        }

        var names = new List<string>();
        foreach (var status in statuses)
        {
            var name = StatusName(status);
            if (!names.Contains(name))
            {
                names.Add(name);
            }
        }

        return string.Join("、", names);
    }

    // ---------------------------------------------------------------- 小工具

    private static void Add(string? text)
    {
        if (!string.IsNullOrEmpty(text) && seenParts.Add(text!))
        {
            parts.Add(text!);
        }
    }

    /// <summary>
    /// 把游戏内部的体力条刻度（满格 1.0）换算成界面上的 0~100。
    /// 体力条上的状态数值和物品效果数值共用这一个刻度。
    /// </summary>
    internal static string Scaled(float value)
    {
        return (value * StatusScale).ToString("0.#");
    }

    /// <summary>带符号的体力条刻度值：正数前加「+」，负数自带「-」。</summary>
    private static string SignedStatus(float value)
    {
        var scaled = value * StatusScale;
        return Mathf.Abs(scaled) < 0.05f ? "0" : scaled.ToString("+0.#;-0.#");
    }

    /// <summary>带符号的整数显示（荆棘这类个数，不是状态量，不换算）。</summary>
    private static string Signed(int value)
    {
        return value == 0 ? "0" : value.ToString("+0;-0");
    }

    private static string TrimPrefix(string name, string prefix)
    {
        return name.StartsWith(prefix, StringComparison.Ordinal)
            ? name.Substring(prefix.Length)
            : name;
    }
}
