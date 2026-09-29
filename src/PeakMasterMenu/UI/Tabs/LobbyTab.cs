using System;
using Peak.Network;
using PeakMasterMenu.UI;
using Photon.Pun;
using TMPro;
using UnityEngine;

namespace PeakMasterMenu.UI.Tabs;

/// <summary>
/// 大厅 / 房间控制页：房间信息、人数上限、游戏内时间、收尾操作。
///
/// 时间本来是一个独立页签，但它调的也是「这一局怎么跑」，所以由 <see cref="WorldTab"/>
/// 接在人数上限后面（见 <see cref="MenuTab.BuildInto"/>）。
/// </summary>
internal sealed class LobbyTab : MenuTab
{
    private readonly WorldTab world = new();

    private TextMeshProUGUI roomName = null!;
    private TextMeshProUGUI roomCount = null!;
    private TextMeshProUGUI roomMax = null!;
    private TextMeshProUGUI roomHost = null!;
    private Action maxPlayersRefresh = null!;
    private float nextRoomRefresh;

    internal override string Title => "大厅";

    protected override void Build()
    {
        UiFactory.Section(Content, "房间信息");
        roomName = UiFactory.ValueRow(Content, "房间名", "-");
        roomCount = UiFactory.ValueRow(Content, "当前人数", "-");
        roomMax = UiFactory.ValueRow(Content, "人数上限", "-");
        roomHost = UiFactory.ValueRow(Content, "我是房主", "-");

        UiFactory.Section(Content, "人数上限");
        maxPlayersRefresh = UiFactory.StepperRow(
            Content,
            "最大玩家数",
            () => NetworkingUtilities.MAX_PLAYERS,
            () => 8,
            NetworkingUtilities.SetMaxPlayers);
        UiFactory.Hint(Content, "Photon 的房间上限在创建房间时就已确定，这里修改只对下一次创建房间生效。");

        // 时间控制也属于「这一局怎么跑」，接在房间设置后面。
        world.BuildInto(Content);

        UiFactory.Section(Content, "操作");
        UiFactory.ActionButton(Content, "强制结束本局（房主）", ForceEndGame);
        UiFactory.ActionButton(Content, "离开房间", () => Player.LeaveCurrentGame());
    }

    internal override void OnShown()
    {
        RefreshRoomInfo();
        world.OnShown();
    }

    internal override void Tick()
    {
        // 时间滑条要每帧刷新，不能被房间信息那 0.5 秒的节流卡在后面。
        world.Tick();

        if (Time.unscaledTime < nextRoomRefresh)
        {
            return;
        }

        nextRoomRefresh = Time.unscaledTime + 0.5f;
        RefreshRoomInfo();
    }

    private void RefreshRoomInfo()
    {
        var inRoom = PhotonNetwork.InRoom && PhotonNetwork.CurrentRoom != null;

        roomName.text = inRoom ? PhotonNetwork.CurrentRoom!.Name : "未在房间中";
        roomCount.text = inRoom ? PhotonNetwork.CurrentRoom!.PlayerCount.ToString() : "-";
        roomMax.text = inRoom ? PhotonNetwork.CurrentRoom!.MaxPlayers.ToString() : "-";
        roomHost.text = inRoom ? (PhotonNetwork.IsMasterClient ? "是" : "否") : "-";

        maxPlayersRefresh?.Invoke();
    }

    private static void ForceEndGame()
    {
        if (!PhotonNetwork.IsMasterClient)
        {
            Plugin.Log.LogInfo("只有房主可以强制结束本局。");
            return;
        }

        var character = Character.localCharacter;
        if (character == null)
        {
            return;
        }

        try
        {
            character.EndGame();
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"强制结束本局失败: {e}");
        }
    }
}
