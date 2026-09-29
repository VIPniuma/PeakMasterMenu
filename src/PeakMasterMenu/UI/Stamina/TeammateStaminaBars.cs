using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace PeakMasterMenu.UI.Stamina;

/// <summary>
/// 队友体力条的大脑：负责克隆、按距离排序显示 / 收起。
///
/// 移植自 PeakStatsEx（LengSword，MIT 许可）的 <c>ProximityStaminaManager</c>。
/// 挂在"自己那个 Character"身上，随角色一起创建 / 销毁。
/// </summary>
internal sealed class TeammateStaminaBars : MonoBehaviour
{
    private readonly Dictionary<Character, CharacterStaminaBar> staminaBars = new();

    private bool warned;

    private void Update()
    {
        try
        {
            UpdateBarGroupLayout();
            UpdateBars();
        }
        catch (Exception e)
        {
            if (!warned)
            {
                warned = true;
                Plugin.Log.LogWarning($"刷新队友体力条出错: {e}");
            }
        }
    }

    private void UpdateBars()
    {
        // 字典里可能留着已经销毁的角色（他退出了），顺手清掉，免得残留一条永远不收的条。
        PruneDestroyedEntries();

        if (Character.localCharacter == null || !Plugin.ShowStaminaBars.Value)
        {
            foreach (var bar in staminaBars.Values)
            {
                bar.AnimateDisable();
            }

            return;
        }

        var limit = Plugin.TeammateBarLimit.Value;
        var proximity = Plugin.TeammateBarProximity.Value;

        // 观战的时候以被观战的人为中心，跟原模组一致。
        var reference = MainCameraMovement.IsSpectating && MainCameraMovement.specCharacter != null
            ? MainCameraMovement.specCharacter
            : Character.localCharacter;

        var center = reference.Center;
        var candidates = new List<(float Distance, CharacterStaminaBar Bar)>();

        foreach (var character in Character.AllCharacters)
        {
            if (!character.isActiveAndEnabled || character.IsLocal)
            {
                continue;
            }

            if (!staminaBars.TryGetValue(character, out var bar) || bar == null)
            {
                bar = CreateStaminaBar(character);
                staminaBars[character] = bar;
            }

            candidates.Add((Vector3.Distance(center, character.Center), bar));
        }

        var ordered = candidates.OrderBy(candidate => candidate.Distance).ToList();

        for (var i = 0; i < ordered.Count; i++)
        {
            if (i < limit && ordered[i].Distance < proximity)
            {
                ordered[i].Bar.AnimateEnable();
            }
            else
            {
                ordered[i].Bar.AnimateDisable();
            }
        }
    }

    private void PruneDestroyedEntries()
    {
        if (staminaBars.Count == 0)
        {
            return;
        }

        List<Character>? dead = null;

        foreach (var pair in staminaBars)
        {
            if (pair.Key != null && pair.Value != null)
            {
                continue;
            }

            dead ??= new List<Character>();
            dead.Add(pair.Key!);
        }

        if (dead == null)
        {
            return;
        }

        foreach (var character in dead)
        {
            staminaBars.Remove(character);
        }
    }

    /// <summary>
    /// 克隆原生那条体力条，改造成"看某个队友"的版本。
    /// </summary>
    private static CharacterStaminaBar CreateStaminaBar(Character observedCharacter)
    {
        var template = GUIManager.instance.bar.transform;
        var clone = Instantiate(template, template.parent);

        // 原生那几个数字节点是给自己那条算的，队友条上我们自己重建。
        foreach (var child in clone.GetComponentsInChildren<Transform>(true))
        {
            if (child.name == "StaminaInfo")
            {
                Destroy(child.gameObject);
            }
        }

        clone.SetAsFirstSibling();

        // Destroy 会延迟到帧尾，所以 CharacterStaminaBar.Awake 里还能读到这个组件的字段。
        Destroy(clone.GetComponent<StaminaBar>());

        var bar = clone.gameObject.AddComponent<CharacterStaminaBar>();
        bar.ObservedCharacter = observedCharacter;

        foreach (var affliction in clone.GetComponentsInChildren<BarAffliction>(true))
        {
            var converted = affliction.gameObject.AddComponent<CharacterBarAffliction>();
            converted.afflictionType = affliction.afflictionType;
            converted.isPetrify = affliction.isPetrify;
            converted.FetchReferences();
            Destroy(affliction);
        }

        AddCharacterName(clone, observedCharacter);

        bar.AnimateEnable();
        return bar;
    }

    /// <summary>把原生那条"高度 / 关卡名"文字复制一份下来改成队友名，并染上队友颜色。</summary>
    private static void AddCharacterName(Transform parent, Character character)
    {
        var template = GUIManager.instance.GetComponentInChildren<AscentUI>();
        if (template == null)
        {
            return;
        }

        var nameRect = (RectTransform)Instantiate(template.transform, parent);
        var ascent = nameRect.GetComponent<AscentUI>();
        var text = ascent.text;
        Destroy(ascent);

        var characterName = Regex.Replace(character.characterName, "(<color=#\\w{6}>|</color>)", "");
        characterName = characterName.TrimToByteLength(18);

        text.text = characterName;
        nameRect.name = "CharacterName: " + characterName;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Ellipsis;
        text.alignment = TextAlignmentOptions.Left;
        text.fontSize = 32f;
        text.autoSizeTextContainer = false;
        text.rectTransform.sizeDelta = new Vector2(260f, text.rectTransform.sizeDelta.y);
        text.color = character.refs.customization.PlayerColor;
        text.outlineColor = new Color32(0, 0, 0, byte.MaxValue);
        text.outlineWidth = 0.055f;

        nameRect.anchorMin = new Vector2(0f, 1f);
        nameRect.anchorMax = new Vector2(0f, 1f);
        nameRect.pivot = Vector2.zero;
        nameRect.anchoredPosition = new Vector2(0f, 3f);
    }

    /// <summary>多条体力条之间的间距要跟着缩放走，交给原生那套纵向布局去做。</summary>
    private static void UpdateBarGroupLayout()
    {
        var gui = GUIManager.instance;

        if (gui == null || gui.bar == null)
        {
            return;
        }

        var parent = gui.bar.transform.parent;

        if (parent.TryGetComponent<VerticalLayoutGroup>(out var group))
        {
            var spacing = CharacterStaminaBar.GetGroupSpacing();

            if (!Mathf.Approximately(group.spacing, spacing))
            {
                group.spacing = spacing;
                LayoutRebuilder.MarkLayoutForRebuild((RectTransform)parent);
            }
        }
    }
}

/// <summary>
/// 原生体力条容器默认只放得下一条，这里撑大给队友条留位置。
/// </summary>
[HarmonyPatch(typeof(GUIManager), "Start")]
internal static class GuiManagerStaminaBarPatch
{
    [HarmonyPostfix]
    private static void Postfix(GUIManager __instance)
    {
        try
        {
            var container = (RectTransform)__instance.bar.transform.parent;
            container.sizeDelta = new Vector2(600f, 1200f);

            var position = container.anchoredPosition;
            position.y = 660f;
            container.anchoredPosition = position;

            if (container.TryGetComponent<VerticalLayoutGroup>(out var group))
            {
                group.spacing = CharacterStaminaBar.GetGroupSpacing();
            }
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"调整体力条容器失败: {e.Message}");
        }
    }
}

/// <summary>
/// 自己进局时挂上管理器。机场（Airport）里不显示，跟原模组一致。
/// </summary>
[HarmonyPatch(typeof(PlayerHandler), "RegisterCharacter")]
internal static class PlayerHandlerStaminaBarsPatch
{
    [HarmonyPostfix]
    private static void Postfix(Character character)
    {
        try
        {
            if (character == null || character.photonView == null || !character.photonView.IsMine)
            {
                return;
            }

            if (SceneManager.GetActiveScene().name == "Airport")
            {
                return;
            }

            if (character.GetComponent<TeammateStaminaBars>() == null)
            {
                character.gameObject.AddComponent<TeammateStaminaBars>();
            }
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"创建队友体力条管理器失败: {e.Message}");
        }
    }
}
