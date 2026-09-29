using System;
using HarmonyLib;
using Photon.Pun;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PeakMasterMenu.UI.Tabs;

/// <summary>
/// 游戏内时间的调节控件。
///
/// 时间由 <see cref="DayNightManager"/> 统一管理，这里通过它的
/// <c>RPCA_SyncTime</c> 广播给所有人，避免联机时只有自己的画面变。
///
/// 本类不再单独占一个页签：它的内容由 <see cref="LobbyTab"/>（大厅页）
/// 通过 <see cref="MenuTab.BuildInto"/> 接在人数上限后面。
/// </summary>
internal sealed class WorldTab : MenuTab
{
    /// <summary>拖动滑条时两次生效之间的最小间隔（秒），免得每帧都发一次同步 RPC。</summary>
    private const float ApplyInterval = 0.2f;

    private static AccessTools.FieldRef<DayNightManager, PhotonView>? photonViewRef;

    private TextMeshProUGUI currentTime = null!;
    private Slider timeSlider = null!;

    /// <summary>正在把游戏里的时间写回滑条，此时忽略滑条自己的回调。</summary>
    private bool syncingSlider;

    /// <summary>待应用的时刻，-1 表示没有改动。</summary>
    private float pendingHour = -1f;

    private float lastAppliedHour = float.NaN;
    private float nextApply;

    internal override string Title => "世界";

    protected override void Build()
    {
        UiFactory.Section(Content, "游戏内时间");
        currentTime = UiFactory.ValueRow(Content, "当前时间", "—");

        timeSlider = UiFactory.SliderRow(
            Content,
            "时刻（0-24 点）",
            0f,
            24f,
            CurrentHour() ?? 6f,
            OnSliderChanged,
            "0.0");

        UiFactory.Hint(
            Content,
            "直接拖动滑条拨时钟，松手即生效（往右是夜晚，往左回到清晨）。"
            + "联机时以房主为准：房主拨动时间会同步给所有人，非房主的改动会被房主的定期同步覆盖。");

        UiFactory.Section(Content, "说明");
        UiFactory.Hint(
            Content,
            "游戏里的一天是 24 小时制，大约 5:30 天亮、21:00 天黑。"
            + "配合屏幕正上方的时间 HUD，可以随时确认离天黑还有多久。");
    }

    /// <summary>每次切回本页都把滑条对齐到游戏里的真实时间。</summary>
    internal override void OnShown()
    {
        SyncSliderFromGame();
    }

    internal override void Tick()
    {
        UpdateCurrentTimeLabel();
        ApplyPendingTime();
    }

    // ---------------------------------------------------------------- 滑条

    private void OnSliderChanged(float hour)
    {
        if (syncingSlider)
        {
            return;
        }

        pendingHour = hour;
    }

    /// <summary>
    /// 拖动过程中不是每一次回调都发 RPC：只记住最新值，按 <see cref="ApplyInterval"/>
    /// 的节奏应用，拖动依然跟手，但不会每帧广播一次。
    /// </summary>
    private void ApplyPendingTime()
    {
        if (pendingHour < 0f || Time.unscaledTime < nextApply)
        {
            return;
        }

        var hour = pendingHour;
        pendingHour = -1f;
        nextApply = Time.unscaledTime + ApplyInterval;

        if (!float.IsNaN(lastAppliedHour) && Mathf.Abs(hour - lastAppliedHour) < 0.01f)
        {
            return;
        }

        lastAppliedHour = hour;
        JumpTo(hour);
    }

    private void SyncSliderFromGame()
    {
        if (timeSlider == null)
        {
            return;
        }

        var hour = CurrentHour();
        if (hour == null)
        {
            return;
        }

        syncingSlider = true;
        timeSlider.value = hour.Value;
        syncingSlider = false;

        // 这次赋值只是「显示当前进度」，不能当成玩家拨动了时钟。
        pendingHour = -1f;
        lastAppliedHour = hour.Value;
    }

    private static float? CurrentHour()
    {
        var manager = DayNightManager.instance;
        if (manager == null)
        {
            return null;
        }

        try
        {
            return Mathf.Repeat(manager.timeOfDay, 24f);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private void UpdateCurrentTimeLabel()
    {
        if (currentTime == null)
        {
            return;
        }

        var manager = DayNightManager.instance;
        if (manager == null)
        {
            currentTime.text = "未进入游戏";
            return;
        }

        try
        {
            var text = manager.FloatToTimeString(manager.timeOfDay);
            var phase = PhaseName(manager.timeOfDay);
            currentTime.text = string.IsNullOrEmpty(phase) ? text : $"{text}（{phase}）";
        }
        catch (Exception)
        {
            currentTime.text = "—";
        }
    }

    private static string PhaseName(float time)
    {
        if (time >= 21f || time < 5.5f)
        {
            return "夜晚";
        }

        if (time >= 17.5f)
        {
            return "黄昏";
        }

        if (time >= 11.5f)
        {
            return "下午";
        }

        return "清晨";
    }

    private static void JumpTo(float hour)
    {
        var manager = DayNightManager.instance;
        if (manager == null)
        {
            Plugin.Log.LogWarning("还没有进入一局游戏，无法调整时间。");
            return;
        }

        // 先广播，再本地立即生效，避免同一帧里读到的还是旧时间。
        try
        {
            photonViewRef ??= AccessTools.FieldRefAccess<DayNightManager, PhotonView>("photonView");
            var view = photonViewRef(manager);
            if (view != null && view.ViewID != 0)
            {
                view.RPC("RPCA_SyncTime", RpcTarget.All, manager.dayCount, hour);
            }
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"同步时间失败，改为只调整本地画面: {e.Message}");
        }

        try
        {
            manager.setTimeOfDay(hour);
            Plugin.Log.LogDebug($"时间已调整到 {hour:0.00} 点。");
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"调整时间失败: {e}");
        }
    }
}
