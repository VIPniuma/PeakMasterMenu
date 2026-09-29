using System;
using UnityEngine;

namespace PeakMasterMenu;

/// <summary>
/// 角色世界坐标的唯一取法。
///
/// PEAK 的玩家是 ragdoll 驱动的：所有位移都发生在每块 <c>Bodypart</c> 自己的刚体上
/// （<c>CharacterRagdoll.MoveAllRigsInDirection</c>、<c>Character.WarpPlayer</c> 都只动 <c>part.Rig</c>），
/// 角色根节点的 transform 从出生那一刻起就再也不会变。
/// 所以读 <c>character.transform.position</c> 永远只能拿到出生点——它看起来就是"坐标不动"。
/// 游戏自己的 <c>Character.Center</c>（躯干刚体位置）才是权威坐标。
/// </summary>
internal static class CharacterBody
{
    /// <summary>角色当前的世界坐标；躯干还没注册好时退回根节点位置。</summary>
    internal static Vector3 Position(Character? character)
    {
        if (character == null)
        {
            return Vector3.zero;
        }

        try
        {
            return character.Center;
        }
        catch (Exception)
        {
            // 刚出生 / 正在切场景时躯干可能还没准备好。
            return character.transform.position;
        }
    }

    /// <summary>头顶上方一点的位置，适合放屏幕标记。</summary>
    internal static Vector3 MarkerAnchor(Character? character, float height)
    {
        return Position(character) + (Vector3.up * height);
    }
}
