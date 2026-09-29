using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using PeakMasterMenu.UI;
using UnityEngine;

namespace PeakMasterMenu;

/// <summary>
/// Mod 入口。负责读取配置、监听热键，并在第一次需要时创建菜单窗口。
/// </summary>
[BepInAutoPlugin]
public partial class Plugin : BaseUnityPlugin
{
    internal static Plugin Instance { get; private set; } = null!;
    internal static ManualLogSource Log { get; private set; } = null!;

    /// <summary>菜单是否处于打开状态（飞行输入会避开菜单，免得打字时人乱飞）。</summary>
    internal static bool MenuOpen { get; private set; }

    /// <summary>打开 / 关闭菜单的快捷键。</summary>
    internal static ConfigEntry<KeyCode> ToggleKey = null!;

    /// <summary>是否允许按 Esc 关闭菜单。</summary>
    internal static ConfigEntry<bool> CloseOnEsc = null!;

    /// <summary>菜单整体缩放。</summary>
    internal static ConfigEntry<float> PanelScale = null!;

    /// <summary>菜单整体不透明度。</summary>
    internal static ConfigEntry<float> PanelOpacity = null!;

    /// <summary>菜单位置偏移（拖动后自动保存）。</summary>
    internal static ConfigEntry<float> PanelOffsetX = null!;

    /// <summary>菜单位置偏移（拖动后自动保存）。</summary>
    internal static ConfigEntry<float> PanelOffsetY = null!;

    /// <summary>是否在屏幕上标记队友。</summary>
    internal static ConfigEntry<bool> ShowTeammateMarkers = null!;

    /// <summary>是否显示坐标 / 延迟。</summary>
    internal static ConfigEntry<bool> ShowHudInfo = null!;

    /// <summary>是否显示附近队友的体力 / 额外体力条。</summary>
    internal static ConfigEntry<bool> ShowStaminaBars = null!;

    /// <summary>队友体力条最多同时显示几条。</summary>
    internal static ConfigEntry<int> TeammateBarLimit = null!;

    /// <summary>队友体力条的显示距离（米）。</summary>
    internal static ConfigEntry<float> TeammateBarProximity = null!;

    /// <summary>队友体力条的缩放。</summary>
    internal static ConfigEntry<float> TeammateBarScale = null!;

    /// <summary>队友体力条后面是否显示他背包里的格子。</summary>
    internal static ConfigEntry<bool> ShowTeammateInventory = null!;

    /// <summary>体力条上是否显示数值。</summary>
    internal static ConfigEntry<bool> ShowStaminaInfo = null!;

    /// <summary>体力条数值的字号。</summary>
    internal static ConfigEntry<float> StaminaInfoFontSize = null!;

    /// <summary>体力条数值的描边宽度。</summary>
    internal static ConfigEntry<float> StaminaInfoOutlineWidth = null!;

    /// <summary>长条（体力 / 额外体力）的数值是否取整。</summary>
    internal static ConfigEntry<bool> RoundStaminaValues = null!;

    /// <summary>状态段的数值是否取整。</summary>
    internal static ConfigEntry<bool> RoundAfflictionValues = null!;

    /// <summary>队友的额外体力数值是否画到条外面。</summary>
    internal static ConfigEntry<bool> ShowTeammateExtraStaminaOutsideBar = null!;

    /// <summary>是否在右上角显示本局地图信息。</summary>
    internal static ConfigEntry<bool> ShowMapInfo = null!;

    /// <summary>是否在屏幕正上方显示游戏内时间。</summary>
    internal static ConfigEntry<bool> ShowClock = null!;

    /// <summary>保命模式的求救键。只在倒下求助 / 保命模式生效期间才会被消费。</summary>
    internal static ConfigEntry<KeyCode> SaveMeKey = null!;

    /// <summary>按住即可打开身上背包的按键。</summary>
    internal static ConfigEntry<KeyCode> BackpackKey = null!;

    /// <summary>
    /// 当前这个背包轮盘是「按住背包键」打开的、正在等玩家松手。
    /// <c>BackpackWheelHoldPatch</c> 靠它决定要不要伪装交互键。
    /// </summary>
    internal static bool BackpackHotkeyHeld { get; private set; }

    private PeakMenuWindow? window;
    private HudOverlay? hud;
    private ItemTooltip? itemTooltip;
    private Harmony? harmony;
    private bool backpackOpen;

    /// <summary>轮盘「标志还挂着但界面没开」已经连续多少帧，见 <see cref="EnsureBackpackWheelNotStuck"/>。</summary>
    private int stuckWheelFrames;

    /// <summary>所有背包都是 4 格。</summary>
    private const int BackpackSlots = 4;

    private void Awake()
    {
        Instance = this;
        Log = Logger;

        ToggleKey = Config.Bind(
            "General",
            "ToggleKey",
            KeyCode.F8,
            "打开 / 关闭菜单的快捷键。取值见 UnityEngine.KeyCode，例如 F8、Insert、Home、End。");

        CloseOnEsc = Config.Bind(
            "General",
            "CloseOnEsc",
            true,
            "菜单打开时，是否允许按 Esc 关闭菜单。");

        PanelScale = Config.Bind(
            "General",
            "PanelScale",
            1.0f,
            new ConfigDescription(
                "菜单整体缩放，1.0 为标准大小。",
                new AcceptableValueRange<float>(0.6f, 1.6f)));

        PanelOpacity = Config.Bind(
            "General",
            "PanelOpacity",
            1.0f,
            new ConfigDescription(
                "菜单整体不透明度，越低越透明。",
                new AcceptableValueRange<float>(0.35f, 1.0f)));

        PanelOffsetX = Config.Bind("General", "PanelOffsetX", 0f, "菜单的横向位置偏移，拖动标题栏后自动写入。");

        PanelOffsetY = Config.Bind("General", "PanelOffsetY", 0f, "菜单的纵向位置偏移，拖动标题栏后自动写入。");

        ShowTeammateMarkers = Config.Bind(
            "Hud",
            "ShowTeammateMarkers",
            true,
            "是否在屏幕上用标签标记队友位置（走散了很有用）。");

        ShowHudInfo = Config.Bind(
            "Hud",
            "ShowHudInfo",
            true,
            "是否在屏幕左上角显示坐标 / 延迟。");

        ShowStaminaBars = Config.Bind(
            "Hud",
            "ShowStaminaBars",
            true,
            "是否把游戏原生的体力条复制几条出来，显示附近队友的体力 / 异常状态 / 额外体力。");

        TeammateBarLimit = Config.Bind(
            "Hud",
            "TeammateBarLimit",
            4,
            new ConfigDescription(
                "队友体力条最多同时显示几条（按距离由近到远取）。",
                new AcceptableValueRange<int>(0, 16)));

        TeammateBarProximity = Config.Bind(
            "Hud",
            "TeammateBarProximity",
            30f,
            new ConfigDescription(
                "队友体力条的显示距离（米），超出这个距离的队友不显示。",
                new AcceptableValueRange<float>(1f, 10000f)));

        TeammateBarScale = Config.Bind(
            "Hud",
            "TeammateBarScale",
            0.72f,
            new ConfigDescription(
                "队友体力条的缩放，1.0 就和自己的体力条一样大。",
                new AcceptableValueRange<float>(0.2f, 1f)));

        ShowTeammateInventory = Config.Bind(
            "Hud",
            "ShowTeammateInventory",
            true,
            "队友体力条后面是否显示他背包 / 手持的格子。");

        ShowStaminaInfo = Config.Bind(
            "Hud",
            "ShowStaminaInfo",
            true,
            "体力条上是否显示数值（自己那条和队友条一起生效）。");

        StaminaInfoFontSize = Config.Bind(
            "Hud",
            "StaminaInfoFontSize",
            20f,
            new ConfigDescription(
                "体力条数值的字号。",
                new AcceptableValueRange<float>(6f, 72f)));

        StaminaInfoOutlineWidth = Config.Bind(
            "Hud",
            "StaminaInfoOutlineWidth",
            0.08f,
            new ConfigDescription(
                "体力条数值的黑色描边宽度，0 就是不描边。",
                new AcceptableValueRange<float>(0f, 1f)));

        RoundStaminaValues = Config.Bind(
            "Hud",
            "RoundStaminaValues",
            true,
            "体力 / 额外体力的数值是否取整（关掉会显示一位小数）。");

        RoundAfflictionValues = Config.Bind(
            "Hud",
            "RoundAfflictionValues",
            false,
            "状态段的数值是否取整（关掉会显示一位小数）。");

        ShowTeammateExtraStaminaOutsideBar = Config.Bind(
            "Hud",
            "ShowTeammateExtraStaminaOutsideBar",
            false,
            "队友的额外体力数值是否画到细条外面（默认画在条里）。");

        ShowMapInfo = Config.Bind(
            "Hud",
            "ShowMapInfo",
            true,
            "是否在右上角显示本局地图（群系）信息。");

        ShowClock = Config.Bind(
            "Hud",
            "ShowClock",
            true,
            "是否在屏幕正上方显示游戏内时间（方便观察天黑天亮）。");

        SaveMeKey = Config.Bind(
            "General",
            "SaveMeKey",
            KeyCode.F,
            "保命模式的求助键。只有自己倒下时、或保命模式已启动时才会响应，其余时候这个键保持游戏原本的功能。");

        BackpackKey = Config.Bind(
            "General",
            "BackpackKey",
            KeyCode.B,
            "按住这个键直接打开身上的背包，松开关闭。");

        ItemFavorites.Initialize(Config);
        ItemTooltipConfig.Bind(Config);

        try
        {
            harmony = new Harmony("PeakModder.PeakMasterMenu");
            harmony.PatchAll();
        }
        catch (Exception e)
        {
            Log.LogError($"应用 Harmony 补丁失败，部分功能不可用: {e}");
        }

        Log.LogInfo($"Plugin {Name} 已加载，按 {ToggleKey.Value} 打开菜单。");
    }

    private void Update()
    {
        try
        {
            if (Input.GetKeyDown(ToggleKey.Value))
            {
                GetWindow().Toggle();
            }
        }
        catch (Exception e)
        {
            Log.LogError($"处理菜单热键时出错: {e}");
        }

        try
        {
            Cheats.Update();
        }
        catch (Exception e)
        {
            Log.LogError($"应用作弊状态时出错: {e}");
        }

        try
        {
            SaveMode.Update();
        }
        catch (Exception e)
        {
            Log.LogError($"处理保命模式时出错: {e}");
        }

        try
        {
            UpdateBackpackHotkey();
        }
        catch (Exception e)
        {
            Log.LogError($"处理背包热键时出错: {e}");
        }

        try
        {
            EnsureBackpackWheelNotStuck();
        }
        catch (Exception e)
        {
            Log.LogError($"检查背包轮盘状态时出错: {e}");
        }

        EnsureHud();
    }

    /// <summary>
    /// 兜底：轮盘早就收起来了，<c>usingBackpackWheel</c> 却还挂着 —— 这就是「视角卡死」的原因。
    ///
    /// 游戏的 <c>GUIManager.OpenBackpackWheel</c> 是「先置位、再 InitWheel」：
    /// 先把 <c>usingBackpackWheel</c> 置真，再调 <c>InitWheel</c>，而 <c>InitWheel</c> 的
    /// <c>SetActive(true)</c> 在最后一行。只要 <c>InitWheel</c> 中途抛异常
    /// （最常见的是背包格子比轮盘切片多，<c>slices[i + 1]</c> 索引越界），
    /// 轮盘 GameObject 就永远不会被激活，<c>BackpackWheel.Update</c> 也就永远不会来关它，
    /// 而 <c>CharacterMovement.CanMoveCamera()</c> 只看 <c>data.usingWheel</c>，
    /// 于是永久返回 false —— 表现就是对着背包按一下 E 之后视角再也动不了。
    ///
    /// 这里把这种残留状态收回去。连着两帧都这样才动手，免得和正常开轮盘的那一帧抢。
    /// </summary>
    private void EnsureBackpackWheelNotStuck()
    {
        var gui = GUIManager.instance;
        var character = Character.localCharacter;

        if (gui == null || character == null || character.data == null || !character.data.usingBackpackWheel)
        {
            stuckWheelFrames = 0;
            return;
        }

        // 轮盘还开着，就是正常状态，别乱动。
        if (gui.backpackWheel != null && gui.backpackWheel.gameObject.activeSelf)
        {
            stuckWheelFrames = 0;
            return;
        }

        if (++stuckWheelFrames < 2)
        {
            return;
        }

        stuckWheelFrames = 0;
        Log.LogWarning("背包轮盘没能打开（InitWheel 中途出错），已复位视角锁定。");
        gui.CloseBackpackWheel();
    }

    /// <summary>
    /// 按住背包键打开、松开关闭。
    /// 用的是游戏自己的 <c>GUIManager.OpenBackpackWheel</c>，
    /// 所以拿取 / 交换 / 丢弃这些交互和原版完全一致。
    /// </summary>
    private void UpdateBackpackHotkey()
    {
        var gui = GUIManager.instance;
        var character = Character.localCharacter;

        if (backpackOpen)
        {
            // 松手、打开菜单、被击倒，或者游戏自己把轮盘收起来了（丢完东西之后）都算结束。
            var stillHolding = Input.GetKey(BackpackKey.Value)
                && !MenuOpen
                && gui != null
                && gui.wheelActive
                && character != null
                && character.data != null
                && !character.data.dead
                && !character.data.fullyPassedOut;

            if (!stillHolding)
            {
                EndBackpackHotkey();
            }

            return;
        }

        if (MenuOpen || character == null)
        {
            return;
        }

        if (Input.GetKeyDown(BackpackKey.Value))
        {
            OpenBackpack();
        }
    }

    private void OpenBackpack()
    {
        var character = Character.localCharacter;
        var gui = GUIManager.instance;
        if (character == null || gui == null || character.data == null)
        {
            return;
        }

        try
        {
            // 1) 背包正拿在手上
            if (character.data.currentItem is Backpack held)
            {
                gui.OpenBackpackWheel(
                    BackpackReference.GetFromBackpackItem(held),
                    BackpackSlots,
                    held.backpackType);
            }
            // 2) 背包背在背上
            else
            {
                var slot = character.player != null ? character.player.backpackSlot : null;
                if (slot == null || slot.IsEmpty())
                {
                    Log.LogInfo("身上没有背包。");
                    return;
                }

                gui.OpenBackpackWheel(
                    BackpackReference.GetFromEquippedBackpack(character),
                    BackpackSlots,
                    slot.backpackType);
            }

            // 以轮盘的真实状态为准：万一有别的窗口挡着没打开，就不要去伪装交互键，
            // 否则会把 E 键的交互抢过来。
            backpackOpen = gui.wheelActive;
            BackpackHotkeyHeld = backpackOpen;
        }
        catch (Exception e)
        {
            Log.LogWarning($"打开背包失败: {e.Message}");

            // OpenBackpackWheel 是先置 usingBackpackWheel 再调 InitWheel 的。
            // InitWheel 中途抛异常时它末尾的 SetActive(true) 不会执行，
            // 轮盘没开、标志却留在那儿，视角就永久锁死了 —— 这里主动收回去。
            gui.CloseBackpackWheel();
        }
    }

    /// <summary>
    /// 松开背包键时**不要**自己去关轮盘。
    /// 只要停止伪装交互键，下一帧 <c>BackpackWheel.Update</c> 就会自己走
    /// <c>Choose()</c> + <c>CloseBackpackWheel()</c>——这正是原版「按住 E 打开、
    /// 鼠标悬停某一格再松手取放」的完整流程。
    /// 如果在这里直接调 CloseBackpackWheel，就会跳过 Choose()，
    /// 轮盘里点什么都放不进背包。
    /// </summary>
    private void EndBackpackHotkey()
    {
        backpackOpen = false;
        BackpackHotkeyHeld = false;
    }

    internal static void SetMenuOpen(bool open)
    {
        MenuOpen = open;
    }

    /// <summary>
    /// HUD 要等真正进入一局游戏后再创建——那时游戏的字体与本地化都已经就绪。
    /// </summary>
    private void EnsureHud()
    {
        if (Character.localCharacter == null)
        {
            return;
        }

        if (hud == null)
        {
            try
            {
                hud = HudOverlay.Create();
                Log.LogInfo("屏幕 HUD 已创建。");
            }
            catch (Exception e)
            {
                Log.LogWarning($"创建屏幕 HUD 失败: {e.Message}");
            }
        }

        if (itemTooltip == null)
        {
            try
            {
                itemTooltip = ItemTooltip.Create();
                Log.LogInfo("物品提示框已创建。");
            }
            catch (Exception e)
            {
                Log.LogWarning($"创建物品提示框失败: {e.Message}");
            }
        }
    }

    /// <summary>把菜单窗口的位置 / 缩放 / 透明度重新应用一遍。</summary>
    internal static void RefreshPanel()
    {
        if (Instance == null)
        {
            return;
        }

        Instance.window?.ApplyPanelSettings();
    }

    private PeakMenuWindow GetWindow()
    {
        if (window == null)
        {
            var go = new GameObject("PeakMasterMenu.Window");
            UnityEngine.Object.DontDestroyOnLoad(go);
            window = go.AddComponent<PeakMenuWindow>();
            Log.LogInfo("菜单窗口已创建。");
        }

        return window;
    }
}
