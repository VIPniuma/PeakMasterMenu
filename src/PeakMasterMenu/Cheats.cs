using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace PeakMasterMenu;

/// <summary>
/// 集中管理所有"持续生效"的作弊开关。
/// 与界面完全解耦：菜单关掉之后，只要开关还开着就依然生效。
/// </summary>
internal static class Cheats
{
    // ---------------------------------------------------------------- 状态类

    /// <summary>无敌：拦截本地玩家身上的一切状态增长（含伤害、寒冷、中毒、诅咒、孢子…）。</summary>
    internal static bool GodMode;

    /// <summary>无饥饿：单独拦截 Hunger 状态。</summary>
    internal static bool NoHunger;

    /// <summary>免疫寒冷 / 中毒 / 诅咒 / 孢子。</summary>
    internal static bool ImmuneStatus;

    /// <summary>免疫坠落伤害。</summary>
    internal static bool NoFallDamage;

    /// <summary>无限体力。</summary>
    internal static bool InfiniteStamina;

    // ---------------------------------------------------------------- 能力类

    /// <summary>无限二段跳。</summary>
    internal static bool ExtraJump;

    /// <summary>自由飞行（WASD + 空格 / Ctrl）。</summary>
    internal static bool Fly;

    /// <summary>穿墙：关掉角色碰撞体。</summary>
    internal static bool NoClip;

    /// <summary>隐身：范围内的敌人不会把你选为目标。</summary>
    internal static bool Stealth;

    /// <summary>攀爬不滑落。</summary>
    internal static bool NoSlip;

    /// <summary>手持物品不消耗（耐久 / 燃料不减）。</summary>
    internal static bool NoItemConsume;

    /// <summary>丢弃物品时轻放在脚下，不继承奔跑速度也不抛出。</summary>
    internal static bool GentleDrop;

    /// <summary>死亡 / 晕倒 / 复活时不掉落身上的物品。</summary>
    internal static bool KeepItemsOnDeath;

    // ---------------------------------------------------------------- 队友交互

    /// <summary>活着的队友也能直接扛起来（按 E）。</summary>
    internal static bool CarryLivingPlayers;

    /// <summary>扛队友时，自己背上的背包不会掉到地上。</summary>
    internal static bool KeepBackpackWhenCarrying;

    // ---------------------------------------------------------------- 屏幕绘制

    /// <summary>是否在屏幕上绘制地面物品标记。</summary>
    internal static bool DrawItems;

    /// <summary>绘制的最大距离（米）。</summary>
    internal static float DrawDistance = 150f;

    /// <summary>勾选了要绘制的物品名。为空表示"一件都不绘制"，需要手动勾选。</summary>
    internal static readonly HashSet<string> DrawItemFilter = new();

    /// <summary>是否在屏幕上绘制敌人（Mob）位置。</summary>
    internal static bool DrawMobs;

    /// <summary>绘制敌人的最大距离（米）。</summary>
    internal static float DrawMobDistance = 120f;

    /// <summary>是否在屏幕上绘制行李箱 / 宝箱位置。</summary>
    internal static bool DrawLuggage;

    /// <summary>是否在屏幕上绘制雕像位置。</summary>
    internal static bool DrawStatues;

    // ---------------------------------------------------------------- 倍率

    internal static float SpeedMultiplier = 1f;
    internal static float JumpMultiplier = 1f;

    internal static float WallClimbSpeed = 1f;
    internal static float VineClimbSpeed = 1f;
    internal static float RopeClimbSpeed = 1f;
    internal static float WallClimbStamina = 1f;
    internal static float VineClimbStamina = 1f;
    internal static float RopeClimbStamina = 1f;
    internal static float StaminaRegen = 1f;

    /// <summary>飞行速度（米/秒）。</summary>
    internal static float FlySpeed = 14f;

    /// <summary>游戏本身固定的体力恢复速度（每秒）。</summary>
    private const float VanillaStaminaRegen = 0.2f;

    /// <summary>无限跳时随手给的额外跳跃次数。</summary>
    private const int ExtraJumpCount = 99;

    private static Character? trackedCharacter;
    private static float appliedSpeed = 1f;
    private static float appliedJump = 1f;
    private static float appliedWallClimbSpeed = 1f;
    private static float appliedVineClimbSpeed = 1f;
    private static float appliedRopeClimbSpeed = 1f;
    private static float appliedWallClimbStamina = 1f;
    private static float appliedVineClimbStamina = 1f;
    private static float appliedRopeClimbStamina = 1f;
    private static bool infiniteStaminaApplied;
    private static bool noClipApplied;

    /// <summary>飞行 / 悬停期间是否已经把关掉的重力接管过来（离开飞行时要还回去）。</summary>
    private static bool flightGravityDisabled;

    /// <summary>悬停锁定的高度：松开上升 / 下降键后，人就被钉在这个高度上。</summary>
    private static float flightHoldY;

    /// <summary><see cref="flightHoldY"/> 是否有效。</summary>
    private static bool flightHoldActive;

    /// <summary>由插件每帧调用。</summary>
    internal static void Update()
    {
        var character = Character.localCharacter;
        if (character == null)
        {
            Forget();
            return;
        }

        if (!ReferenceEquals(character, trackedCharacter))
        {
            trackedCharacter = character;
            ResetApplied();
        }

        ApplyInfiniteStamina(character);
        ApplyMovement(character);
        ApplyClimbing(character);
        ApplyStaminaRegen(character);
        ApplyAbilities(character);
        ApplyNoClip(character);
        ApplyItemCheats(character);
        RestoreFlightGravity(character);
    }

    /// <summary>把所有开关恢复为默认值。</summary>
    internal static void Reset()
    {
        GodMode = false;
        NoHunger = false;
        ImmuneStatus = false;
        NoFallDamage = false;
        InfiniteStamina = false;
        ExtraJump = false;
        Fly = false;
        NoClip = false;
        Stealth = false;
        NoSlip = false;
        NoItemConsume = false;
        GentleDrop = false;
        KeepItemsOnDeath = false;

        CarryLivingPlayers = false;
        KeepBackpackWhenCarrying = false;

        DrawItems = false;
        DrawDistance = 150f;
        DrawItemFilter.Clear();

        DrawMobs = false;
        DrawMobDistance = 120f;

        DrawLuggage = false;
        DrawStatues = false;

        SpeedMultiplier = 1f;
        JumpMultiplier = 1f;
        WallClimbSpeed = 1f;
        VineClimbSpeed = 1f;
        RopeClimbSpeed = 1f;
        WallClimbStamina = 1f;
        VineClimbStamina = 1f;
        RopeClimbStamina = 1f;
        StaminaRegen = 1f;
        FlySpeed = 14f;
    }

    // ---------------------------------------------------------------- 飞行输入

    /// <summary>
    /// 由 Harmony 补丁在 <c>CharacterMovement.FixedUpdate</c> 之后调用。
    /// 直接改写每块刚体的速度，因为此时游戏本帧的力已经施加完毕、物理积分还没开始。
    /// </summary>
    internal static void ApplyFlightPhysics(CharacterMovement movement)
    {
        if (!Fly && !NoClip)
        {
            return;
        }

        var character = Character.localCharacter;
        if (character == null || character.refs == null)
        {
            return;
        }

        if (!ReferenceEquals(character.refs.movement, movement))
        {
            return;
        }

        var ragdoll = character.refs.ragdoll;
        if (ragdoll == null)
        {
            return;
        }

        var velocity = Vector3.zero;
        var hasVerticalInput = false;

        if (Fly)
        {
            var direction = FlyVelocity();
            velocity = direction * FlySpeed;
            hasVerticalInput = Mathf.Abs(direction.y) > 0.001f;

            // 布娃娃控制量就是"身体站得多直"。游戏一判定你在空中，
            // 每帧都把目标值压成 0（整个人软成一摊），所以这里飞行时直接拉满，
            // 配合 FlightRagdollPatch 让身体一直是站直的。
            character.data.currentRagdollControll = 1f;
        }

        // 光把速度写掉是不够的：游戏本帧已经给每块刚体加过一次向下的力
        //（Bodypart::Gravity 走 AddForce，动画弹簧的 FollowRotation_Position 也在拽），
        // 所以飞行 / 悬停期间先把重力关掉，离开飞行状态时在 RestoreFlightGravity 里还原。
        //
        // 但"关重力 + 覆盖速度"之后仍然会剩一点下沉，所以再加一道高度锁：
        // 没按上升 / 下降键时，按上一帧实际掉下去多少把垂直速度补回来，
        // 一帧一帧地把人钉在锁定高度上，做到一点不掉。
        var centerY = CharacterBody.Position(character).y;
        var holdVelocity = 0f;

        if (hasVerticalInput || Plugin.MenuOpen)
        {
            // 跟着上升 / 下降走，锁定高度跟着挪；松手后就停在这个新高度。
            flightHoldY = centerY;
            flightHoldActive = true;
        }
        else if (flightHoldActive)
        {
            holdVelocity = (flightHoldY - centerY) / Mathf.Max(Time.fixedDeltaTime, 0.0001f);
        }
        else
        {
            // 刚进入飞行 / 悬停：以当前高度为准。
            flightHoldY = centerY;
            flightHoldActive = true;
        }

        velocity.y += holdVelocity;

        flightGravityDisabled = true;

        foreach (var part in ragdoll.partList)
        {
            if (part == null)
            {
                continue;
            }

            var rig = part.Rig;
            if (rig == null)
            {
                continue;
            }

            if (Fly)
            {
                rig.useGravity = false;
                rig.linearVelocity = velocity;
            }
            else
            {
                // 只关碰撞不飞行时也保持悬停：垂直速度同样按锁定高度修正，
                // 免得悬在半空还一点点往下沉。
                rig.useGravity = false;
                var current = rig.linearVelocity;
                current.y = holdVelocity;
                rig.linearVelocity = current;
            }
        }

        movement.CapFallDamage(0f, 1f);
    }

    /// <summary>
    /// 离开飞行 / 悬停状态后，把 <see cref="ApplyFlightPhysics"/> 关掉的重力还原回去。
    ///
    /// 每帧由 <see cref="Update"/> 调用：只要重力是我们关的、而飞行已经停了，就还回去。
    /// 留着不还会让角色彻底失去重力，落地后就再也掉不下来了。
    /// </summary>
    private static void RestoreFlightGravity(Character character)
    {
        if (!flightGravityDisabled || Fly || NoClip)
        {
            return;
        }

        flightGravityDisabled = false;
        flightHoldActive = false;

        var ragdoll = character.refs != null ? character.refs.ragdoll : null;
        if (ragdoll == null)
        {
            return;
        }

        foreach (var part in ragdoll.partList)
        {
            var rig = part != null ? part.Rig : null;
            if (rig != null)
            {
                rig.useGravity = true;
            }
        }
    }

    private static Vector3 FlyVelocity()
    {
        // 菜单打开时按键属于输入框，不要拿去驱动飞行。
        if (Plugin.MenuOpen)
        {
            return Vector3.zero;
        }

        var direction = Vector3.zero;
        var camera = MainCamera.instance;
        var transform = camera != null ? camera.transform : null;

        var forward = transform != null
            ? Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized
            : Vector3.forward;

        if (forward.sqrMagnitude < 0.001f)
        {
            forward = Vector3.forward;
        }

        var right = Vector3.Cross(Vector3.up, forward).normalized;

        if (Input.GetKey(KeyCode.W)) direction += forward;
        if (Input.GetKey(KeyCode.S)) direction -= forward;
        if (Input.GetKey(KeyCode.D)) direction += right;
        if (Input.GetKey(KeyCode.A)) direction -= right;
        if (Input.GetKey(KeyCode.Space)) direction += Vector3.up;
        if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.C)) direction -= Vector3.up;

        return direction.sqrMagnitude > 1f ? direction.normalized : direction;
    }

    // ---------------------------------------------------------------- 物品吸取

    /// <summary>把附近的落地物品吸到面前，短暂悬停后自然落下。</summary>
    internal static void PullNearbyItems()
    {
        var character = Character.localCharacter;
        if (character == null)
        {
            return;
        }

        // 位置取躯干刚体：角色根节点的 transform 是出生点，永远不动。
        var center = CharacterBody.Position(character) + (Vector3.up * 0.8f);
        var picks = new List<Item>();

        try
        {
            var all = Item.ALL_ITEMS;
            if (all == null)
            {
                return;
            }

            foreach (var item in all)
            {
                if (item == null || item.itemState != ItemState.Ground)
                {
                    continue;
                }

                if (Vector3.Distance(item.transform.position, center) > 40f)
                {
                    continue;
                }

                picks.Add(item);
            }
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"扫描物品失败: {e}");
            return;
        }

        picks.Sort((a, b) => Vector3.Distance(a.transform.position, center)
            .CompareTo(Vector3.Distance(b.transform.position, center)));

        if (picks.Count > 20)
        {
            picks.RemoveRange(20, picks.Count - 20);
        }

        if (picks.Count == 0)
        {
            Plugin.Log.LogInfo("附近没有可以吸取的落地物品。");
            return;
        }

        var camera = MainCamera.instance;
        var forward = camera != null
            ? Vector3.ProjectOnPlane(camera.transform.forward, Vector3.up).normalized
            : character.transform.forward;

        if (forward.sqrMagnitude < 0.001f)
        {
            forward = character.transform.forward;
        }

        var basePosition = center + (forward * 1.6f);

        try
        {
            for (var i = 0; i < picks.Count; i++)
            {
                var item = picks[i];
                var offset = new Vector3(
                    ((i % 4) - 1.5f) * 0.4f,
                    0.4f + ((i / 4) * 0.2f),
                    ((i % 2) - 0.5f) * 0.4f);
                item.SetKinematicNetworked(true, basePosition + offset, item.transform.rotation);
            }

            if (Plugin.Instance != null)
            {
                Plugin.Instance.StartCoroutine(ReleasePulledItems(picks));
            }
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"吸取物品失败: {e}");
        }
    }

    private static IEnumerator ReleasePulledItems(List<Item> items)
    {
        yield return new WaitForSeconds(0.9f);

        foreach (var item in items)
        {
            if (item == null)
            {
                continue;
            }

            try
            {
                if (item.itemState != ItemState.Ground)
                {
                    continue;
                }

                item.SetKinematicNetworked(false, item.transform.position, item.transform.rotation);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"放下物品失败: {e.Message}");
            }
        }
    }

    // ---------------------------------------------------------------- 一键操作

    /// <summary>
    /// 把「额外体力条」补满。
    /// 游戏里额外体力（蘑菇汤、士气加成给的黄条）上限只有 1，
    /// 而且会被石化值压低，所以交给 <c>SetExtraStamina</c> 自己做钳制。
    /// </summary>
    internal static void FillExtraStamina()
    {
        var character = Character.localCharacter;
        if (character == null || character.data == null)
        {
            Plugin.Log.LogWarning("还没有进入一局游戏，无法补充额外体力。");
            return;
        }

        try
        {
            character.SetExtraStamina(1f);
            character.ClampStamina();
            Plugin.Log.LogInfo($"额外体力已补满（当前 {character.data.extraStamina:F2}）。");
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"补满额外体力失败: {e}");
        }
    }

    // ---------------------------------------------------------------- 状态应用

    private static void ApplyInfiniteStamina(Character character)
    {
        if (InfiniteStamina)
        {
            character.infiniteStam = true;
            infiniteStaminaApplied = true;
        }
        else if (infiniteStaminaApplied)
        {
            character.infiniteStam = false;
            infiniteStaminaApplied = false;
        }
    }

    private static void ApplyMovement(Character character)
    {
        var movement = character.refs != null ? character.refs.movement : null;
        if (movement == null)
        {
            return;
        }

        appliedSpeed = UpdateMultiplier(
            appliedSpeed,
            SpeedMultiplier,
            factor => movement.movementModifier *= factor);

        // jumpImpulse 来自预制体，如果为 0 说明还没初始化，缩放它没有意义。
        if (movement.jumpImpulse > 0.0001f)
        {
            appliedJump = UpdateMultiplier(
                appliedJump,
                JumpMultiplier,
                factor => movement.jumpImpulse *= factor);
        }

        // 站在地上时游戏会把坠落伤害上限重置为 0，这里持续覆盖成"不受伤"。
        if (NoFallDamage)
        {
            movement.CapFallDamage(0f, 1f);
        }
    }

    /// <summary>
    /// 攀爬相关调节。
    /// 这里刻意只改 <c>climbSpeed</c> 这类"没人叠加"的字段，
    /// 不动 <c>climbSpeedMod</c>（游戏自己的加速类状态会用 += / -= 改它，抢着写会互相覆盖）。
    /// </summary>
    private static void ApplyClimbing(Character character)
    {
        var refs = character.refs;
        if (refs == null)
        {
            return;
        }

        var wall = refs.climbing;
        if (wall != null)
        {
            appliedWallClimbSpeed = UpdateMultiplier(
                appliedWallClimbSpeed,
                WallClimbSpeed,
                factor => wall.climbSpeed *= factor);

            appliedWallClimbStamina = UpdateMultiplier(
                appliedWallClimbStamina,
                WallClimbStamina,
                factor =>
                {
                    wall.maxStaminaUsage *= factor;
                    wall.minStaminaUsage *= factor;
                });
        }

        var vine = refs.vineClimbing;
        if (vine != null)
        {
            appliedVineClimbSpeed = UpdateMultiplier(
                appliedVineClimbSpeed,
                VineClimbSpeed,
                factor => vine.climbSpeed *= factor);

            appliedVineClimbStamina = UpdateMultiplier(
                appliedVineClimbStamina,
                VineClimbStamina,
                factor =>
                {
                    vine.staminaUsage *= factor;
                    vine.staminaUsageGrab *= factor;
                });
        }

        var rope = refs.ropeHandling;
        if (rope != null)
        {
            appliedRopeClimbSpeed = UpdateMultiplier(
                appliedRopeClimbSpeed,
                RopeClimbSpeed,
                factor => rope.climbSpeed *= factor);

            appliedRopeClimbStamina = UpdateMultiplier(
                appliedRopeClimbStamina,
                RopeClimbStamina,
                factor =>
                {
                    rope.staminaUsage *= factor;
                    rope.staminaUsageUp *= factor;
                });
        }
    }

    /// <summary>
    /// 体力恢复加速。游戏把恢复速度硬编码成了每秒 0.2，无法直接改，
    /// 所以这里在它的基础上补一份额外恢复量。
    /// </summary>
    private static void ApplyStaminaRegen(Character character)
    {
        if (StaminaRegen <= 1f || !character.IsLocal || character.data == null)
        {
            return;
        }

        if (character.data.isClimbingAnything || character.data.dead || character.data.fullyPassedOut)
        {
            return;
        }

        var extra = VanillaStaminaRegen * (StaminaRegen - 1f) * Time.deltaTime;
        if (extra <= 0f)
        {
            return;
        }

        character.AddStamina(extra);
        character.ClampStamina();
    }

    private static void ApplyAbilities(Character character)
    {
        var data = character.data;
        if (data == null)
        {
            return;
        }

        try
        {
            // 无限跳：游戏本身就留了 extraJumps 这个"额外跳跃"通道，
            // 落地时会被重置，所以每帧补满即可。
            if (ExtraJump)
            {
                data.extraJumps = ExtraJumpCount;
                if (data.jumpsRemaining < 1)
                {
                    data.jumpsRemaining = 1;
                }
            }
            else if (data.extraJumps > ExtraJumpCount)
            {
                data.extraJumps = character.refs != null && character.refs.afflictions != null
                    ? character.refs.afflictions.totalExtraJumps
                    : 0;
            }

            // 攀爬不滑落：slippy 会让角色在攀爬时下滑。
            if (NoSlip)
            {
                data.slippy = 0f;
            }
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"应用能力开关失败: {e.Message}");
        }
    }

    /// <summary>
    /// 手持物品不消耗：使用次数与剩余百分比在游戏里都是逐次递减的，
    /// 这里在它们变化后立刻写回去，等价于"用不完"。
    /// 只在数值真的变了才写，避免每帧都触发一次物品数据同步。
    /// </summary>
    private static void ApplyItemCheats(Character character)
    {
        if (!NoItemConsume || character.data == null)
        {
            return;
        }

        var item = character.data.currentItem;
        if (item == null)
        {
            return;
        }

        try
        {
            var uses = item.GetData<OptionableIntItemData>(DataEntryKey.ItemUses);
            if (uses.HasData && uses.Value < item.totalUses)
            {
                uses.Value = item.totalUses;
                item.WasActive();
            }

            var remaining = item.GetData<FloatItemData>(DataEntryKey.UseRemainingPercentage);
            if (remaining.Value < 1f)
            {
                item.SetUseRemainingPercentage(1f);
            }
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"恢复手持物品状态失败: {e.Message}");
        }
    }

    private static void ApplyNoClip(Character character)
    {
        if (NoClip == noClipApplied)
        {
            return;
        }

        var ragdoll = character.refs != null ? character.refs.ragdoll : null;
        if (ragdoll == null)
        {
            return;
        }

        try
        {
            ragdoll.ToggleCollision(!NoClip);
            noClipApplied = NoClip;
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"切换碰撞失败: {e.Message}");
        }
    }

    /// <summary>
    /// 用"先撤销上一轮、再施加本轮"的方式叠加倍率，
    /// 这样不会覆盖掉游戏本身（或其它 Mod）对同一字段的修改。
    /// </summary>
    private static float UpdateMultiplier(float applied, float desired, Action<float> apply)
    {
        if (Mathf.Approximately(applied, desired))
        {
            return applied;
        }

        if (!Mathf.Approximately(applied, 1f))
        {
            apply(1f / applied);
        }

        if (!Mathf.Approximately(desired, 1f))
        {
            apply(desired);
        }

        return desired;
    }

    private static void ResetApplied()
    {
        appliedSpeed = 1f;
        appliedJump = 1f;
        appliedWallClimbSpeed = 1f;
        appliedVineClimbSpeed = 1f;
        appliedRopeClimbSpeed = 1f;
        appliedWallClimbStamina = 1f;
        appliedVineClimbStamina = 1f;
        appliedRopeClimbStamina = 1f;
        infiniteStaminaApplied = false;
        noClipApplied = false;
        flightHoldActive = false;
    }

    private static void Forget()
    {
        trackedCharacter = null;
        ResetApplied();
    }
}
