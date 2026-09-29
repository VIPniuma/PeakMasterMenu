using System;
using HarmonyLib;
using Photon.Pun;
using UnityEngine;

namespace PeakMasterMenu;

/// <summary>
/// 游戏里没有统一的"伤害入口"——所有伤害、寒冷、中毒、诅咒其实都是
/// <see cref="CharacterAfflictions"/> 上的一种"状态量"。
/// 所以只要在 <c>AddStatus</c> / <c>AdjustStatus</c> 上做拦截，
/// 就能一次性实现无敌、无饥饿、免疫异常状态。
/// </summary>
[HarmonyPatch(typeof(CharacterAfflictions))]
internal static class AfflictionPatches
{
    [HarmonyPatch(nameof(CharacterAfflictions.AddStatus))]
    [HarmonyPrefix]
    private static bool AddStatusPrefix(
        CharacterAfflictions __instance,
        CharacterAfflictions.STATUSTYPE statusType,
        ref bool __result)
    {
        if (!ShouldBlock(__instance, statusType))
        {
            return true;
        }

        __result = false;
        return false;
    }

    [HarmonyPatch(nameof(CharacterAfflictions.AdjustStatus))]
    [HarmonyPrefix]
    private static bool AdjustStatusPrefix(
        CharacterAfflictions __instance,
        CharacterAfflictions.STATUSTYPE statusType)
    {
        return !ShouldBlock(__instance, statusType);
    }

    private static bool ShouldBlock(CharacterAfflictions afflictions, CharacterAfflictions.STATUSTYPE statusType)
    {
        var character = afflictions != null ? afflictions.character : null;

        // 只保护本地玩家自己：别人（以及 Bot）的伤害照常结算。
        if (character == null || !character.IsLocal)
        {
            return false;
        }

        if (Cheats.GodMode)
        {
            return true;
        }

        if (Cheats.NoHunger && statusType == CharacterAfflictions.STATUSTYPE.Hunger)
        {
            return true;
        }

        return Cheats.ImmuneStatus && IsEnvironmental(statusType);
    }

    private static bool IsEnvironmental(CharacterAfflictions.STATUSTYPE statusType)
    {
        switch (statusType)
        {
            case CharacterAfflictions.STATUSTYPE.Cold:
            case CharacterAfflictions.STATUSTYPE.Poison:
            case CharacterAfflictions.STATUSTYPE.Curse:
            case CharacterAfflictions.STATUSTYPE.Spores:
                return true;
            default:
                return false;
        }
    }
}

/// <summary>
/// 飞行 / 穿墙：在游戏本帧的受力计算之后、物理积分之前改写刚体速度。
/// </summary>
[HarmonyPatch(typeof(CharacterMovement))]
internal static class MovementPatches
{
    [HarmonyPatch(nameof(CharacterMovement.FixedUpdate))]
    [HarmonyPostfix]
    private static void FixedUpdatePostfix(CharacterMovement __instance)
    {
        if (!Cheats.Fly && !Cheats.NoClip)
        {
            return;
        }

        Cheats.ApplyFlightPhysics(__instance);
    }
}

/// <summary>
/// 飞行时不布娃娃。
///
/// 身体"站得多直"由 <c>CharacterData.GetTargetRagdollControll</c> 决定，
/// 而它是这么写的：只要 <c>fallSeconds &gt; 0</c>（在空中坠落）就返回 0 —— 整个人软掉，
/// 于是飞起来之后角色一直摊着，落地前都"站不起来"。
/// 飞行开着的时候直接让它返回 1（完全站直），身体就会像正常站立那样直立、朝向视角。
/// 晕倒 / 死亡 / 石化这些情况不碰，免得更糟。
/// </summary>
[HarmonyPatch(typeof(CharacterData))]
internal static class FlightRagdollPatch
{
    [HarmonyPatch(nameof(CharacterData.GetTargetRagdollControll))]
    [HarmonyPostfix]
    private static void GetTargetRagdollControllPostfix(CharacterData __instance, ref float __result)
    {
        if (!Cheats.Fly || __result >= 1f || __instance == null)
        {
            return;
        }

        // 只管本地玩家自己，别人的身体照旧。
        var local = Character.localCharacter;
        if (local == null || local.data == null || !ReferenceEquals(local.data, __instance))
        {
            return;
        }

        if (__instance.dead || __instance.passedOut || __instance.fullyPassedOut)
        {
            return;
        }

        __result = 1f;
    }
}

/// <summary>
/// 隐身：游戏里每个敌人各写一套目标选择，没有统一开关。
/// 这里统一处理占绝大多数的 <see cref="Mob"/> 家族（甲虫、蝎子、青蛙等）——
/// 让它们在锁定本地玩家后立刻放弃目标。
/// </summary>
[HarmonyPatch(typeof(Mob))]
internal static class MobPatches
{
    private static AccessTools.FieldRef<Mob, Character>? targetRef;

    [HarmonyPatch("Targeting")]
    [HarmonyPostfix]
    private static void TargetingPostfix(Mob __instance)
    {
        if (!Cheats.Stealth)
        {
            return;
        }

        var local = Character.localCharacter;
        if (local == null)
        {
            return;
        }

        try
        {
            targetRef ??= AccessTools.FieldRefAccess<Mob, Character>("targetChar");

            if (ReferenceEquals(targetRef(__instance), local))
            {
                targetRef(__instance) = null!;
            }
        }
        catch (System.Exception e)
        {
            Plugin.Log.LogWarning($"处理隐身失败: {e.Message}");
        }
    }
}

/// <summary>
/// 轻放丢弃：把投掷力度与继承速度清零，物品就只会落在脚下而不是飞出去。
/// 只对本地玩家自己的丢弃生效，避免影响队友看到的画面。
/// </summary>
[HarmonyPatch(typeof(CharacterItems))]
internal static class GentleDropPatch
{
    private static AccessTools.FieldRef<CharacterItems, Character>? characterRef;

    [HarmonyPatch(nameof(CharacterItems.DropItemRpc))]
    [HarmonyPrefix]
    private static void DropItemRpcPrefix(
        CharacterItems __instance,
        ref float throwCharge,
        ref Vector3 velocity)
    {
        if (!Cheats.GentleDrop)
        {
            return;
        }

        try
        {
            characterRef ??= AccessTools.FieldRefAccess<CharacterItems, Character>("character");
            var character = characterRef(__instance);
            if (character == null || !character.IsLocal)
            {
                return;
            }

            throwCharge = 0f;
            velocity = Vector3.zero;
        }
        catch (System.Exception e)
        {
            Plugin.Log.LogWarning($"轻放丢弃失败: {e.Message}");
        }
    }
}

//// <summary>
/// 死亡 / 晕倒 / 复活时不掉落物品。
/// <c>DropAllItems</c> 本来就只对本地玩家生效，这里在开关打开时直接拦截，
/// 于是死亡、晕倒、复活这三条会掉落物品的路径都会被跳过。
/// </summary>
[HarmonyPatch(typeof(CharacterItems))]
internal static class KeepItemsOnDeathPatch
{
    private static AccessTools.FieldRef<CharacterItems, Character>? characterRef;

    [HarmonyPatch(nameof(CharacterItems.DropAllItems))]
    [HarmonyPrefix]
    private static bool DropAllItemsPrefix(CharacterItems __instance)
    {
        if (!Cheats.KeepItemsOnDeath)
        {
            return true;
        }

        try
        {
            characterRef ??= AccessTools.FieldRefAccess<CharacterItems, Character>("character");
            var character = characterRef(__instance);

            // 非本地角色原本就会直接 return，保持原样即可。
            return character == null || !character.IsLocal;
        }
        catch (System.Exception e)
        {
            Plugin.Log.LogWarning($"阻止掉落物品失败: {e.Message}");
            return true;
        }
    }
}

/// <summary>
/// 让「按住背包键」时背包轮盘一直保持打开。
/// 原版 <see cref="BackpackWheel"/> 每帧检查 <c>input.interactIsPressed</c>，
/// 一旦为 false 就立刻 <c>Choose()</c> 并关闭——而我们的背包键并不会驱动交互键，
/// 所以轮盘只会闪一下就消失。
/// 这里在这个 Update 执行期间把交互键临时置真、结束时立刻还原：
/// 既骗过了轮盘自身的检查，又不会影响在其之后运行的交互系统
/// （Interaction 的执行顺序是 600，晚于所有普通 Update）。
///
/// 松手时我们不去关轮盘，只把标志清掉，让轮盘自己走完
/// <c>Choose()</c> + <c>CloseBackpackWheel()</c>，
/// 这样"悬停某一格再松手取放"和原版按住 E 完全一致。
/// </summary>
[HarmonyPatch(typeof(BackpackWheel))]
internal static class BackpackWheelHoldPatch
{
    private static bool forced;

    [HarmonyPatch("Update")]
    [HarmonyPrefix]
    private static void UpdatePrefix(BackpackWheel __instance)
    {
        forced = false;

        if (__instance == null || !__instance.gameObject.activeSelf)
        {
            return;
        }

        var local = Character.localCharacter;
        var input = local != null ? local.input : null;
        if (input == null || input.interactIsPressed)
        {
            return;
        }

        // 只有「按住背包键打开」的这个轮盘才伪装成交互键被按住。
        if (!Plugin.BackpackHotkeyHeld)
        {
            return;
        }

        input.interactIsPressed = true;
        forced = true;
    }

    [HarmonyPatch("Update")]
    [HarmonyPostfix]
    private static void UpdatePostfix()
    {
        if (!forced)
        {
            return;
        }

        forced = false;

        var local = Character.localCharacter;
        if (local != null && local.input != null)
        {
            local.input.interactIsPressed = false;
        }
    }
}

/// <summary>
/// 让活着的队友也能被扛起。
/// 原版 <c>CanBeCarried</c> 只在对方"晕倒但没死"时返回 true，这里补上开关。
/// </summary>
[HarmonyPatch(typeof(CharacterInteractible))]
internal static class CarryLivingPlayerPatch
{
    private static AccessTools.FieldRef<CharacterInteractible, Character>? characterRef;

    [HarmonyPatch("CanBeCarried")]
    [HarmonyPostfix]
    private static void CanBeCarriedPostfix(CharacterInteractible __instance, ref bool __result)
    {
        if (__result || !Cheats.CarryLivingPlayers)
        {
            return;
        }

        try
        {
            characterRef ??= AccessTools.FieldRefAccess<CharacterInteractible, Character>("character");
            var character = characterRef(__instance);
            if (character == null || character.isBot || character.data == null)
            {
                return;
            }

            // 死人扛不动，已经被人扛着的也不重复触发。
            if (character.data.dead || character.data.carrier != null)
            {
                return;
            }

            __result = true;
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"处理「扛起活人」失败: {e.Message}");
        }
    }
}

/// <summary>
/// 扛着活人时，<c>CharacterCarrying.Update</c> 会因为"对方没晕倒"而立刻把人放下。
/// 开关打开时把这段放下逻辑整段跳过，只在真的该放（对方死了 / 自己倒下了）时才放行。
/// </summary>
[HarmonyPatch(typeof(CharacterCarrying))]
internal static class CarryLivingKeepPatch
{
    private static AccessTools.FieldRef<CharacterCarrying, Character>? characterRef;

    [HarmonyPatch("Update")]
    [HarmonyPrefix]
    private static bool UpdatePrefix(CharacterCarrying __instance)
    {
        if (!Cheats.CarryLivingPlayers)
        {
            return true;
        }

        try
        {
            characterRef ??= AccessTools.FieldRefAccess<CharacterCarrying, Character>("character");
            var character = characterRef(__instance);
            if (character == null || character.data == null || !character.photonView.IsMine)
            {
                return true;
            }

            var carried = character.data.carriedPlayer;
            if (carried == null || carried.data == null)
            {
                return true;
            }

            if (carried.data.dead || character.data.dead || character.data.fullyPassedOut)
            {
                return true;
            }

            return false;
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"处理「扛起活人」失败: {e.Message}");
            return true;
        }
    }
}

/// <summary>
/// 扛人时保住自己的背包。
/// 原版 <c>RPCA_StartCarry</c> 一上来就把背上的背包丢到地上，
/// 这里在开关打开时换成一份"不丢背包"的同款实现。
/// </summary>
[HarmonyPatch(typeof(CharacterCarrying))]
internal static class KeepBackpackWhenCarryingPatch
{
    private static AccessTools.FieldRef<CharacterCarrying, Character>? characterRef;

    [HarmonyPatch("RPCA_StartCarry")]
    [HarmonyPrefix]
    private static bool StartCarryPrefix(CharacterCarrying __instance, PhotonView targetView)
    {
        if (!Cheats.CarryLivingPlayers || !Cheats.KeepBackpackWhenCarrying)
        {
            return true;
        }

        try
        {
            characterRef ??= AccessTools.FieldRefAccess<CharacterCarrying, Character>("character");
            var character = characterRef(__instance);
            if (character == null || character.data == null || targetView == null)
            {
                return true;
            }

            // 背上没背包就没什么要保护的，直接交给原版。
            var slot = character.player != null ? character.player.backpackSlot : null;
            if (slot == null || slot.IsEmpty())
            {
                return true;
            }

            var target = targetView.GetComponent<Character>();
            if (target == null || target.data == null)
            {
                return true;
            }

            // 已经扛着别人：先放下，行为与原版保持一致。
            if (character.data.carriedPlayer != null)
            {
                __instance.Drop(character.data.carriedPlayer);
                return false;
            }

            AttachCarried(character, target);
            return false;
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"处理「扛人保留背包」失败，回退到原版行为: {e.Message}");
            return true;
        }
    }

    /// <summary>复刻原版收尾逻辑，唯一区别是不碰 <c>backpackSlot</c>。</summary>
    private static void AttachCarried(Character carrier, Character target)
    {
        target.refs.ragdoll.ToggleCollision(false);
        target.refs.animations.SetBool("IsCarried", true);
        target.data.isCarried = true;
        carrier.data.carriedPlayer = target;
        target.data.carrier = carrier;

        target.refs.afflictions.SubtractStatus(CharacterAfflictions.STATUSTYPE.FlyTrap, 1f, true);
        target.refs.afflictions.SubtractStatus(CharacterAfflictions.STATUSTYPE.Web, 1f, true);

        var characters = PlayerHandler.GetAllPlayerCharacters();
        if (characters == null)
        {
            return;
        }

        foreach (var character in characters)
        {
            if (character == null || character.refs == null || character.refs.afflictions == null)
            {
                continue;
            }

            character.refs.afflictions.UpdateWeight();
        }
    }
}
