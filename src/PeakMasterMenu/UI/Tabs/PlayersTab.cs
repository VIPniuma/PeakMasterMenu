using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using Photon.Pun;
using TMPro;
using UnityEngine;
using Zorro.Core;

namespace PeakMasterMenu.UI.Tabs;

/// <summary>
/// 传送页：队友位移、阶段传送、坐标书签与观战切换。
/// 所有位移都走游戏自己的 <c>WarpPlayerRPC</c>，联机时队友本人也会看到位置变化。
/// </summary>
internal sealed class PlayersTab : MenuTab
{
    private sealed class Bookmark
    {
        internal string Name = string.Empty;
        internal Vector3 Position;
    }

    private readonly List<Bookmark> bookmarks = new();

    private RectTransform playerList = null!;
    private RectTransform bookmarkList = null!;
    private RectTransform dummyList = null!;
    private TextMeshProUGUI listHint = null!;
    private TextMeshProUGUI stageHint = null!;
    private TextMeshProUGUI dummyHint = null!;

    private string playerStateKey = string.Empty;
    private float nextPlayerRefresh;

    internal override string Title => "传送";

    protected override void Build()
    {
        UiFactory.Section(Content, "快速操作");
        UiFactory.ActionButton(Content, "全部传送到我", TeleportEveryoneToMe);
        UiFactory.ActionButton(Content, "把附近的物品吸到脚下", Cheats.PullNearbyItems);
        UiFactory.Hint(Content, "「全部传送到我」会让房间里的每位队友落到你身边，联机时对方也会看到位移。");

        UiFactory.Section(Content, "玩家列表");
        UiFactory.ActionButton(Content, "刷新玩家列表", RebuildPlayers);
        listHint = UiFactory.Hint(Content, "进入一局游戏后才会显示玩家。");
        playerList = UiFactory.List(Content, "PlayerList");

        UiFactory.Section(Content, "测试假人");
        UiFactory.ActionButton(Content, "刷一个假人", SpawnDummy);
        UiFactory.ActionButton(Content, "清掉所有假人", ClearDummies);
        dummyHint = UiFactory.Hint(
            Content,
            "一个人测不过瘾时可以刷个假人当靶子：用的是游戏自己的机器人，纯本地生成（只有你看得见），"
            + "队友标记 / 体力条 / 传送这些都会把它算上。它没有 AI，会原地站着；游戏当它是 bot，"
            + "所以抓不起来，僵尸也不会追它。");
        dummyList = UiFactory.List(Content, "DummyList");

        UiFactory.Section(Content, "阶段传送");
        UiFactory.ActionButton(Content, "传送上一阶段", () => JumpSegment(-1));
        UiFactory.ActionButton(Content, "传送下一阶段", () => JumpSegment(1));
        stageHint = UiFactory.Hint(Content, "进入一局游戏后，这里会显示这局实际的关卡段。");
        UiFactory.Hint(
            Content,
            "每天的地图都不一样，所以段数和每段是什么都在进图后现读，不写死。"
            + "房主点了直接换图，其他人点会转交给房主执行；换图会重置这一阶段的地形与物资。");

        UiFactory.Section(Content, "坐标书签");
        UiFactory.ActionButton(Content, "把当前位置存为书签", AddBookmark);
        bookmarkList = UiFactory.List(Content, "BookmarkList");

        UiFactory.Section(Content, "观战");
        UiFactory.ActionButton(Content, "上一个观战目标", () => CycleSpectate(-1));
        UiFactory.ActionButton(Content, "下一个观战目标", () => CycleSpectate(1));
        UiFactory.Hint(Content, "只有倒下或死亡、已经进入观战状态时才生效。");
    }

    internal override void OnShown()
    {
        RebuildPlayers();
        RebuildDummies();
        RebuildBookmarks();
        RefreshStageHint();
    }

    /// <summary>
    /// 本页开着的时候盯一下队友的生死状态：有人倒下 / 阵亡时，
    /// 名字后面的「复活」按钮要自己冒出来，不用手动点刷新。
    /// </summary>
    internal override void Tick()
    {
        if (Time.unscaledTime < nextPlayerRefresh)
        {
            return;
        }

        nextPlayerRefresh = Time.unscaledTime + 0.5f;

        var key = PlayerStateKey();
        if (key != playerStateKey)
        {
            RebuildPlayers();
        }

        // 换段要等房主那边同步过来，所以这里也跟着刷一下。
        RefreshStageHint();
    }

    // ---------------------------------------------------------------- 队友位移

    private void RebuildPlayers()
    {
        if (playerList == null)
        {
            return;
        }

        Clear(playerList);
        playerStateKey = PlayerStateKey();

        var characters = GetCharacters();
        if (characters.Count == 0)
        {
            listHint.text = "进入一局游戏后才会显示玩家。";
            return;
        }

        var isMaster = false;
        try
        {
            isMaster = PhotonNetwork.IsMasterClient;
        }
        catch (Exception)
        {
            // 未联机时忽略。
        }

        listHint.text = $"共 {characters.Count} 名玩家。"
            + (isMaster ? "你是房主，可以踢人。" : "只有房主能踢人。");

        foreach (var character in characters)
        {
            var captured = character;
            var downed = IsDowned(character);
            var card = UiFactory.Row(playerList, 96f, "PlayerCard");
            var background = UiFactory.Panel(card, UiFactory.ButtonSprite, UiFactory.TabBg, false, "Bg");
            UiFactory.Stretch(background.rectTransform);

            var name = UiFactory.Label(
                background.transform,
                CharacterTitle(character),
                22f,
                TextAlignmentOptions.Left,
                downed ? UiFactory.TabActiveBg : UiFactory.TextMain);
            name.rectTransform.anchorMin = new Vector2(0f, 1f);
            name.rectTransform.anchorMax = new Vector2(1f, 1f);
            name.rectTransform.pivot = new Vector2(0.5f, 1f);
            name.rectTransform.offsetMin = new Vector2(16f, 0f);
            // 名字后面要留给「复活」按钮，所以把文本右边界收一收。
            name.rectTransform.offsetMax = new Vector2(downed ? -132f : -16f, -8f);
            name.rectTransform.sizeDelta = new Vector2(name.rectTransform.sizeDelta.x, 30f);

            if (downed)
            {
                // 只有倒下 / 阵亡的玩家才会在名字右边出现这个按钮。
                var revive = UiFactory.Button(background.transform, "复活", () => Revive(captured), 18f, UiFactory.OnBg);
                var reviveRect = revive.image.rectTransform;
                reviveRect.anchorMin = new Vector2(1f, 1f);
                reviveRect.anchorMax = new Vector2(1f, 1f);
                reviveRect.pivot = new Vector2(1f, 1f);
                reviveRect.sizeDelta = new Vector2(104f, 30f);
                reviveRect.anchoredPosition = new Vector2(-16f, -8f);
            }

            var actions = UiFactory.HorizontalRow(background.transform, 42f, 8f, "Actions");
            actions.anchorMin = new Vector2(0f, 0f);
            actions.anchorMax = new Vector2(1f, 0f);
            actions.pivot = new Vector2(0.5f, 0f);
            actions.offsetMin = new Vector2(12f, 10f);
            actions.offsetMax = new Vector2(-12f, 52f);

            UiFactory.Button(actions, "传送到他", () => TeleportTo(captured), 20f);
            UiFactory.Button(actions, "传送到我", () => TeleportToMe(captured), 20f);

            if (isMaster)
            {
                UiFactory.Button(actions, "踢出", () => Kick(captured), 20f);
            }
        }
    }

    // ---------------------------------------------------------------- 测试假人

    private void SpawnDummy()
    {
        dummyHint.text = Dummies.Spawn();
        RebuildDummies();
    }

    private void ClearDummies()
    {
        dummyHint.text = Dummies.Clear();
        RebuildDummies();
    }

    private void RemoveDummy(Character dummy)
    {
        Dummies.Remove(dummy);
        RebuildDummies();
    }

    /// <summary>
    /// 假人卡片。排版和玩家卡片一致，但动作只有位移和删除 ——
    /// 假人不是真的联机玩家，踢人 / 复活那套在它身上没有意义。
    /// </summary>
    private void RebuildDummies()
    {
        if (dummyList == null)
        {
            return;
        }

        Clear(dummyList);

        var dummies = Dummies.Characters;
        if (dummies.Count == 0)
        {
            dummyHint.text = "现在没有假人。点「刷一个假人」会在你身前生成一个。";
            return;
        }

        foreach (var dummy in dummies)
        {
            var captured = dummy;
            var card = UiFactory.Row(dummyList, 96f, "DummyCard");
            var background = UiFactory.Panel(card, UiFactory.ButtonSprite, UiFactory.TabBg, false, "Bg");
            UiFactory.Stretch(background.rectTransform);

            var title = UiFactory.Label(
                background.transform,
                DummyTitle(dummy),
                22f,
                TextAlignmentOptions.Left,
                UiFactory.TextMain);
            title.rectTransform.anchorMin = new Vector2(0f, 1f);
            title.rectTransform.anchorMax = new Vector2(1f, 1f);
            title.rectTransform.pivot = new Vector2(0.5f, 1f);
            title.rectTransform.offsetMin = new Vector2(16f, 0f);
            title.rectTransform.offsetMax = new Vector2(-16f, -8f);
            title.rectTransform.sizeDelta = new Vector2(title.rectTransform.sizeDelta.x, 30f);

            var actions = UiFactory.HorizontalRow(background.transform, 42f, 8f, "Actions");
            actions.anchorMin = new Vector2(0f, 0f);
            actions.anchorMax = new Vector2(1f, 0f);
            actions.pivot = new Vector2(0.5f, 0f);
            actions.offsetMin = new Vector2(12f, 10f);
            actions.offsetMax = new Vector2(-12f, 52f);

            UiFactory.Button(actions, "传送到他", () => TeleportTo(captured), 20f);
            UiFactory.Button(actions, "传送到我", () => TeleportToMe(captured), 20f);
            UiFactory.Button(actions, "删除", () => RemoveDummy(captured), 20f);
        }
    }

    /// <summary>假人卡片上的标题：名字 + 距离。</summary>
    private static string DummyTitle(Character dummy)
    {
        var name = SafeCharacterName(dummy);
        var local = Character.localCharacter;

        if (local == null || ReferenceEquals(local, dummy))
        {
            return name;
        }

        var distance = Vector3.Distance(CharacterBody.Position(local), CharacterBody.Position(dummy));
        return $"{name}  {distance:F0}m";
    }

    private static void TeleportTo(Character target)
    {
        var local = Character.localCharacter;
        if (local == null || target == null || ReferenceEquals(local, target))
        {
            return;
        }

        Warp(local, StandPosition(target));
    }

    private static void TeleportToMe(Character target)
    {
        var local = Character.localCharacter;
        if (local == null || target == null || ReferenceEquals(local, target))
        {
            return;
        }

        // 假人落到自己侧后方：两具 ragdoll 直接叠在一起会互相挤，容易把人顶飞。
        var position = Dummies.DisplayName(target) != null ? DummyDropPosition(local) : StandPosition(local);
        Warp(target, position);
    }

    /// <summary>
    /// 假人放到玩家身边时的落点：脚下侧后方一点。
    ///
    /// 方向取 <c>lookDirection_Flat</c> 而不是 <c>transform.forward</c> —— 角色根节点是出生点，
    /// 它上面的朝向从出生起就没再变过。
    /// </summary>
    private static Vector3 DummyDropPosition(Character local)
    {
        var forward = local.data != null ? local.data.lookDirection_Flat : Vector3.forward;
        forward.y = 0f;

        if (forward.sqrMagnitude < 0.001f)
        {
            forward = Vector3.forward;
        }
        else
        {
            forward = forward.normalized;
        }

        var right = Vector3.Cross(Vector3.up, forward);
        return CharacterBody.Position(local) + (-forward * 1.4f) + (right * 1.2f) + (Vector3.up * 0.4f);
    }

    private void TeleportEveryoneToMe()
    {
        var local = Character.localCharacter;
        if (local == null)
        {
            Plugin.Log.LogWarning("还没有进入一局游戏，无法传送。");
            return;
        }

        var position = StandPosition(local);
        var count = 0;

        foreach (var character in GetCharacters())
        {
            if (character == null || ReferenceEquals(character, local))
            {
                continue;
            }

            Warp(character, position);
            count++;
        }

        Plugin.Log.LogInfo($"已把 {count} 名队友传送到身边。");
    }

    private static void Warp(Character character, Vector3 position)
    {
        try
        {
            var view = character.photonView;

            // 本地假人不在 Photon 里（视图号是 0）：发 RPC 没人收、还会报错；
            // 游戏的 WarpPlayer 也不适合它 —— 那条路是给注册过的真人角色准备的，
            // 假人身上的网络位移组件收不到数据，会把刚挪走的身体又拽回去。
            // 所以假人走模组自己的本地位移。
            if (view == null || view.ViewID == 0)
            {
                LocalWarp(character, position);
                return;
            }

            view.RPC("WarpPlayerRPC", RpcTarget.All, position, true);
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"传送失败: {e}");
        }
    }

    /// <summary>
    /// 本地假人的位移。
    ///
    /// 只动 ragdoll 是不够的：假人根节点上挂着网络的位移同步组件（视图号 0，收不到任何数据），
    /// 它每帧会把角色往"它记下的位置"拽，刚体挪完就被拉回出生点，看上去完全没动。
    /// 所以这里连根节点一起摆好，再连补几帧 —— 每帧按目标重算一次差值，
    /// 谁在后面拽都会被这一步盖过去，直到假人稳稳落在目标位置。
    /// </summary>
    private static void LocalWarp(Character character, Vector3 position)
    {
        try
        {
            character.StartCoroutine(LocalWarpRoutine(character, position));
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"假人传送失败: {e}");
        }
    }

    private static IEnumerator LocalWarpRoutine(Character character, Vector3 position)
    {
        LogDummyRig(character);

        // 根节点先摆到目标：有些跟随逻辑是拿根节点当基准的。
        character.transform.position = position;

        for (var step = 0; step < 12 && character != null; step++)
        {
            var delta = position - CharacterBody.Position(character);
            if (delta.sqrMagnitude < 0.01f)
            {
                break;
            }

            var ragdoll = character.refs.ragdoll;
            if (ragdoll != null && ragdoll.partList != null)
            {
                foreach (var part in ragdoll.partList)
                {
                    var rig = part != null ? part.Rig : null;
                    if (rig == null)
                    {
                        continue;
                    }

                    // 直接设坐标（不等物理步）：立刻生效，也不受 MovePosition 时序的影响。
                    rig.position += delta;
                    rig.linearVelocity = Vector3.zero;
                    rig.angularVelocity = Vector3.zero;
                }
            }

            yield return null;
        }

        Plugin.Log.LogInfo($"[假人] 目标 {position}，落地后 {CharacterBody.Position(character)}");
    }

    /// <summary>
    /// 把假人身上"可能和位置较劲"的组件打一份：万一还是传不动，看日志就知道是谁在拽。
    /// </summary>
    private static void LogDummyRig(Character character)
    {
        try
        {
            var view = character.photonView;
            var observed = string.Empty;

            if (view != null && view.ObservedComponents != null)
            {
                foreach (var component in view.ObservedComponents)
                {
                    observed += (component == null ? "null" : component.GetType().Name) + " ";
                }
            }

            var net = string.Empty;
            foreach (var component in character.GetComponentsInChildren<Component>(true))
            {
                if (component == null)
                {
                    continue;
                }

                var type = component.GetType().Name;
                if (type.Contains("Photon") || type.Contains("View"))
                {
                    net += type + " ";
                }
            }

            Plugin.Log.LogInfo(
                $"[假人] name={Dummies.DisplayName(character)}"
                + $" viewId={(view == null ? -1 : view.ViewID)} owner={(view == null ? -1 : view.OwnerActorNr)}"
                + $" isBot={character.isBot} data={(character.data == null ? "null" : "ok")}"
                + $" observed=[{observed}] net=[{net}]");
        }
        catch (Exception)
        {
            // 诊断本身出错无所谓，别影响传送。
        }
    }

    /// <summary>
    /// 抬高一截，避免直接落到地面以下。
    /// 坐标必须取躯干刚体：角色根节点的 transform 是出生点，永远不动。
    /// </summary>
    private static Vector3 StandPosition(Character character)
    {
        return CharacterBody.Position(character) + (Vector3.up * 0.4f);
    }

    // ---------------------------------------------------------------- 落点计算

    /// <summary>往下打射线找地面时，从参考点上方多高的地方开始探。</summary>
    private const float ProbeHeight = 60f;

    /// <summary>
    /// 从参考点正上方往下打射线，落在高度上离参考点最近的那个面上，再抬高一截。
    /// 参考点埋在地形里就落到它上方的地面，悬在半空就落到正下方的地面，
    /// 这样就不会出现「传送完卡在地图下面」的情况。
    /// </summary>
    private static Vector3 LandingSpot(Vector3 anchor, Character character, float clearance = 1.5f)
    {
        var landing = anchor;
        var best = float.MaxValue;
        var found = false;

        try
        {
            var origin = anchor + (Vector3.up * ProbeHeight);
            var hits = Physics.RaycastAll(origin, Vector3.down, ProbeHeight * 2f, ~0, QueryTriggerInteraction.Ignore);
            foreach (var hit in hits)
            {
                var target = hit.transform;
                if (target == null || IsPartOf(character, target))
                {
                    continue;
                }

                var delta = Mathf.Abs(hit.point.y - anchor.y);
                if (delta >= best)
                {
                    continue;
                }

                best = delta;
                landing = hit.point;
                found = true;
            }
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"寻找落点失败，按原坐标传送: {e.Message}");
        }

        return (found ? landing : anchor) + (Vector3.up * clearance);
    }

    /// <summary>这块碰撞体是不是要传送的那个角色自己的，探地面时要跳过。</summary>
    private static bool IsPartOf(Character character, Transform target)
    {
        return character != null && (target == character.transform || target.IsChildOf(character.transform));
    }

    /// <summary>倒下（<c>fullyPassedOut</c>）或阵亡（<c>dead</c>）。</summary>
    private static bool IsDowned(Character character)
    {
        return character.data != null && (character.data.dead || character.data.fullyPassedOut);
    }

    /// <summary>
    /// 玩家名单 + 生死状态的指纹，用来判断卡片要不要重建。
    /// 先排序再拼，免得读取顺序偶尔变化时误判成"状态变了"而反复重建。
    /// </summary>
    private static string PlayerStateKey()
    {
        var tokens = new List<string>();

        foreach (var character in GetCharacters())
        {
            tokens.Add($"{character.GetInstanceID()}{(IsDowned(character) ? '1' : '0')}");
        }

        tokens.Sort(StringComparer.Ordinal);
        return string.Join("|", tokens);
    }

    /// <summary>玩家名字，倒下 / 阵亡时在后面补一句状态。</summary>
    private static string CharacterTitle(Character character)
    {
        var name = SafeCharacterName(character);

        if (character.data == null)
        {
            return name;
        }

        if (character.data.dead)
        {
            return name + "（已阵亡）";
        }

        return character.data.fullyPassedOut ? name + "（已倒下）" : name;
    }

    /// <summary>
    /// 复活一名玩家（<c>RPCA_ReviveAtPosition</c> 会顺便把角色摆到指定位置）。
    /// 位置规则：
    /// 1. 他正在灵魂观战 → 放到他观战的那个人的位置，一复活就在队友身边；
    /// 2. 其它情况 → 就地复活，用游戏自己的 <c>GetSpectatePosition()</c>
    ///    （阵亡时给的是「最后一次活着的位置」，因为尸体早被搬去 DeathPos 了）。
    /// </summary>
    private static void Revive(Character target)
    {
        if (target == null || target.data == null || !IsDowned(target))
        {
            return;
        }

        try
        {
            target.photonView.RPC(
                "RPCA_ReviveAtPosition",
                RpcTarget.All,
                RevivePosition(target),
                true,
                -1);
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"复活失败: {e}");
        }
    }

    private static Vector3 RevivePosition(Character target)
    {
        var watched = SpectateTarget(target);
        if (watched != null)
        {
            return StandPosition(watched);
        }

        return target.GetSpectatePosition() + (Vector3.up * 0.4f);
    }

    /// <summary>
    /// 灵魂观战中正在看的那个角色。
    /// <c>PlayerGhost.m_target</c> 是由 <c>RPCA_SetTarget</c>（AllBuffered）同步过来的，
    /// 所以队友的观战目标我们也能读到。
    /// </summary>
    private static Character? SpectateTarget(Character target)
    {
        try
        {
            var ghost = target.Ghost;
            var watched = ghost != null ? ghost.m_target : null;

            if (watched != null && !ReferenceEquals(watched, target) && watched.data != null)
            {
                return watched;
            }
        }
        catch (Exception)
        {
            // 没有观战对象时忽略，退回就地复活。
        }

        return null;
    }

    private static void Kick(Character target)
    {
        if (target == null)
        {
            return;
        }

        try
        {
            if (!PhotonNetwork.IsMasterClient)
            {
                Plugin.Log.LogWarning("只有房主能把玩家踢出房间。");
                return;
            }

            var owner = target.photonView != null ? target.photonView.Owner : null;
            if (owner == null)
            {
                return;
            }

            PhotonNetwork.CloseConnection(owner);
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"踢出玩家失败: {e}");
        }
    }

    // ---------------------------------------------------------------- 阶段传送

    /// <summary>
    /// 跳上 / 下一阶段（<paramref name="direction"/> 取 ±1）。
    ///
    /// 走游戏自己的 <c>MapHandler.JumpToSegment</c>：房主直接换图，
    /// 不是房主的话它会自己转成 <c>RPC_WarpToSegment</c> 交给房主执行，所以谁点都行。
    /// 换图会重置当前阶段的地形和物资。
    /// </summary>
    private void JumpSegment(int direction)
    {
        try
        {
            var segments = CurrentSegments();
            if (segments.Length == 0)
            {
                Plugin.Log.LogWarning("还没进图，读不到这局有哪些关卡段。");
                return;
            }

            var index = CurrentIndex(segments);
            if (index < 0)
            {
                Plugin.Log.LogWarning("现在不在正常的关卡段里（可能在虚空段），先回到正常关卡再试。");
                return;
            }

            var target = index + Math.Sign(direction);
            if (target < 0 || target >= segments.Length)
            {
                Plugin.Log.LogWarning(direction > 0
                    ? $"已经是最后一段了（这局共 {segments.Length} 段）。"
                    : "已经是第一段了。");
                return;
            }

            // 游戏内部把 Segment 枚举值直接当 segments 的下标用，
            // 超出 TheKiln 的值会被它自己减 1，那就跳错了，所以这里挡住。
            if (target > (int)Segment.TheKiln)
            {
                Plugin.Log.LogWarning("这一段游戏没留跳转入口，跳不过去。");
                return;
            }

            var name = BiomeNames.Of(segments[target].biome);
            MapHandler.JumpToSegment((Segment)target);
            Plugin.Log.LogInfo($"已跳到第 {target + 1}/{segments.Length} 段（{name}）。");
            RefreshStageHint();
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"阶段传送失败: {e}");
        }
    }

    /// <summary>
    /// 这局地图实际有哪些关卡段。
    ///
    /// 每天的地图都是随机重排的群系，段数和每段是什么都不能写死，
    /// 所以直接从 <c>MapHandler.segments</c> 现读；虚空段（终局之后）不算关卡。
    /// </summary>
    private static MapHandler.MapSegment[] CurrentSegments()
    {
        if (!MapHandler.Exists)
        {
            return Array.Empty<MapHandler.MapSegment>();
        }

        var segments = Singleton<MapHandler>.Instance.segments;
        if (segments == null)
        {
            return Array.Empty<MapHandler.MapSegment>();
        }

        var list = new List<MapHandler.MapSegment>(segments.Length);
        foreach (var segment in segments)
        {
            if (segment == null || segment.segmentParent == null || segment.biome == Biome.BiomeType.Void)
            {
                continue;
            }

            list.Add(segment);
        }

        return list.ToArray();
    }

    /// <summary>
    /// 当前在第几段。<c>MapHandler.CurrentSegmentNumber</c> 给的就是 <c>segments</c> 的下标
    /// （游戏内部把 Segment 枚举值当下标用），虚空段会返回 <c>Segment.Void</c>，
    /// 那就对不上这张列表了，返回 -1。
    /// </summary>
    private static int CurrentIndex(MapHandler.MapSegment[] segments)
    {
        if (!MapHandler.Exists)
        {
            return -1;
        }

        var index = (int)MapHandler.CurrentSegmentNumber;
        return index >= 0 && index < segments.Length ? index : -1;
    }

    /// <summary>页面那行「这局共几段 / 现在第几段」。</summary>
    private void RefreshStageHint()
    {
        if (stageHint == null)
        {
            return;
        }

        var text = StageHintText();
        if (stageHint.text != text)
        {
            stageHint.text = text;
        }
    }

    private static string StageHintText()
    {
        try
        {
            var segments = CurrentSegments();
            if (segments.Length == 0)
            {
                return "还没进图：进去以后才能读到这局的关卡段。";
            }

            var index = CurrentIndex(segments);
            if (index < 0)
            {
                return $"这局共 {segments.Length} 段，现在这段游戏没报告位置（可能在虚空段）。";
            }

            return $"这局共 {segments.Length} 段，现在第 {index + 1} 段：{BiomeNames.Of(segments[index].biome)}";
        }
        catch (Exception e)
        {
            return $"读取关卡段失败：{e.Message}";
        }
    }

    // ---------------------------------------------------------------- 坐标书签

    private void AddBookmark()
    {
        var local = Character.localCharacter;
        if (local == null)
        {
            Plugin.Log.LogWarning("还没有进入一局游戏，无法记录坐标。");
            return;
        }

        var position = CharacterBody.Position(local);
        bookmarks.Add(new Bookmark
        {
            Name = $"书签 {bookmarks.Count + 1}　X{position.x:F0} Y{position.y:F0} Z{position.z:F0}",
            Position = position,
        });

        RebuildBookmarks();
    }

    private void RebuildBookmarks()
    {
        if (bookmarkList == null)
        {
            return;
        }

        Clear(bookmarkList);

        if (bookmarks.Count == 0)
        {
            var hint = UiFactory.Hint(bookmarkList, "还没有书签。站在想去的位置上点「把当前位置存为书签」。");
            hint.text = "还没有书签。站在想去的位置上点「把当前位置存为书签」。";
            return;
        }

        for (var i = 0; i < bookmarks.Count; i++)
        {
            var bookmark = bookmarks[i];
            var index = i;

            var card = UiFactory.Row(bookmarkList, 96f, "BookmarkCard");
            var background = UiFactory.Panel(card, UiFactory.ButtonSprite, UiFactory.TabBg, false, "Bg");
            UiFactory.Stretch(background.rectTransform);

            var name = UiFactory.Label(
                background.transform,
                bookmark.Name,
                21f,
                TextAlignmentOptions.Left,
                UiFactory.TextMain);
            name.rectTransform.anchorMin = new Vector2(0f, 1f);
            name.rectTransform.anchorMax = new Vector2(1f, 1f);
            name.rectTransform.pivot = new Vector2(0.5f, 1f);
            name.rectTransform.offsetMin = new Vector2(16f, 0f);
            name.rectTransform.offsetMax = new Vector2(-16f, -8f);
            name.rectTransform.sizeDelta = new Vector2(name.rectTransform.sizeDelta.x, 30f);

            var actions = UiFactory.HorizontalRow(background.transform, 42f, 8f, "Actions");
            actions.anchorMin = new Vector2(0f, 0f);
            actions.anchorMax = new Vector2(1f, 0f);
            actions.pivot = new Vector2(0.5f, 0f);
            actions.offsetMin = new Vector2(12f, 10f);
            actions.offsetMax = new Vector2(-12f, 52f);

            UiFactory.Button(
                actions,
                "传送过去",
                () =>
                {
                    var local = Character.localCharacter;
                    if (local != null)
                    {
                        Warp(local, LandingSpot(bookmark.Position, local));
                    }
                },
                20f);

            UiFactory.Button(
                actions,
                "删除",
                () =>
                {
                    if (index >= 0 && index < bookmarks.Count)
                    {
                        bookmarks.RemoveAt(index);
                    }

                    RebuildBookmarks();
                },
                20f);
        }
    }

    // ---------------------------------------------------------------- 观战

    private static void CycleSpectate(int direction)
    {
        try
        {
            var camera = Singleton<MainCameraMovement>.Instance;
            if (camera == null)
            {
                return;
            }

            // SwapSpecPlayer 是私有方法，而且只有已经在观战时才有意义。
            var method = AccessTools.Method(typeof(MainCameraMovement), "SwapSpecPlayer", new[] { typeof(int) });
            if (method == null)
            {
                Plugin.Log.LogWarning("当前游戏版本没有找到观战切换方法。");
                return;
            }

            method.Invoke(camera, new object[] { direction });
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"切换观战目标失败: {e}");
        }
    }

    // ---------------------------------------------------------------- 工具

    private static List<Character> GetCharacters()
    {
        var result = new List<Character>();

        try
        {
            var characters = PlayerHandler.GetAllPlayerCharacters();
            if (characters != null)
            {
                foreach (var character in characters)
                {
                    if (character != null && character.data != null)
                    {
                        result.Add(character);
                    }
                }
            }
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"读取玩家列表失败: {e.Message}");
        }

        return result;
    }

    /// <summary>
    /// 玩家名字。
    ///
    /// 顺序和 HUD 那边一致：本模组的假人名字 → 角色的 <c>characterName</c> → Photon 昵称 → 「玩家」。
    /// 真人队友的 <c>characterName</c> 要等脏话过滤回调才写上，等不到就退回昵称，
    /// 否则一屋子人都叫「玩家」，谁也分不清谁是谁。
    /// </summary>
    private static string SafeCharacterName(Character character)
    {
        try
        {
            var name = Dummies.DisplayName(character);

            if (string.IsNullOrEmpty(name))
            {
                name = character.characterName;
            }

            if (string.IsNullOrEmpty(name) || name == "Bot")
            {
                try
                {
                    var view = character.photonView;
                    var owner = view != null ? view.Owner : null;
                    name = owner != null ? owner.NickName : null;
                }
                catch (Exception)
                {
                    // 读不到昵称就算了，下面还有兜底。
                }
            }

            if (string.IsNullOrEmpty(name))
            {
                name = "玩家";
            }

            if (character.IsLocal)
            {
                name += "（你）";
            }

            return name;
        }
        catch (Exception)
        {
            return "玩家";
        }
    }

    private static void Clear(RectTransform host)
    {
        for (var i = host.childCount - 1; i >= 0; i--)
        {
            UnityEngine.Object.Destroy(host.GetChild(i).gameObject);
        }
    }
}
