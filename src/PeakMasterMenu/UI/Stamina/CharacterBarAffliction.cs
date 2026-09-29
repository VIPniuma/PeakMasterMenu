using UnityEngine;

namespace PeakMasterMenu.UI.Stamina;

/// <summary>
/// 体力条上的一段异常状态（烫伤 / 中毒 / 饥饿……）。
///
/// 移植自 PeakStatsEx（LengSword，MIT 许可）的 <c>CharacterBarAffliction</c>。
/// 为什么不直接用游戏原生的 <see cref="BarAffliction"/>：它取值写死在
/// <c>Character.observedCharacter</c> 上（也就是你自己），而队友那几条要比的是别人，
/// 所以这里换成读 <see cref="CharacterStaminaBar.ObservedCharacter"/> 的版本。
/// </summary>
internal sealed class CharacterBarAffliction : MonoBehaviour
{
    /// <summary>这一段代表的异常状态类型。</summary>
    internal CharacterAfflictions.STATUSTYPE afflictionType;

    /// <summary>是不是石化段（石化不走 afflictions，走 <c>data.petrifyAmount</c>）。</summary>
    internal bool isPetrify;

    /// <summary>这一段的目标宽度（像素）。由 <see cref="FetchDesiredSize"/> 算出，<see cref="UpdateVisual"/> 平滑逼近。</summary>
    internal float size;

    private RectTransform rectTransform = null!;
    private CharacterStaminaBar characterStaminaBar = null!;

    /// <summary>克隆完先调一次：抓引用、藏掉原生图标、把自己登记到所属的体力条上。</summary>
    internal void FetchReferences()
    {
        rectTransform = (RectTransform)transform;

        // 原生的图标是给"你自己那条"配的，队友条上不要。
        var icon = transform.Find("Icon");
        if (icon != null)
        {
            icon.gameObject.SetActive(false);
        }

        characterStaminaBar = GetComponentInParent<CharacterStaminaBar>();
        characterStaminaBar.AddCharacterBarAffliction(this);
    }

    internal void FetchDesiredSize()
    {
        var character = characterStaminaBar.ObservedCharacter;
        if (character == null)
        {
            return;
        }

        var amount = isPetrify
            ? character.data.petrifyAmount / 100f
            : character.refs.afflictions.GetCurrentStatus(afflictionType);

        size = characterStaminaBar.fullBarRectTransform.sizeDelta.x * amount;

        if (amount > 0.01f)
        {
            // 太细的状态段看不出来，给个最小宽度。
            if (size < characterStaminaBar.minAfflictionWidth)
            {
                size = characterStaminaBar.minAfflictionWidth;
            }

            gameObject.SetActive(true);
        }
        else
        {
            gameObject.SetActive(false);
        }
    }

    internal void UpdateVisual()
    {
        var sizeDelta = rectTransform.sizeDelta;
        sizeDelta.x = Mathf.Lerp(rectTransform.sizeDelta.x, size, Mathf.Min(Time.deltaTime * 10f, 0.1f));
        rectTransform.sizeDelta = sizeDelta;
    }
}
