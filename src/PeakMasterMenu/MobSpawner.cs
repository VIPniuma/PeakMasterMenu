using System;
using Photon.Pun;
using UnityEngine;

namespace PeakMasterMenu;

/// <summary>
/// 怪物生成器。
///
/// 「物品生成器」只能刷物品：预制体全部来自游戏的 <c>ItemDatabase</c>，
/// 再由房主把它塞进目标手里（<c>GameUtils.InstantiateAndGrab</c>）。
/// 野生怪不在物品库里，走的是另一条路 —— 游戏自己刷怪就一句话：
/// <c>PhotonNetwork.Instantiate(预制体名, 位置, 朝向, 0)</c>，
/// 而且都在房主端执行（见 <c>MushroomZombieSpawner.Spawn</c>）。
///
/// 这里的「预制体名」必须写成 <c>Resources.Load</c> 认得的那种名字：
/// PEAK 的 PhotonNetwork.PrefabPool 被 PEAKLib 换成了 CustomPrefabPool，
/// 里面没登记的原版预制体由 Photon 的 DefaultPool 兜底，而 DefaultPool 就是
/// <c>Resources.Load(prefabId)</c>。所以名字对不上就会报
/// "DefaultPool failed to load ..."（名字对不上是很容易踩的坑：
/// 类名 ≠ 预制体名，比如 <c>Skelleton</c> 类的预制体叫 "Skeleton"）。
///
/// 名字按游戏自己的 ResourceManager 表（globalgamemanagers）抄的：
/// Resources 根目录的直接写名字，放在 0_Items 文件夹里的要带前缀。
/// </summary>
internal static class MobSpawner
{
    /// <summary>生成在玩家身前多少米（留点距离，别一刷出来就贴脸）。</summary>
    private const float SpawnDistance = 8f;

    /// <summary>界面上的一个怪物条目。</summary>
    internal sealed class Entry
    {
        internal Entry(string label, params string[] resourceIds)
        {
            Label = label;
            ResourceIds = resourceIds;
        }

        /// <summary>界面上显示的名字。</summary>
        internal string Label { get; }

        /// <summary>候选的 Resources 资源名，按顺序探测，第一个能加载到的就用它。</summary>
        internal string[] ResourceIds { get; }
    }

    /// <summary>
    /// 能刷出来的怪，名字都是游戏资源里真实存在的。
    ///
    /// 蜘蛛、蚁狮、秃鹰、青蛙舌、野猪机器人、水豚这些是随地图预置在关卡里的，
    /// Resources 里没有它们的预制体，靠名字刷不出来，所以不在列表里。
    /// </summary>
    internal static readonly Entry[] Entries =
    {
        new Entry("甲虫", "0_Items/Beetle", "Beetle"),
        new Entry("蝎子", "0_Items/Scorpion", "Scorpion"),
        new Entry("蜜蜂群", "BeeSwarm", "0_Items/BeeSwarm"),
        new Entry("蘑菇僵尸", "MushroomZombie", "0_Items/MushroomZombie"),
        new Entry("风滚草", "TumbleWeed", "0_Items/TumbleWeed"),
        new Entry("骷髅", "Skeleton", "0_Items/Skeleton"),
        new Entry("蜂巢", "0_Items/Beehive", "Beehive"),
        new Entry("BingBong", "0_Items/BingBong", "BingBong"),
        new Entry("童子军大师", "Character_Scoutmaster", "0_Items/Character_Scoutmaster"),
    };

    /// <summary>在本地玩家身前生成一只怪，返回给界面显示的提示文字。</summary>
    internal static string Spawn(Entry entry)
    {
        var character = Character.localCharacter;
        if (character == null || character.data == null)
        {
            return "还没进入一局游戏，无法生成怪物。";
        }

        if (!PhotonNetwork.InRoom)
        {
            return "还没连上房间，无法生成怪物。";
        }

        // 游戏自己的刷怪（Spawner / Beehive / MushroomZombieSpawner）都只在房主端跑，
        // 客人端刷出来的怪很容易和房主那边对不上，所以这里也只让房主来。
        if (!PhotonNetwork.IsMasterClient)
        {
            return "只有房主能生成怪物（游戏自己也是在房主端刷怪的）。";
        }

        var resourceId = ResolveResourceId(entry);
        if (resourceId == null)
        {
            return $"这个版本里找不到「{entry.Label}」的预制体，刷不了。";
        }

        try
        {
            var position = SpawnPosition(character);
            var spawned = PhotonNetwork.Instantiate(resourceId, position, Quaternion.identity, 0);

            if (spawned == null)
            {
                return $"生成「{entry.Label}」失败，详见日志。";
            }

            Plugin.Log.LogInfo($"已生成怪物 {entry.Label}（预制体 {resourceId}）：{position}");
            return $"已生成 {entry.Label}";
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"生成怪物 {entry.Label}（预制体 {resourceId}）失败: {e}");
            return $"生成「{entry.Label}」失败，详见日志。";
        }
    }

    /// <summary>
    /// 挑一个真的能加载到的资源名。
    ///
    /// 先用 <c>Resources.Load</c> 探一遍是有意的：DefaultPool 内部做的就是这件事，
    /// 能加载到的名字交给 <c>PhotonNetwork.Instantiate</c> 就一定会成功；
    /// 加载不到的话 Photon 那边只会打一串红字，不如在这里就拦下来。
    /// </summary>
    private static string? ResolveResourceId(Entry entry)
    {
        foreach (var candidate in entry.ResourceIds)
        {
            try
            {
                if (Resources.Load<GameObject>(candidate) != null)
                {
                    return candidate;
                }
            }
            catch (Exception)
            {
                // 资源名不合法就试下一个候选。
            }
        }

        return null;
    }

    /// <summary>
    /// 生成位置：玩家身前几米的地面；够不到地面就退回身前半空。
    /// 刷怪和刷假人（<see cref="Dummies"/>）共用同一套落点规则。
    /// </summary>
    internal static Vector3 SpawnPosition(Character character)
    {
        var camera = MainCamera.instance;
        var forward = camera != null
            ? Vector3.ProjectOnPlane(camera.transform.forward, Vector3.up).normalized
            : character.transform.forward;

        if (forward.sqrMagnitude < 0.001f)
        {
            forward = character.transform.forward;
        }

        // 位置必须取躯干刚体：角色根节点的 transform 是出生点，永远不动。
        var origin = CharacterBody.Position(character);
        var spot = origin + (forward * SpawnDistance) + (Vector3.up * 2f);

        try
        {
            var hit = HelperFunctions.LineCheck(
                spot,
                spot + (Vector3.down * 60f),
                HelperFunctions.LayerType.TerrainMap);

            if (hit.collider != null)
            {
                return hit.point + (Vector3.up * 0.5f);
            }
        }
        catch (Exception)
        {
            // 射线失败就用身前的位置。
        }

        return origin + (forward * SpawnDistance);
    }
}
