using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PeakMasterMenu.UI;

/// <summary>
/// 滚轮减速器：一格滚轮只走固定的一小段，再叠一层平滑收尾。
///
/// 不用 <see cref="ScrollRect"/> 自带的滚轮，是因为它是「灵敏度 × 滚轮增量」直接乘出来的，
/// 一格能滚掉大半屏，页面一长就根本没法细看。这里改成「一格 = 固定距离」，滚起来是一小步一小步地走。
///
/// 位置不自己算，而是把收敛好的增量再转发回 <see cref="ScrollRect.OnScroll"/>：
/// 方向（哪头是上）、边界（滚到头该不该停）、内容锚点怎么换算，全部交给它自己的实现，
/// 省得在这里重算一遍坐标再踩一次「方向反了 / 范围反了」的坑。
/// 前提是 <c>UiFactory.ScrollView</c> 里把 <c>scrollSensitivity</c> 留成 1 —— 转发过去的量会被它再乘一次，设 0 就全被乘没了。
///
/// 挂在 Viewport 上：滚轮事件是从鼠标下的控件沿父级往上找第一个处理者的，
/// 放在这里就会先被本组件接住，于是 <see cref="ScrollRect"/> 自己那套滚轮处理不会再触发。
/// </summary>
internal sealed class WheelScroll : MonoBehaviour, IScrollHandler
{
    /// <summary>一格滚轮滚动的距离（UI 单位，1080p 参考分辨率下约等于像素）。</summary>
    private const float StepDistance = 60f;

    /// <summary>平滑收尾时间（秒），越小越跟手。</summary>
    private const float SmoothTime = 0.07f;

    internal ScrollRect Target = null!;

    /// <summary>攒下来还没滚完的距离。</summary>
    private float pending;

    /// <summary>转发用的假事件，复用同一个实例免得每帧产生垃圾。</summary>
    private PointerEventData? forwarded;

    public void OnScroll(PointerEventData eventData)
    {
        if (Target == null)
        {
            return;
        }

        var raw = eventData.scrollDelta.y;
        if (Mathf.Approximately(raw, 0f))
        {
            return;
        }

        // 只取方向，不取数值 —— 不同设备一格滚轮的增量能差十几倍（0.1 / 1 / 3 都见过），
        // 按数值滚就会出现「有的设备飞快、有的纹丝不动」。方向也沿用原事件的符号，
        // 不去假设「正数是上还是下」。
        pending += Mathf.Sign(raw) * StepDistance;

        Target.StopMovement();
    }

    private void LateUpdate()
    {
        if (Target == null)
        {
            return;
        }

        // 收尾：剩下的不足一格就直接走完，免得留个尾巴一直抖。
        if (Mathf.Abs(pending) < 0.5f)
        {
            if (!Mathf.Approximately(pending, 0f))
            {
                Deliver(pending);
                pending = 0f;
            }

            return;
        }

        // 指数靠近：每一帧走掉剩余距离的一部分，滚轮停下之后自己收住。
        var step = pending * Mathf.Min(1f, Time.unscaledDeltaTime / SmoothTime);
        pending -= step;
        Deliver(step);
    }

    private void Deliver(float delta)
    {
        forwarded ??= new PointerEventData(EventSystem.current);
        forwarded.scrollDelta = new Vector2(0f, delta);
        Target.OnScroll(forwarded);
    }
}
