using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace PeakMasterMenu.Installer
{
    /// <summary>
    /// 安装器的视觉主题：配色、字体和圆角绘制。
    ///
    /// 配色不是随手挑的 —— 数值直接对齐游戏内菜单那一套
    /// （见 src/PeakMasterMenu/UI/UiFactory.cs 里的 PanelBg / ButtonBg / TabActiveBg），
    /// 暖褐色底 + 橙黄主色 + 米白文字，这样「安装器」和「进游戏按 F8 打开的那个菜单」
    /// 看起来才是同一个东西。
    ///
    /// 关于尺寸：界面按 96 DPI 设计，实际绘制时统一用 <see cref="Px"/> 把「设计像素」
    /// 换算成当前 DPI 的像素。窗口本身走 WinForms 的 AutoScaleMode.Font 自动缩放，
    /// 两者的缩放比例是一致的（都跟随系统 DPI），所以不会各缩各的。
    /// </summary>
    internal static class Theme
    {
        // ------------------------------------------------------------------ 配色

        /// <summary>窗口底色，比卡片更深，让卡片浮起来。</summary>
        internal static readonly Color WindowBg = Color.FromArgb(13, 12, 10);

        /// <summary>卡片底色。</summary>
        internal static readonly Color CardBg = Color.FromArgb(30, 27, 23);

        /// <summary>卡片描边，和游戏里的按钮底色同色，保证层次和谐。</summary>
        internal static readonly Color CardBorder = Color.FromArgb(54, 49, 41);

        /// <summary>标题栏底色。</summary>
        internal static readonly Color HeaderBg = Color.FromArgb(40, 35, 29);

        /// <summary>普通控件（按钮、开关行）的底色。</summary>
        internal static readonly Color RowBg = Color.FromArgb(54, 49, 41);
        internal static readonly Color RowHover = Color.FromArgb(74, 67, 56);
        internal static readonly Color RowPressed = Color.FromArgb(44, 40, 34);

        /// <summary>主色：游戏里的高亮橙。</summary>
        internal static readonly Color Brand = Color.FromArgb(230, 163, 50);
        internal static readonly Color BrandHover = Color.FromArgb(246, 182, 76);
        internal static readonly Color BrandPressed = Color.FromArgb(198, 137, 34);
        internal static readonly Color BrandDisabled = Color.FromArgb(92, 75, 44);

        /// <summary>开关的「开」/「关」底色，和游戏内 Switch 控件一致。</summary>
        internal static readonly Color OnBg = Color.FromArgb(73, 139, 69);
        internal static readonly Color OffBg = Color.FromArgb(68, 63, 57);

        internal static readonly Color Success = Color.FromArgb(126, 178, 104);
        internal static readonly Color Danger = Color.FromArgb(216, 98, 78);

        internal static readonly Color TextMain = Color.FromArgb(242, 236, 218);
        internal static readonly Color TextMuted = Color.FromArgb(173, 164, 148);

        /// <summary>橙底上的文字色（深色），保证对比度。</summary>
        internal static readonly Color TextOnBrand = Color.FromArgb(26, 22, 17);

        /// <summary>日志/输入框比卡片更深，像一块凹进去的纸。</summary>
        internal static readonly Color LogBg = Color.FromArgb(14, 13, 11);
        internal static readonly Color LogText = Color.FromArgb(200, 192, 172);

        // ------------------------------------------------------------------ 字体

        /// <summary>
        /// WinForms 的 AutoScaleMode.Font 用 Segoe UI 9pt 作基准（对应 7x15 的 AutoScaleDimensions），
        /// 这个必须和 MainForm 里声明的 AutoScaleDimensions 匹配，否则窗口会被缩放两次。
        /// </summary>
        internal const string ScaleBaseFamily = "Segoe UI";
        internal const float ScaleBaseSize = 9f;

        private static readonly string UiFamily = PickUiFamily();
        private static readonly string MonoFamily = PickMonoFamily();

        /// <summary>界面上实际用的字体：优先「微软雅黑」，中文比 Segoe UI 好看得多。</summary>
        private static string PickUiFamily()
        {
            string[] preferred = { "Microsoft YaHei UI", "Microsoft YaHei", "Segoe UI", "Tahoma" };
            return PickFirstAvailable(preferred, FontFamily.GenericSansSerif.Name);
        }

        private static string PickMonoFamily()
        {
            string[] preferred = { "Consolas", "Courier New" };
            return PickFirstAvailable(preferred, FontFamily.GenericMonospace.Name);
        }

        private static string PickFirstAvailable(string[] preferred, string fallback)
        {
            try
            {
                var installed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                using (var collection = new InstalledFontCollection())
                {
                    foreach (var family in collection.Families)
                    {
                        installed.Add(family.Name);
                    }
                }

                foreach (var name in preferred)
                {
                    if (installed.Contains(name))
                    {
                        return name;
                    }
                }
            }
            catch (Exception)
            {
                // 取不到字体列表就用默认的，不值得为此让安装器起不来
            }

            return fallback;
        }

        /// <summary>当前控件所在显示器的 DPI 相对 96 的倍数。</summary>
        internal static float Scale(Control control)
        {
            return control.DeviceDpi / 96f;
        }

        /// <summary>把设计像素换算成当前 DPI 的实际像素。</summary>
        internal static float Px(Control control, float value)
        {
            return value * Scale(control);
        }

        internal static Font Ui(Control control, float size)
        {
            return new Font(UiFamily, size * Scale(control), FontStyle.Regular, GraphicsUnit.Point);
        }

        internal static Font UiBold(Control control, float size)
        {
            return new Font(UiFamily, size * Scale(control), FontStyle.Bold, GraphicsUnit.Point);
        }

        internal static Font Mono(Control control, float size)
        {
            return new Font(MonoFamily, size * Scale(control), FontStyle.Regular, GraphicsUnit.Point);
        }

        // ------------------------------------------------------------------ 绘制

        /// <summary>统一的绘制环境设置：抗锯齿 + 清晰的文字渲染。</summary>
        internal static void Setup(Graphics graphics)
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        }

        /// <summary>圆角矩形路径。九宫格拉伸那套在 WinForms 里用不上，直接画就行。</summary>
        internal static GraphicsPath Rounded(RectangleF bounds, float radius)
        {
            var path = new GraphicsPath();

            if (bounds.Width <= 0f || bounds.Height <= 0f)
            {
                return path;
            }

            var diameter = Math.Min(radius * 2f, Math.Min(bounds.Width, bounds.Height));
            if (diameter <= 0.5f)
            {
                path.AddRectangle(bounds);
                return path;
            }

            path.AddArc(bounds.X, bounds.Y, diameter, diameter, 180f, 90f);
            path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270f, 90f);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0f, 90f);
            path.AddArc(bounds.X, bounds.Bottom - diameter, diameter, diameter, 90f, 90f);
            path.CloseFigure();
            return path;
        }

        /// <summary>画一块圆角底，可选描边。</summary>
        internal static void FillRounded(Graphics graphics, RectangleF bounds, float radius, Color fill, Color border, float borderWidth)
        {
            using (var path = Rounded(bounds, radius))
            {
                if (fill.A > 0)
                {
                    using (var brush = new SolidBrush(fill))
                    {
                        graphics.FillPath(brush, path);
                    }
                }

                if (border.A > 0 && borderWidth > 0f)
                {
                    using (var pen = new Pen(border, borderWidth))
                    {
                        graphics.DrawPath(pen, path);
                    }
                }
            }
        }

        /// <summary>把两种颜色按比例混合，用于生成「禁用态」这类过渡色。</summary>
        internal static Color Blend(Color from, Color to, float amount)
        {
            var t = Math.Max(0f, Math.Min(1f, amount));
            return Color.FromArgb(
                from.A,
                (int)(from.R + ((to.R - from.R) * t)),
                (int)(from.G + ((to.G - from.G) * t)),
                (int)(from.B + ((to.B - from.B) * t)));
        }

        /// <summary>绘制文字的统一选项：不额外加内边距，避免中文上下被裁。</summary>
        internal const TextFormatFlags TextOptions =
            TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix;
    }
}
