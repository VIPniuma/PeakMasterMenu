using System;
using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PeakMasterMenu.UI.Stamina;

/// <summary>
/// 一条"别人的"体力条。
///
/// 移植自 PeakStatsEx（LengSword，MIT 许可）的 <c>CharacterStaminaBar</c>。
///
/// 做法不是自己画，而是把游戏原生左下角那条体力条整个克隆一份：
/// - 克隆出来的对象里，原生的 <see cref="StaminaBar"/> 被丢掉（见
///   <see cref="TeammateStaminaBars.CreateStaminaBar"/>），换成这个组件；
/// - 于是原生那套布局 / 贴图 / 动画节奏全部保留，只把"看谁"从
///   <c>Character.observedCharacter</c> 改成 <see cref="ObservedCharacter"/>；
/// - 名字换成队友名并染上队友颜色，按配置缩放、下移。
///
/// 这样就和自己那条长得一模一样，不会像自绘的那样"看起来不是原版"。
/// </summary>
internal sealed class CharacterStaminaBar : MonoBehaviour
{
    /// <summary>额外体力那条展开后的高度。</summary>
    private const float ExtraBarExpandedSize = 28f;

    /// <summary>额外体力条相对主条宽度的比例。</summary>
    internal const float ExtraStaminaBarWidthScale = 0.7f;

    /// <summary>石化段的高度比例。</summary>
    internal const float PetrifyBarHeightScale = 0.5f;

    private static readonly Color DefaultBarColor = new Color(0.1035f, 0.2656f, 0.3019f, 1f);
    private static readonly Color OutOfStaminaColor = new Color(0.566f, 0.0089f, 0.0089f, 1f);

    private float minStaminaBarWidth = 20f;

    internal float minAfflictionWidth = 60f;

    internal RectTransform fullBarRectTransform = null!;
    internal RectTransform staminaBarRectTransform = null!;
    internal RectTransform extraBar = null!;
    internal RectTransform extraBarStamina = null!;
    internal RectTransform extraBarOutline = null!;

    private RectTransform maxStaminaBarRectTransform = null!;
    private RectTransform staminaBarOutlineRectTransform = null!;
    private RectTransform staminaBarOutlineOverflowBar = null!;
    private Image barImage = null!;
    private Image glowImage = null!;
    private GameObject shieldIcon = null!;
    private GameObject campfireIcon = null!;
    private float staminaBarOffset;

    internal readonly List<CharacterBarAffliction> characterBarAfflictions = new();

    private CharacterBarAffliction? petrifyAffliction;
    private Coroutine? animateDisableCoroutine;

    private Character? _observedCharacter;
    private bool hadObservedCharacter;
    private bool outOfStamina;
    private bool sequencingExtraBar;
    private bool isEnabled;
    private float sinTime;
    private readonly float tau = Mathf.PI * 2f;
    private float desiredExtraStaminaSize;
    private float extraOutlineMaxWidth;
    private float extraBarDefaultVisualGap;
    private float appliedTeammateVisualYOffset;

    private readonly List<TeammateInventorySlot> inventorySlotUIs = new();
    private readonly List<TeammateInventorySlot> backpackSlotUIs = new();
    private TeammateInventorySlot? heldItemSlot;

    /// <summary>这条在看谁。</summary>
    internal Character? ObservedCharacter
    {
        get => _observedCharacter;
        set
        {
            if (value != null)
            {
                hadObservedCharacter = true;
            }

            _observedCharacter = value;

            if (value != null)
            {
                ApplyTeammateVisualOffset();
            }
        }
    }

    internal void AnimateEnable()
    {
        if (!isEnabled && animateDisableCoroutine == null)
        {
            isEnabled = true;
            gameObject.SetActive(true);
            animateDisableCoroutine = StartCoroutine(EnableRoutine());
        }
    }

    internal void AnimateDisable()
    {
        if (isEnabled && animateDisableCoroutine == null)
        {
            isEnabled = false;
            animateDisableCoroutine = StartCoroutine(DisableRoutine());
        }
    }

    internal void AddCharacterBarAffliction(CharacterBarAffliction affliction)
    {
        characterBarAfflictions.Add(affliction);

        if (affliction.isPetrify)
        {
            petrifyAffliction = affliction;
        }
    }

    private void Awake()
    {
        // 此刻原生 StaminaBar 还在（CreateStaminaBar 里 Destroy 是延迟到帧尾的），
        // 正好用来抄它身上的几个数值和复用它的预制体部件。
        if (TryGetComponent<StaminaBar>(out var original))
        {
            staminaBarOffset = original.staminaBarOffset;
            minStaminaBarWidth = original.minStaminaBarWidth;
            minAfflictionWidth = original.minAfflictionWidth;
            DisableMoraleBoostUi(original);
        }

        CacheStaminaBarReferences();
        CreateInventorySlots();

        if (original != null)
        {
            CreateExtraStaminaBar(original);

            if (original.extraBar != null)
            {
                original.extraBar.gameObject.SetActive(false);
            }
        }
    }

    private void OnDestroy()
    {
        StaminaInfoTexts.Remove(this);
    }

    private void Update()
    {
        if (ObservedCharacter == null || !ObservedCharacter.gameObject.activeSelf)
        {
            // 队友退出 / 死亡后把这条收掉。没看过人就直接不管（克隆出来还没派上用场）。
            if (hadObservedCharacter)
            {
                Destroy(gameObject);
            }

            return;
        }

        ApplyConfiguredScale();
        ApplyTeammateVisualOffset();
        ApplyExtraBarScaleCompensation();
        UpdateStaminaBar();
        UpdateExtraStaminaBar();

        if (Plugin.ShowTeammateInventory.Value)
        {
            UpdateInventorySlots();
        }
        else
        {
            ClearInventorySlots();
        }

        StaminaInfoTexts.UpdateForCharacterBar(this);
    }

    // ---------------------------------------------------------------- 抓引用

    private void CacheStaminaBarReferences()
    {
        fullBarRectTransform = (RectTransform)transform.Find("FullBar");
        maxStaminaBarRectTransform = (RectTransform)transform.Find("LayoutGroup/MaxStamina");
        staminaBarOutlineRectTransform = (RectTransform)transform.Find("OutlineMask/Outline");
        staminaBarOutlineOverflowBar = (RectTransform)transform.Find("OutlineOverflowLine");
        barImage = maxStaminaBarRectTransform.Find("Back").GetComponent<Image>();
        staminaBarRectTransform = (RectTransform)barImage.transform.Find("Stamina");
        glowImage = staminaBarRectTransform.Find("Glow").GetComponent<Image>();
        campfireIcon = staminaBarOutlineRectTransform.Find("CampfireIcon").gameObject;
        shieldIcon = staminaBarOutlineRectTransform.Find("ShieldIcon").gameObject;
    }

    /// <summary>
    /// 士气提升那套动画 / 文字是给自己那条配的，克隆体上要关掉，否则会跟着抖。
    /// </summary>
    private static void DisableMoraleBoostUi(StaminaBar original)
    {
        if (original.moraleBoostAnimator != null)
        {
            original.moraleBoostAnimator.enabled = false;
        }

        if (original.moraleBoostText != null)
        {
            original.moraleBoostText.enabled = false;
            original.moraleBoostText.gameObject.SetActive(false);
        }
    }

    // ---------------------------------------------------------------- 额外体力条

    /// <summary>
    /// 克隆原生"额外体力条"（原生那条只在自己身上是竖着展开的），
    /// 组成队友条下面那一细条。
    /// </summary>
    private void CreateExtraStaminaBar(StaminaBar original)
    {
        if (fullBarRectTransform == null)
        {
            Plugin.Log.LogWarning("移植的体力条：没找到 FullBar，跳过额外体力条。");
            return;
        }

        var holder = new GameObject("FriendExtraBar");
        holder.transform.SetParent(transform, false);

        extraBar = holder.AddComponent<RectTransform>();
        extraBar.anchorMin = new Vector2(0f, 0.5f);
        extraBar.anchorMax = new Vector2(0f, 0.5f);
        extraBar.pivot = new Vector2(0f, 0.5f);
        extraBar.anchoredPosition = new Vector2(
            fullBarRectTransform.anchoredPosition.x,
            fullBarRectTransform.anchoredPosition.y - 31f);
        extraBarDefaultVisualGap = (fullBarRectTransform.anchoredPosition.y - extraBar.anchoredPosition.y) * 0.72f;
        extraBar.sizeDelta = Vector2.one * ExtraBarExpandedSize;

        if (original.extraBarOutline != null)
        {
            var outline = Instantiate(original.extraBarOutline.gameObject, holder.transform, false);
            outline.name = "FriendExtraBarOutline";
            extraBarOutline = outline.GetComponent<RectTransform>();
            var position = extraBarOutline.anchoredPosition;
            extraBarOutline.anchoredPosition = new Vector2(position.x - 46f, position.y - 1f);
            extraOutlineMaxWidth = fullBarRectTransform.sizeDelta.x * ExtraStaminaBarWidthScale + 12f;
            extraBarOutline.sizeDelta = new Vector2(extraOutlineMaxWidth, extraBarOutline.sizeDelta.y);
            RemoveAllScriptsExceptImage(outline);
            RemoveCopiedStaminaInfo(outline);
        }

        if (original.extraBarStamina != null)
        {
            var fill = Instantiate(original.extraBarStamina.gameObject, holder.transform, false);
            fill.name = "FriendExtraBarStamina";
            extraBarStamina = fill.GetComponent<RectTransform>();
            extraBarStamina.SetParent(extraBarOutline, false);
            extraBarStamina.anchorMin = new Vector2(0f, 0.5f);
            extraBarStamina.anchorMax = new Vector2(0f, 0.5f);
            extraBarStamina.pivot = new Vector2(0f, 0.5f);
            extraBarStamina.anchoredPosition = Vector2.zero;
            extraBarStamina.localRotation = Quaternion.identity;
            extraBarStamina.localScale = Vector3.one;
            extraBarStamina.offsetMin = new Vector2(0f, extraBarStamina.offsetMin.y);

            var innerStamina = fill.transform.Find("Stamina");
            if (innerStamina is RectTransform inner)
            {
                inner.anchoredPosition = new Vector2(inner.anchoredPosition.x + 5f, inner.anchoredPosition.y);
                inner.sizeDelta = new Vector2(inner.sizeDelta.x, inner.sizeDelta.y * 0.5f);
            }

            RemoveAllScriptsExceptImage(fill);
            RemoveCopiedStaminaInfo(fill);
        }

        CreateOrMovePetrifyAffliction();
        holder.SetActive(false);
    }

    private void CreateOrMovePetrifyAffliction()
    {
        var gui = GUIManager.instance;

        if (petrifyAffliction == null && gui != null && gui.bar != null && gui.bar.petrifyAffliction != null)
        {
            var template = gui.bar.petrifyAffliction;
            var copy = Instantiate(template.gameObject, extraBarOutline, false);
            copy.name = "FriendPetrifyAffliction";
            RemoveAllScriptsExceptImage(copy);
            petrifyAffliction = copy.AddComponent<CharacterBarAffliction>();
            petrifyAffliction.afflictionType = template.afflictionType;
            petrifyAffliction.isPetrify = true;
            petrifyAffliction.FetchReferences();
        }

        if (petrifyAffliction != null)
        {
            var rect = (RectTransform)petrifyAffliction.transform;
            rect.SetParent(extraBarOutline, false);
            rect.anchorMin = new Vector2(1f, 0.5f);
            rect.anchorMax = new Vector2(1f, 0.5f);
            rect.pivot = new Vector2(1f, 0.5f);
            rect.anchoredPosition = new Vector2(-8f, 0f);
            rect.localRotation = Quaternion.identity;
            rect.localScale = new Vector3(0.7f, PetrifyBarHeightScale, 1f);
            rect.SetAsLastSibling();
        }
    }

    private void UpdateExtraStaminaBar()
    {
        if (extraBar == null || extraBarStamina == null || extraBarOutline == null || ObservedCharacter == null)
        {
            return;
        }

        var extraStamina = ObservedCharacter.data.extraStamina;
        var petrified = petrifyAffliction != null && ObservedCharacter.data.petrifyAmount > 1f;
        var shouldShow = extraStamina > 0f | petrified;

        if (!extraBar.gameObject.activeSelf & shouldShow)
        {
            extraBar.sizeDelta = Vector2.zero;
            extraBar.DOKill();
            extraBar.DOSizeDelta(Vector2.one * ExtraBarExpandedSize, 0.25f).SetEase(Ease.OutCubic);
            extraBar.gameObject.SetActive(true);
        }

        if (!extraBar.gameObject.activeSelf)
        {
            return;
        }

        desiredExtraStaminaSize = Mathf.Max(0f, extraStamina * fullBarRectTransform.sizeDelta.x * ExtraStaminaBarWidthScale);
        extraBarStamina.sizeDelta = new Vector2(
            Mathf.Lerp(extraBarStamina.sizeDelta.x, Mathf.Max(4f, desiredExtraStaminaSize), Time.deltaTime * 10f),
            extraBarStamina.sizeDelta.y);
        extraBarStamina.gameObject.SetActive(extraBarStamina.sizeDelta.x > 6.1f);
        extraBarOutline.sizeDelta = new Vector2(extraOutlineMaxWidth, extraBarOutline.sizeDelta.y);

        if (!shouldShow && !sequencingExtraBar)
        {
            sequencingExtraBar = true;
            DOTween.Sequence()
                .Append(extraBar.DOSizeDelta(new Vector2(extraBar.sizeDelta.x, 0f), 0.2f))
                .OnComplete(() =>
                {
                    extraBar.gameObject.SetActive(false);
                    sequencingExtraBar = false;
                });
        }
    }

    /// <summary>克隆体上只保留 Image，其余脚本（含原生 BarAffliction / 各种逻辑）全部干掉。</summary>
    private static void RemoveAllScriptsExceptImage(GameObject target)
    {
        foreach (var behaviour in target.GetComponents<MonoBehaviour>())
        {
            if (behaviour is not Image)
            {
                DestroyImmediate(behaviour);
            }
        }
    }

    /// <summary>干掉克隆出来的 StaminaInfo 文字（那是我们自己后加的数字，不要跟着复制）。</summary>
    private static void RemoveCopiedStaminaInfo(GameObject target)
    {
        foreach (var child in target.GetComponentsInChildren<Transform>(true))
        {
            if (child != target.transform && child.name == "StaminaInfo")
            {
                DestroyImmediate(child.gameObject);
            }
        }
    }

    // ---------------------------------------------------------------- 缩放 / 位移

    private static float GetTeammateVisualYOffset()
    {
        return 10f + InterpolateScaleSetting(10f, -1f, -21f);
    }

    private void ApplyTeammateVisualOffset()
    {
        var offset = GetTeammateVisualYOffset();
        var delta = offset - appliedTeammateVisualYOffset;

        if (Mathf.Abs(delta) < 0.001f)
        {
            return;
        }

        appliedTeammateVisualYOffset = offset;

        for (var i = 0; i < transform.childCount; i++)
        {
            if (transform.GetChild(i) is RectTransform rect)
            {
                rect.anchoredPosition += new Vector2(0f, delta);
            }
        }
    }

    private void ApplyConfiguredScale()
    {
        if (!isEnabled || animateDisableCoroutine != null)
        {
            return;
        }

        var scale = GetConfiguredScale();

        if (Mathf.Abs(transform.localScale.x - scale) >= 0.001f)
        {
            transform.DOKill();
            transform.localScale = Vector3.one * scale;
        }
    }

    /// <summary>额外体力条是绝对定位的，缩放后要把间距按比例补偿回去，否则会叠在主条上。</summary>
    private void ApplyExtraBarScaleCompensation()
    {
        if (extraBar == null || fullBarRectTransform == null || extraBarDefaultVisualGap <= 0f)
        {
            return;
        }

        var gap = Mathf.Max(0f, extraBarDefaultVisualGap + InterpolateScaleSetting(-3f, 0f, 10f));
        var compensated = gap / Mathf.Max(0.001f, GetConfiguredScale());
        extraBar.anchoredPosition = new Vector2(extraBar.anchoredPosition.x, fullBarRectTransform.anchoredPosition.y - compensated);
    }

    internal static float GetConfiguredScale()
    {
        return Mathf.Clamp(Plugin.TeammateBarScale.Value, 0.2f, 1f);
    }

    /// <summary>多条队友条之间的间距，跟着缩放走。</summary>
    internal static float GetGroupSpacing()
    {
        return Mathf.Max(0f, InterpolateScaleSetting(26.8f, 38f, 65.8f));
    }

    /// <summary>在 0.6 / 0.72 / 1.0 三个采样点上做二次插值，跟原模组一样的曲线。</summary>
    private static float InterpolateScaleSetting(float atLow, float atDefault, float atHigh)
    {
        var scale = GetConfiguredScale();
        var low = (scale - 0.72f) * (scale - 1f) / 0.048f;
        var mid = (scale - 0.6f) * (scale - 1f) / -0.0336f;
        var high = (scale - 0.6f) * (scale - 0.72f) / 0.11199998f;
        return atLow * low + atDefault * mid + atHigh * high;
    }

    // ---------------------------------------------------------------- 每帧刷新

    private void UpdateStaminaBar()
    {
        var character = ObservedCharacter;

        if (character == null)
        {
            return;
        }

        foreach (var affliction in characterBarAfflictions)
        {
            affliction.FetchDesiredSize();
            affliction.UpdateVisual();
        }

        var desiredStamina = Mathf.Max(0f, character.data.currentStamina * fullBarRectTransform.sizeDelta.x + staminaBarOffset);

        if (character.data.currentStamina <= 0.005f && !outOfStamina)
        {
            outOfStamina = true;
            OutOfStaminaPulse();
        }
        else if (character.data.currentStamina > 0.005f)
        {
            outOfStamina = false;
        }

        var step = Time.deltaTime * 10f;
        staminaBarRectTransform.sizeDelta = new Vector2(
            Mathf.Lerp(staminaBarRectTransform.sizeDelta.x, desiredStamina, step),
            staminaBarRectTransform.sizeDelta.y);

        var overflow = Mathf.Clamp01((staminaBarRectTransform.sizeDelta.x - desiredStamina) * 0.5f);
        sinTime += step * overflow;
        var glow = glowImage.color;
        glow.a = overflow * 0.4f - Mathf.Abs(Mathf.Sin(sinTime)) * 0.2f;
        glowImage.color = glow;

        var desiredMax = Mathf.Max(0f, character.GetMaxStamina() * fullBarRectTransform.sizeDelta.x + staminaBarOffset);
        maxStaminaBarRectTransform.sizeDelta = new Vector2(
            Mathf.Lerp(maxStaminaBarRectTransform.sizeDelta.x, desiredMax, step),
            maxStaminaBarRectTransform.sizeDelta.y);

        var statusSum = character.refs.afflictions.statusSum;
        staminaBarOutlineRectTransform.sizeDelta = new Vector2(
            14f + Mathf.Max(1f, statusSum) * fullBarRectTransform.sizeDelta.x,
            staminaBarOutlineRectTransform.sizeDelta.y);
        staminaBarOutlineOverflowBar.gameObject.SetActive(statusSum > 1.005f);
        staminaBarRectTransform.gameObject.SetActive(staminaBarRectTransform.sizeDelta.x > minStaminaBarWidth);
        maxStaminaBarRectTransform.gameObject.SetActive(maxStaminaBarRectTransform.sizeDelta.x > minStaminaBarWidth);

        if (sinTime > tau)
        {
            sinTime -= tau;
        }

        shieldIcon.SetActive(character.data.isInvincible);
        campfireIcon.SetActive(!character.refs.afflictions.canGetHungry);
    }

    private void OutOfStaminaPulse()
    {
        barImage.color = OutOfStaminaColor;
        barImage.DOColor(DefaultBarColor, 0.5f);
    }

    private IEnumerator EnableRoutine()
    {
        transform.localScale = Vector3.zero;
        transform.DOScale(GetConfiguredScale(), 0.5f).SetEase(Ease.OutElastic);
        yield return new WaitForSeconds(0.5f);
        animateDisableCoroutine = null;
    }

    private IEnumerator DisableRoutine()
    {
        transform.localScale = Vector3.one * GetConfiguredScale();
        transform.DOScale(0f, 0.5f).SetEase(Ease.InExpo);
        yield return new WaitForSeconds(0.5f);
        gameObject.SetActive(false);
        animateDisableCoroutine = null;
    }

    // ---------------------------------------------------------------- 队友背包格

    private void CreateInventorySlots()
    {
        var gui = GUIManager.instance;

        if (gui == null || gui.items == null || gui.items.Length == 0)
        {
            return;
        }

        var sprite = gui.items[0].selectedSlotIcon.sprite;
        var color = gui.items[0].selectedSlotIcon.color;
        var label = FindNicknameLabel();
        var offset = GetNicknameLabelWidth(label) + 184f;

        for (var i = 0; i < 3; i++)
        {
            inventorySlotUIs.Add(CreateInventorySlot($"InvSlot_{i}", new Vector2(32f, 32f), new Vector2(offset + i * 38f, 43f), sprite, color));
        }

        heldItemSlot = CreateInventorySlot("HoldingItemSlot", new Vector2(38f, 38f), new Vector2(offset + 114f, 43f), sprite, color);

        for (var i = 0; i < 4; i++)
        {
            backpackSlotUIs.Add(CreateInventorySlot($"BackpackSlot_{i}", new Vector2(32f, 32f), new Vector2(offset + 170f + i * 38f, 43f), sprite, color));
        }
    }

    private void UpdateInventorySlots()
    {
        var character = ObservedCharacter;

        if (character?.player?.itemSlots == null)
        {
            ClearInventorySlots();
            return;
        }

        var slots = character.player.itemSlots;
        var selected = character.refs.items.currentSelectedSlot;

        for (var i = 0; i < inventorySlotUIs.Count && i < slots.Length; i++)
        {
            inventorySlotUIs[i].SetItem(slots[i], selected.IsSome && selected.Value == i);
        }

        var currentItem = character.data.currentItem;

        if (heldItemSlot != null)
        {
            heldItemSlot.SetItem(new ItemSlot(byte.MaxValue) { prefab = currentItem }, true);
        }

        if (!character.player.backpackSlot.IsEmpty())
        {
            if (character.player.backpackSlot.data.TryGetDataEntry<BackpackData>(DataEntryKey.BackpackData, out var backpack))
            {
                var backpackSlots = backpack.itemSlots;

                for (var i = 0; i < backpackSlotUIs.Count && i < backpackSlots.Length; i++)
                {
                    backpackSlotUIs[i].SetItem(backpackSlots[i], false);
                }
            }

            return;
        }

        foreach (var slot in backpackSlotUIs)
        {
            slot.Clear();
        }
    }

    private void ClearInventorySlots()
    {
        foreach (var slot in inventorySlotUIs)
        {
            slot.Clear();
        }

        heldItemSlot?.Clear();
    }

    private TeammateInventorySlot CreateInventorySlot(string name, Vector2 size, Vector2 position, Sprite borderSprite, Color borderColor)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(transform, false);

        var rect = go.GetComponent<RectTransform>();
        rect.sizeDelta = size;
        rect.anchorMin = new Vector2(0f, 0.5f);
        rect.anchorMax = new Vector2(0f, 0.5f);
        rect.pivot = new Vector2(0f, 0.5f);
        rect.anchoredPosition = position;

        var slot = go.AddComponent<TeammateInventorySlot>();
        slot.Initialize(borderSprite, borderColor);
        return slot;
    }

    private Transform? FindNicknameLabel()
    {
        foreach (Transform child in transform)
        {
            if (child.name.StartsWith("CharacterName"))
            {
                return child;
            }
        }

        return null;
    }

    private static float GetNicknameLabelWidth(Transform? label)
    {
        return label == null ? 100f : label.GetComponent<RectTransform>().sizeDelta.x;
    }
}
