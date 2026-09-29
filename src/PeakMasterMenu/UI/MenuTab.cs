using UnityEngine;

namespace PeakMasterMenu.UI;

/// <summary>
/// 菜单中的一个页签。每个页签把自己的控件构建到窗口提供的滚动内容区里。
/// </summary>
internal abstract class MenuTab
{
    /// <summary>页签显示名。</summary>
    internal abstract string Title { get; }

    /// <summary>本页签内容的父节点（窗口注入）。</summary>
    protected RectTransform Content = null!;

    /// <summary>由窗口调用：绑定父节点并构建界面。</summary>
    internal void Attach(RectTransform content)
    {
        Content = content;
        Build();
    }

    /// <summary>
    /// 把本页签的控件构建到指定父节点里。
    ///
    /// 给「合并页签」用：几个相近的页签合成一个之后，被合并的那些不再单独占一个页签，
    /// 而是由合并后的页签在自己的 <see cref="Build"/> 里依次调用本方法，
    /// 把它们首尾接在同一个滚动区里。
    /// </summary>
    internal void BuildInto(RectTransform parent)
    {
        Attach(parent);
    }

    /// <summary>构建界面，只在第一次打开时执行一次。</summary>
    protected abstract void Build();

    /// <summary>页签被切换到前台时调用。</summary>
    internal virtual void OnShown()
    {
    }

    /// <summary>页签处于前台时每帧调用，用于刷新实时数据。</summary>
    internal virtual void Tick()
    {
    }

    /// <summary>本地玩家是否可用。</summary>
    protected static bool HasLocalCharacter => Character.localCharacter != null;
}
