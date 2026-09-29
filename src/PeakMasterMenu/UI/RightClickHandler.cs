using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;

namespace PeakMasterMenu.UI;

/// <summary>
/// 只监听鼠标右键点击的组件。
/// UGUI 的 Button 默认只认左键，右键需要单独接管。
/// </summary>
internal sealed class RightClickHandler : MonoBehaviour, IPointerClickHandler
{
    internal UnityAction? Handler;

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Right)
        {
            Handler?.Invoke();
        }
    }
}
