using System;
using System.Collections.Generic;
using UnityEngine;

namespace PeakMasterMenu;

/// <summary>
/// 本地测试假人：借游戏自己的「机器人」当桩子。
///
/// 为什么不用真队友：PEAK 的「队友」就是 <c>PlayerHandler</c> 里按 Photon 玩家号索引的真人角色
/// （<c>Character.Awake</c> 只把非 bot 的角色注册进去，注册时还要读 <c>view.Owner</c>），
/// 一局里没有第二个真人就刷不出来 —— 本地硬塞进去的角色要么被 <c>PlayerHandler</c>
/// 的缓存刷新清掉，要么直接顶掉你自己的角色。
///
/// 所以这里退一步，用游戏自己的机器人 <c>Bot</c>：
/// - 它是货真价实的 <c>Character</c>：有 ragdoll、有动画、站得住，
///   坐标走 <see cref="CharacterBody"/> 那一套（和玩家一样）；
/// - 游戏刷机器人用的就是 <c>Object.Instantiate</c>（见 <c>BotSpawner</c>），
///   纯本地、不过 Photon，所以照做是安全的：不会撞 ViewID，也不会把注册表搞乱；
/// - <c>isBot = true</c>，没有 <c>PatrolBoss</c> 驱动它就不会动
///   （<c>BotMoverRagdoll</c> 只按 <c>MovementInput</c> 出力，输入是 0 就原地站着），
///   游戏的 AI 也不会理它（僵尸不追它、也抓不起来），就是一根让你测的桩子。
///
/// 因为游戏不认它是玩家，所以队友标记 / 体力条 / 玩家列表这些由本模组自己把它算上
/// （见 <c>HudOverlay.Teammates</c> 与 <c>PlayersTab</c>）。
/// </summary>
internal static class Dummies
{
    private static readonly List<Character> spawned = new List<Character>();

    /// <summary>
    /// 假人自己的名字表。
    ///
    /// 不靠 <c>Character.characterName</c> 显示：那是游戏给真人（走一遍脏话过滤的 Photon 昵称）
    /// 和机器人（固定 "Bot"）准备的字段，模组写进去属于借用，下次读到的可能就被游戏重置了。
    /// 显示一律优先用这里记的。
    /// </summary>
    private static readonly Dictionary<Character, string> names = new Dictionary<Character, string>();

    /// <summary>缓存机器人预制体的来源，见 <see cref="ResolveSource"/>。</summary>
    private static GameObject? source;

    /// <summary>当前活着的假人，顺手剔除已被销毁的。</summary>
    internal static List<Character> Characters
    {
        get
        {
            for (var i = spawned.Count - 1; i >= 0; i--)
            {
                if (spawned[i] == null)
                {
                    names.Remove(spawned[i]);
                    spawned.RemoveAt(i);
                }
            }

            return spawned;
        }
    }

    /// <summary>假人名字（界面显示用）；不是本模组刷的假人返回 null。</summary>
    internal static string? DisplayName(Character? character)
    {
        if (character == null)
        {
            return null;
        }

        return names.TryGetValue(character, out var name) ? name : null;
    }

    /// <summary>删掉一个假人（名字表和列表一起清）。</summary>
    internal static void Remove(Character? character)
    {
        if (character == null)
        {
            return;
        }

        try
        {
            UnityEngine.Object.Destroy(character.gameObject);
        }
        catch (Exception)
        {
            // 已经没了就算了。
        }

        names.Remove(character);
        spawned.Remove(character);
    }

    /// <summary>在本地玩家身前刷一个假人，返回给界面显示的提示文字。</summary>
    internal static string Spawn()
    {
        var local = Character.localCharacter;
        if (local == null || local.data == null)
        {
            return "还没进入一局游戏，刷不了假人。";
        }

        var prefab = ResolveSource();
        if (prefab == null)
        {
            return "这一局里找不到机器人（游戏只在童子军营地的 BotSpawner 上挂了它的预制体），刷不了假人。";
        }

        try
        {
            var position = MobSpawner.SpawnPosition(local);
            var created = UnityEngine.Object.Instantiate(prefab, position, Quaternion.identity);
            var character = created.GetComponent<Character>();
            if (character == null)
            {
                return "生成出来的物体上没有 Character 组件，刷不了假人。";
            }

            // 假人不巡逻，把寻路组件关掉：不关的话它落在地上没有烘焙 NavMesh 的地方会刷报错。
            var navigator = created.GetComponentInChildren<Navigator>(true);
            if (navigator != null)
            {
                navigator.enabled = false;
            }

            var name = $"假人 {spawned.Count + 1}";
            names[character] = name;

            // 顺带写进游戏字段：游戏自己的界面（观战 / 计分板）也认这个名字。
            character.characterName = name;
            spawned.Add(character);

            Plugin.Log.LogInfo($"已生成假人 {name}（来源 {prefab.name}）：{position}");
            return $"已生成「{name}」。它没有 AI，会原地站着不动。";
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"生成假人失败: {e}");
            return "生成假人失败，详见日志。";
        }
    }

    /// <summary>删掉全部假人，返回给界面显示的提示文字。</summary>
    internal static string Clear()
    {
        var count = 0;

        // 先拷一份：Remove 会改动 spawned，直接在它上面遍历会抛异常。
        foreach (var character in new List<Character>(Characters))
        {
            Remove(character);
            count++;
        }

        spawned.Clear();
        names.Clear();
        return count == 0 ? "现在没有假人。" : $"已清掉 {count} 个假人。";
    }

    /// <summary>
    /// 找机器人预制体。
    ///
    /// 游戏把机器人挂在童子军营地的 <see cref="BotSpawner"/> 上，而且它不在 Resources 里
    /// （按名字加载不到），只能从场景对象上取引用。地图分段即使没走到也是躺在场景里的
    /// （只是没激活），所以要连未激活的一起找。
    ///
    /// 实在找不到（比如这局没开童子军），就退一步克隆场上已经有的机器人：
    /// 机器人是本地生成的，视图号是 0，克隆它和克隆预制体一样安全。
    /// </summary>
    private static GameObject? ResolveSource()
    {
        if (source != null)
        {
            return source;
        }

        try
        {
            foreach (var spawner in UnityEngine.Object.FindObjectsByType<BotSpawner>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (spawner != null && spawner.botPrefab != null)
                {
                    source = spawner.botPrefab;
                    return source;
                }
            }
        }
        catch (Exception)
        {
            // 取不到就试克隆。
        }

        try
        {
            foreach (var bot in UnityEngine.Object.FindObjectsByType<Bot>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (bot != null)
                {
                    source = bot.gameObject;
                    return source;
                }
            }
        }
        catch (Exception)
        {
            // 忽略。
        }

        return null;
    }
}
