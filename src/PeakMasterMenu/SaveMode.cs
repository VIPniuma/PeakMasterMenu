using Photon.Pun;
using UnityEngine;

namespace PeakMasterMenu;

/// <summary>
/// 保命模式。
///
/// 设计要点：<see cref="Plugin.SaveMeKey"/> 这个键**只在**下面两种情况下才会被消费——
/// 1. 自己已经倒下或死亡，屏幕上正显示「按 F 求救」；
/// 2. 保命模式已经启动，屏幕上正显示「按 F 关闭」。
///
/// 其余任何时刻我们都完全不碰这个按键，所以它平时依旧是游戏原本的功能。
/// </summary>
internal static class SaveMode
{
    /// <summary>保命模式是否处于启动状态。</summary>
    internal static bool Active { get; private set; }

    /// <summary>是否应该显示「按 F 求救」提示。</summary>
    internal static bool Prompt { get; private set; }

    /// <summary>由插件每帧调用。</summary>
    internal static void Update()
    {
        var character = Character.localCharacter;
        var down = character != null
            && character.data != null
            && (character.data.dead || character.data.fullyPassedOut);

        if (Active)
        {
            // 保命模式期间持续压制开关，避免被菜单或其它逻辑关掉。
            Cheats.GodMode = true;
            Cheats.InfiniteStamina = true;
            Prompt = false;
        }
        else
        {
            Prompt = down;
        }

        if (!Prompt && !Active)
        {
            return;
        }

        // 菜单开着时按键属于输入框，别拿去触发保命。
        if (Plugin.MenuOpen)
        {
            return;
        }

        if (Input.GetKeyDown(Plugin.SaveMeKey.Value))
        {
            Toggle(character);
        }
    }

    /// <summary>关闭保命模式（菜单里的一键关闭按钮也会调它）。</summary>
    internal static void Disable()
    {
        if (!Active)
        {
            return;
        }

        Active = false;
        Prompt = false;
        Cheats.GodMode = false;
        Cheats.InfiniteStamina = false;
    }

    private static void Toggle(Character? character)
    {
        if (Active)
        {
            Disable();
            Plugin.Log.LogInfo("保命模式已关闭。");
            return;
        }

        Active = true;
        Prompt = false;
        Cheats.GodMode = true;
        Cheats.InfiniteStamina = true;

        StandUpSelf(character);
        Plugin.Log.LogInfo("保命模式已启动：无敌 + 无限体力。再按一次求救键关闭。");
    }

    /// <summary>
    /// 让人从「倒下 / 死亡」里起来。
    ///
    /// 刻意**不走**游戏的复活流程（<c>RPCA_ReviveAtPosition</c>）：那条路会丢掉身上
    /// 的装备，还会把人搬到 <c>debugLastDied</c>（最后一次死亡点，没死过时甚至是默认的
    /// 原点），所以才会出现「按了 F 之后被传送走」。
    ///
    /// 让人躺着起不来的根源其实只是异常状态与石化，把它们清干净，游戏自己就会让人
    /// 在原地站起来——位置、装备都不会动。
    /// </summary>
    private static void StandUpSelf(Character? character)
    {
        if (character == null || character.data == null)
        {
            return;
        }

        var data = character.data;
        if (!data.dead && !data.fullyPassedOut)
        {
            return;
        }

        if (character.refs == null || character.refs.afflictions == null)
        {
            return;
        }

        try
        {
            // 真正死亡时游戏已经把尸体搬去了 DeathPos（天上），此时直接清状态人会在半空，
            // 所以要退回「最后一次活着的位置」——也就是你倒下的地方。
            // 只是晕倒的话身体还躺在原地，完全不需要任何位移。
            var bodyMovedAway = data.dead;
            var standPosition = bodyMovedAway ? character.LastLivingPosition : character.Center;

            // 游戏自带的状态清理：重置 dead / passedOut / fullyPassedOut / deathTimer，
            // 并清掉身上的 affliction。掉落物品和位移都写在 RPCA_ReviveAtPosition 里，
            // 我们不调它，所以就都不会发生。applyStatus 传 false，自然也不会附赠
            // 复活后的诅咒与饥饿。
            character.photonView.RPC("ReviveCharacter", RpcTarget.All, false);

            // 再把石化与全部异常状态（含诅咒）清干净，避免刚站起来又立刻倒下。
            character.refs.afflictions.RemoveAllThorns();
            character.refs.afflictions.ClearAllStatus(excludeCurse: false, excludePetrify: false);
            data.SetPetrify(0);

            // 直接交出身体控制权，省掉游戏那几秒的「苏醒」过渡。
            data.passOutValue = 0f;
            data.deathTimer = 0f;

            if (bodyMovedAway)
            {
                // 尸体已经被搬走了，只把身体搬回倒下点；不做任何其它复活附带效果。
                character.photonView.RPC("WarpPlayerRPC", RpcTarget.All, standPosition, true);

                if (character.refs.stats != null)
                {
                    character.refs.stats.justDied = false;
                    character.refs.stats.justRevived = true;
                }
            }
        }
        catch (System.Exception e)
        {
            Plugin.Log.LogError($"自救失败: {e}");
        }
    }
}
