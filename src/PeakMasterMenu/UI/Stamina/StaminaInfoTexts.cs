using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace PeakMasterMenu.UI.Stamina;

/// <summary>
/// 体力条上的数值。
///
/// 移植自 PeakStatsEx（LengSword，MIT 许可）的 <c>StaminaInfoPatch</c>，
/// 只保留"数值"这一半：主条当前体力、额外体力、每段异常状态的数值。
///
/// 数值本身不是百分比而是"像素宽度 ÷ 每格像素"，所以和条的实际长短永远对得上 ——
/// 条有多长，数字就有多大。每格 <see cref="BarUnitSize"/> 像素。
/// </summary>
internal static class StaminaInfoTexts
{
    private const float LargeBarThreshold = 30f;
    private const float SmallBarThreshold = 15f;
    private const float BarUnitSize = 6f;

    private const float ExtraStaminaTextOffsetX = 6f;
    private const float ExtraStaminaTextWidth = 80f;

    /// <summary>队友条的数值不用每帧刷新，隔一小会儿更新一次就够，省点开销。</summary>
    private const float CharacterRefreshInterval = 0.2f;

    private static readonly Dictionary<string, TextMeshProUGUI> BarTexts = new();
    private static readonly Dictionary<string, float> LastKnownValues = new();
    private static readonly Dictionary<int, float> LastCharacterUpdateTime = new();

    // ---------------------------------------------------------------- 自己的那条

    /// <summary>挂在原生 <c>StaminaBar.Update</c> 后面调用。</summary>
    internal static void UpdateForSelfBar(StaminaBar bar)
    {
        // 克隆出来的队友条上那个 StaminaBar 还没被销毁时也可能走到这里，
        // 只认原生那一条，免得给队友条也糊一层数字。
        if (GUIManager.instance == null || bar != GUIManager.instance.bar)
        {
            return;
        }

        if (!InitializedSelfBar)
        {
            InitializedSelfBar = true;
            InitSelfBar(bar);
        }

        if (!Plugin.ShowStaminaInfo.Value)
        {
            HideSelfBarTexts(bar);
            return;
        }

        UpdateBarText(bar.staminaBar.name, bar.desiredStaminaSize, Plugin.RoundStaminaValues.Value, trimWholeDecimal: false);
        UpdateBarText("ExtraStamina", bar.desiredExtraStaminaSize, Plugin.RoundStaminaValues.Value, trimWholeDecimal: false);

        foreach (var affliction in bar.afflictions)
        {
            UpdateBarText(affliction.name, affliction.size, Plugin.RoundAfflictionValues.Value, trimWholeDecimal: true);
        }
    }

    private static bool InitializedSelfBar;

    private static void InitSelfBar(StaminaBar bar)
    {
        AddTextObject(bar.staminaBar.gameObject, bar.staminaBar.name);
        AddTextObject(bar.extraBarStamina.gameObject, "ExtraStamina");

        foreach (var affliction in bar.afflictions)
        {
            AddTextObject(affliction.gameObject, affliction.name);
        }
    }

    private static void HideSelfBarTexts(StaminaBar bar)
    {
        HideText(bar.staminaBar.name);
        HideText("ExtraStamina");

        foreach (var affliction in bar.afflictions)
        {
            HideText(affliction.name);
        }
    }

    // ---------------------------------------------------------------- 队友那条

    internal static void UpdateForCharacterBar(CharacterStaminaBar bar)
    {
        if (bar.ObservedCharacter == null)
        {
            return;
        }

        var id = bar.GetInstanceID();

        if (!BarTexts.ContainsKey(CharacterStaminaKey(id)))
        {
            InitCharacterBar(bar);
        }

        if (!Plugin.ShowStaminaInfo.Value)
        {
            HideCharacterBarTexts(bar);
            return;
        }

        if (LastCharacterUpdateTime.TryGetValue(id, out var last) && Time.time - last < CharacterRefreshInterval)
        {
            return;
        }

        LastCharacterUpdateTime[id] = Time.time;

        var width = bar.fullBarRectTransform.sizeDelta.x;

        var staminaSize = Mathf.Max(0f, bar.ObservedCharacter.data.currentStamina * width);
        UpdateBarText(CharacterStaminaKey(id), staminaSize, Plugin.RoundStaminaValues.Value, trimWholeDecimal: false);

        var extraSize = Mathf.Max(0f, bar.ObservedCharacter.data.extraStamina * width);
        var outside = Plugin.ShowTeammateExtraStaminaOutsideBar.Value;
        ApplyCharacterExtraStaminaTextLayout(bar, outside);
        UpdateBarText(CharacterExtraStaminaKey(id), extraSize, Plugin.RoundStaminaValues.Value, trimWholeDecimal: false, outside ? "+" : string.Empty);

        foreach (var affliction in bar.characterBarAfflictions)
        {
            affliction.FetchDesiredSize();
            UpdateBarText(CharacterAfflictionKey(id, affliction), affliction.size, Plugin.RoundAfflictionValues.Value, trimWholeDecimal: true);
        }
    }

    private static void InitCharacterBar(CharacterStaminaBar bar)
    {
        var id = bar.GetInstanceID();
        AddTextObject(bar.staminaBarRectTransform.gameObject, CharacterStaminaKey(id));
        AddTextObject(bar.extraBar.gameObject, CharacterExtraStaminaKey(id));
        ApplyCharacterExtraStaminaTextLayout(bar, Plugin.ShowTeammateExtraStaminaOutsideBar.Value);

        foreach (var affliction in bar.characterBarAfflictions)
        {
            AddTextObject(affliction.gameObject, CharacterAfflictionKey(id, affliction));
        }
    }

    private static void HideCharacterBarTexts(CharacterStaminaBar bar)
    {
        var id = bar.GetInstanceID();
        HideText(CharacterStaminaKey(id));
        HideText(CharacterExtraStaminaKey(id));

        foreach (var affliction in bar.characterBarAfflictions)
        {
            HideText(CharacterAfflictionKey(id, affliction));
        }
    }

    /// <summary>体力条被销毁时把它的文字一起清掉，免得字典越攒越多。</summary>
    internal static void Remove(CharacterStaminaBar bar)
    {
        var id = bar.GetInstanceID();
        LastCharacterUpdateTime.Remove(id);

        var keys = new List<string>
        {
            CharacterStaminaKey(id),
            CharacterExtraStaminaKey(id),
        };

        foreach (var affliction in bar.characterBarAfflictions)
        {
            keys.Add(CharacterAfflictionKey(id, affliction));
        }

        foreach (var key in keys)
        {
            BarTexts.Remove(key);
            LastKnownValues.Remove(key);
        }
    }

    private static string CharacterStaminaKey(int id)
    {
        return id + "_Stamina";
    }

    private static string CharacterExtraStaminaKey(int id)
    {
        return id + "_ExtraStamina";
    }

    private static string CharacterAfflictionKey(int id, CharacterBarAffliction affliction)
    {
        return id + "_" + affliction.gameObject.name;
    }

    // ---------------------------------------------------------------- 额外体力数值的位置

    /// <summary>额外体力的数字默认画在细条里面；开了开关就挪到条外面、前面加个"+"。</summary>
    private static void ApplyCharacterExtraStaminaTextLayout(CharacterStaminaBar bar, bool showOutsideBar)
    {
        if (!BarTexts.TryGetValue(CharacterExtraStaminaKey(bar.GetInstanceID()), out var text))
        {
            return;
        }

        if (showOutsideBar)
        {
            if (text.transform.parent != bar.extraBar)
            {
                text.transform.SetParent(bar.extraBar, false);
            }

            text.color = Color.green;
            PositionExtraStaminaTextOutsideBar(text, bar);
        }
        else
        {
            if (text.transform.parent != bar.extraBarStamina)
            {
                text.transform.SetParent(bar.extraBarStamina, false);
            }

            PositionExtraStaminaTextInsideBar(text);
        }
    }

    private static void PositionExtraStaminaTextInsideBar(TextMeshProUGUI text)
    {
        var rect = text.rectTransform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.localRotation = Quaternion.identity;
        rect.localScale = Vector3.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        text.color = Color.black;
        text.alignment = TextAlignmentOptions.Center;
    }

    private static void PositionExtraStaminaTextOutsideBar(TextMeshProUGUI text, CharacterStaminaBar bar)
    {
        var rect = text.rectTransform;
        var outline = bar.extraBarOutline;
        var rightEdge = outline.anchoredPosition.x + outline.sizeDelta.x * (1f - outline.pivot.x);

        rect.anchorMin = new Vector2(0f, 0.5f);
        rect.anchorMax = new Vector2(0f, 0.5f);
        rect.pivot = new Vector2(0f, 0.5f);
        rect.anchoredPosition = new Vector2(rightEdge + ExtraStaminaTextOffsetX, outline.anchoredPosition.y);
        rect.sizeDelta = new Vector2(ExtraStaminaTextWidth, Mathf.Max(outline.sizeDelta.y, 1f));
        rect.localRotation = Quaternion.identity;
        rect.localScale = Vector3.one;
        text.alignment = TextAlignmentOptions.Left;
    }

    // ---------------------------------------------------------------- 通用

    private static void AddTextObject(GameObject host, string key)
    {
        var gui = GUIManager.instance;

        if (gui == null || gui.heroDayText == null || host == null)
        {
            return;
        }

        var go = new GameObject("StaminaInfo");
        go.transform.SetParent(host.transform, false);

        host.SetActive(true);

        var text = go.AddComponent<TextMeshProUGUI>();
        text.font = gui.heroDayText.font;
        text.fontSize = Plugin.StaminaInfoFontSize.Value;

        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.localRotation = Quaternion.identity;
        rect.localScale = host.TryGetComponent<CharacterBarAffliction>(out var affliction) && affliction.isPetrify
            ? new Vector3(1.4285715f, 2f, 1f)
            : Vector3.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        text.alignment = TextAlignmentOptions.Center;
        text.richText = true;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.text = string.Empty;
        text.outlineWidth = Plugin.StaminaInfoOutlineWidth.Value;

        go.SetActive(false);

        BarTexts[key] = text;
        LastKnownValues[key] = 0f;
    }

    private static void UpdateBarText(string key, float size, bool roundLargeValue, bool trimWholeDecimal, string prefix = "", string suffix = "", bool forceUpdate = false)
    {
        if (!BarTexts.TryGetValue(key, out var text) || text == null)
        {
            return;
        }

        var changed = forceUpdate
            || !LastKnownValues.ContainsKey(key)
            || !Mathf.Approximately(LastKnownValues[key], size)
            || !text.gameObject.activeSelf;

        // 太短的段塞不下数字，干脆不显示。
        if (size < SmallBarThreshold)
        {
            text.gameObject.SetActive(false);
            LastKnownValues[key] = size;
            return;
        }

        var value = prefix + FormatBarValue(size, roundLargeValue, trimWholeDecimal);

        if (!string.IsNullOrEmpty(suffix))
        {
            value += suffix;
        }

        if (changed || text.text != value)
        {
            text.text = value;
            text.gameObject.SetActive(true);
            LastKnownValues[key] = size;
        }
    }

    /// <summary>
    /// 像素宽度换算成"格数"。短条取整，长条保留一位小数（可按配置取整 / 去掉 .0）。
    /// </summary>
    private static string FormatBarValue(float size, bool roundLargeValue, bool trimWholeDecimal)
    {
        if (size >= LargeBarThreshold)
        {
            var value = size / BarUnitSize;

            if (roundLargeValue)
            {
                return Mathf.Round(value).ToString();
            }

            var text = value.ToString("F1");
            return trimWholeDecimal ? text.Replace(".0", string.Empty) : text;
        }

        return Mathf.Round(size / BarUnitSize).ToString();
    }

    private static void HideText(string key)
    {
        if (BarTexts.TryGetValue(key, out var text) && text != null)
        {
            text.gameObject.SetActive(false);
        }
    }
}

/// <summary>把数值文字挂到原生体力条上。</summary>
[HarmonyLib.HarmonyPatch(typeof(StaminaBar), "Update")]
internal static class StaminaBarUpdateTextPatch
{
    [HarmonyLib.HarmonyPostfix]
    private static void Postfix(StaminaBar __instance)
    {
        try
        {
            StaminaInfoTexts.UpdateForSelfBar(__instance);
        }
        catch (System.Exception e)
        {
            Plugin.Log.LogError($"刷新体力条数值失败: {e}");
        }
    }
}
