using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

namespace PeakMasterMenu.UI.Stamina;

/// <summary>
/// 队友体力条后面跟着的那几个小格子（随身三格 + 手持 + 背包四格）。
///
/// 移植自 PeakStatsEx（LengSword，MIT 许可）的 <c>InventorySlotUI</c>。
/// </summary>
internal sealed class TeammateInventorySlot : MonoBehaviour
{
    /// <summary>1×1 白色贴图，用来画燃料条底色。</summary>
    private static Sprite? whiteSprite;

    private RectTransform rectTransform = null!;
    private RawImage icon = null!;
    private Image fill = null!;
    private GameObject? fuelBar;
    private Image? fuelBarFill;

    private Vector2 startingSizeDelta;
    private Item? currentItem;
    private ItemInstanceData? currentData;

    internal void Initialize(Sprite selectedSprite, Color selectedColor)
    {
        rectTransform = GetComponent<RectTransform>();
        startingSizeDelta = rectTransform.sizeDelta;

        CreateFuelBar();

        icon = gameObject.AddComponent<RawImage>();
        icon.color = new Color(1f, 1f, 1f, 0f);
        icon.raycastTarget = false;

        fill = CreateFillImage("Fill", selectedSprite, selectedColor);
        fill.enabled = false;
    }

    internal void SetItem(ItemSlot slot, bool selected)
    {
        if (!slot.IsEmpty() && slot.prefab != null && slot.prefab.UIData != null)
        {
            currentItem = slot.prefab;
            currentData = slot.data;

            icon.texture = currentItem.UIData.GetIcon();
            icon.enabled = true;
            icon.color = Color.white;

            UpdateCookedColor();
            UpdateFuelBar();
        }
        else
        {
            Clear();
        }

        SetSelected(selected);
    }

    internal void Clear()
    {
        if (fill != null)
        {
            fill.enabled = false;
        }

        if (icon != null)
        {
            icon.enabled = false;
            icon.texture = null;
            icon.color = new Color(1f, 1f, 1f, 0f);
        }

        currentItem = null;
        currentData = null;

        if (fuelBar != null)
        {
            fuelBar.SetActive(false);
        }
    }

    private void SetSelected(bool isSelected)
    {
        if (currentItem != null && fill != null)
        {
            if (isSelected)
            {
                rectTransform.DOKill();
                rectTransform.DOSizeDelta(startingSizeDelta * 1.2f, 0.5f).SetEase(Ease.OutElastic);
                fill.enabled = true;
                fill.transform.localScale = Vector3.zero;
                fill.transform.DOKill();
                fill.transform.DOScale(1f, 0.25f).SetEase(Ease.OutCubic);
            }
            else
            {
                rectTransform.DOKill();
                rectTransform.DOSizeDelta(startingSizeDelta, 0.2f).SetEase(Ease.OutCubic);
                fill.enabled = false;
            }

            return;
        }

        if (isSelected)
        {
            // 空格子被选中时抖一下，提示"现在拿的是手"。
            rectTransform.DOKill();
            DOTween.Sequence()
                .Append(rectTransform.DOSizeDelta(startingSizeDelta * 1.2f, 0.075f).SetEase(Ease.OutCubic))
                .Append(rectTransform.DOSizeDelta(startingSizeDelta, 0.125f).SetEase(Ease.InSine));
        }
        else
        {
            rectTransform.DOKill();
            rectTransform.sizeDelta = startingSizeDelta;
        }
    }

    private void UpdateCookedColor()
    {
        if (currentData != null
            && currentData.TryGetDataEntry<IntItemData>(DataEntryKey.CookedAmount, out var cooked))
        {
            icon.color = ItemCooking.GetCookColor(cooked.Value);
        }
    }

    private void UpdateFuelBar()
    {
        if (fuelBar == null)
        {
            return;
        }

        // 燃料条只在看"自己"的时候才有意义，队友身上不显示（跟原模组一致）。
        if (Character.observedCharacter != Character.localCharacter)
        {
            fuelBar.SetActive(false);
            return;
        }

        if (currentData != null
            && currentData.TryGetDataEntry<FloatItemData>(DataEntryKey.UseRemainingPercentage, out var fuel))
        {
            fuelBar.SetActive(true);

            if (fuelBarFill != null)
            {
                fuelBarFill.fillAmount = fuel.Value;
            }
        }
        else
        {
            fuelBar.SetActive(false);

            if (fuelBarFill != null)
            {
                fuelBarFill.fillAmount = 1f;
            }
        }
    }

    private Image CreateFillImage(string name, Sprite sprite, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(transform, false);

        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.sizeDelta = Vector2.zero;
        rect.anchoredPosition = Vector2.zero;

        var image = go.AddComponent<Image>();
        image.sprite = sprite;
        image.type = Image.Type.Sliced;
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    private void CreateFuelBar()
    {
        var sprite = GetWhiteSprite();

        var bar = new GameObject("FuelBar", typeof(RectTransform));
        bar.transform.SetParent(transform, false);
        bar.transform.SetAsFirstSibling();

        var barRect = bar.GetComponent<RectTransform>();
        barRect.anchorMin = new Vector2(0f, 0f);
        barRect.anchorMax = new Vector2(1f, 0f);
        barRect.sizeDelta = new Vector2(0f, 4f);
        barRect.anchoredPosition = new Vector2(0f, 2f);

        var background = bar.AddComponent<Image>();
        background.color = new Color(0f, 0f, 0f, 0.5f);
        background.sprite = sprite;
        background.type = Image.Type.Sliced;

        var fillGo = new GameObject("Fill", typeof(RectTransform));
        fillGo.transform.SetParent(bar.transform, false);

        var fillRect = fillGo.GetComponent<RectTransform>();
        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = Vector2.one;
        fillRect.sizeDelta = Vector2.zero;
        fillRect.anchoredPosition = Vector2.zero;

        fuelBarFill = fillGo.AddComponent<Image>();
        fuelBarFill.color = Color.yellow;
        fuelBarFill.sprite = sprite;
        fuelBarFill.type = Image.Type.Filled;
        fuelBarFill.fillMethod = Image.FillMethod.Horizontal;
        fuelBarFill.fillOrigin = 0;

        fuelBar = bar;
        fuelBar.SetActive(false);
    }

    private static Sprite GetWhiteSprite()
    {
        if (whiteSprite != null)
        {
            return whiteSprite;
        }

        var texture = new Texture2D(1, 1);
        texture.SetPixel(0, 0, Color.white);
        texture.Apply();
        whiteSprite = Sprite.Create(texture, new Rect(0f, 0f, 1f, 1f), Vector2.one * 0.5f);
        return whiteSprite;
    }

    private void OnDisable()
    {
        if (rectTransform != null)
        {
            rectTransform.DOKill();
        }

        if (fill != null)
        {
            fill.transform.DOKill();
            fill.enabled = false;
        }

        if (rectTransform != null)
        {
            rectTransform.sizeDelta = startingSizeDelta;
        }
    }
}
