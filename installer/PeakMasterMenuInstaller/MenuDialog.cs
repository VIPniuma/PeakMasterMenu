using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace PeakMasterMenu.Installer
{
    /// <summary>
    /// 和安装器同风格的对话框，用来替代系统 MessageBox。
    ///
    /// 替换的理由很实际：系统弹窗是浅色 Windows 10 风格，突然从一片暖褐色界面里弹出一个
    /// 白底方框，割裂感很强。这里的方法签名刻意和 MessageBox.Show 保持一致，
    /// 因此调用处只需要把类名换掉。
    /// </summary>
    internal sealed class MenuDialog : Form
    {
        private const float DialogWidth = 460f;
        private const float TitleHeight = 46f;
        private const float IconSize = 42f;
        private const float PadLeft = 24f;
        private const float PadRight = 24f;
        private const float GapIconText = 18f;
        private const float PadContentTop = 22f;
        private const float PadContentBottom = 26f;
        private const float ButtonAreaHeight = 74f;
        private const float ButtonWidth = 112f;
        private const float ButtonHeight = 42f;
        private const float ButtonGap = 10f;

        private MessageBoxIcon _icon = MessageBoxIcon.None;
        private DialogResult _closeResult = DialogResult.Cancel;

        private MenuDialog()
        {
        }

        public static DialogResult Show(
            IWin32Window owner,
            string text,
            string caption,
            MessageBoxButtons buttons,
            MessageBoxIcon icon)
        {
            return Show(owner, text, caption, buttons, icon, MessageBoxDefaultButton.Button1);
        }

        public static DialogResult Show(
            IWin32Window owner,
            string text,
            string caption,
            MessageBoxButtons buttons,
            MessageBoxIcon icon,
            MessageBoxDefaultButton defaultButton)
        {
            using (var dialog = new MenuDialog())
            {
                dialog.Build(text ?? string.Empty, caption ?? string.Empty, buttons, icon, defaultButton);

                return owner != null ? dialog.ShowDialog(owner) : dialog.ShowDialog();
            }
        }

        /// <summary>快捷生成按当前 DPI 缩放的坐标和尺寸。</summary>
        private Point P(float x, float y) => new Point((int)Theme.Px(this, x), (int)Theme.Px(this, y));
        private Size S(float w, float h) => new Size((int)Theme.Px(this, w), (int)Theme.Px(this, h));

        /// <summary>无边框窗口本来是没有投影的，加上这个才不至于「贴」在桌面上。</summary>
        protected override CreateParams CreateParams
        {
            get
            {
                const int CsDropShadow = 0x00020000;
                var parameters = base.CreateParams;
                parameters.ClassStyle |= CsDropShadow;
                return parameters;
            }
        }

        private void Build(
            string text,
            string caption,
            MessageBoxButtons buttons,
            MessageBoxIcon icon,
            MessageBoxDefaultButton defaultButton)
        {
            SuspendLayout();

            Text = caption.Length > 0 ? caption : "PeakMasterMenu";
            _icon = icon;
            Font = Theme.Ui(this, 9f);
            BackColor = Theme.WindowBg;
            ForeColor = Theme.TextMain;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            AutoScaleMode = AutoScaleMode.None;
            KeyPreview = true;
            DoubleBuffered = true;

            var textWidth = (int)Theme.Px(this, DialogWidth - PadLeft - IconSize - GapIconText - PadRight);
            var iconHeight = (int)Theme.Px(this, IconSize);

            // 用 Label 来承载消息文本，它自带换行和 DPI 缩放，比自己算 TextRenderer 省事很多
            var messageLabel = new Label
            {
                Text = text,
                Font = Theme.Ui(this, 9.5f),
                ForeColor = Theme.TextMain,
                BackColor = Color.Transparent,
                AutoSize = true,
                MaximumSize = new Size(textWidth, 0),
                Location = P(PadLeft + IconSize + GapIconText, TitleHeight + PadContentTop),
            };

            Controls.Add(messageLabel);
            messageLabel.Refresh();

            var contentHeight = Math.Max(iconHeight, messageLabel.Height);
            var height = TitleHeight + PadContentTop + PadContentBottom + ButtonAreaHeight
                + (contentHeight / Theme.Scale(this));

            ClientSize = S(DialogWidth, height);

            var titleBar = new MenuTitleBar
            {
                Caption = caption.Length > 0 ? caption : "PeakMasterMenu",
                Subtitle = string.Empty,
                ShowLogo = false,
                Location = P(0, 0),
                Size = S(DialogWidth, TitleHeight),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            };

            titleBar.CloseRequested += (sender, e) => DialogResult = _closeResult;
            Controls.Add(titleBar);

            BuildButtons(buttons, defaultButton, contentHeight);

            ResumeLayout(false);
        }

        private void BuildButtons(MessageBoxButtons buttons, MessageBoxDefaultButton defaultButton, int contentHeight)
        {
            var specs = ResolveButtons(buttons);
            var defaultIndex = ResolveDefaultIndex(defaultButton, specs.Count);

            // 取消/否这类「退出」按钮的返回结果：只有确定按钮时，Esc 和右上角的叉也应该当作确定
            _closeResult = specs.Count == 1 ? specs[0].Result : DialogResult.Cancel;

            var scale = Theme.Scale(this);
            var designTop = (TitleHeight + PadContentTop)
                + (contentHeight / scale)
                + PadContentBottom
                + ((ButtonAreaHeight - ButtonHeight) / 2f);

            var left = DialogWidth - PadRight;

            // 从右往左摆，最后一个（最右边的）是主按钮
            for (var i = specs.Count - 1; i >= 0; i--)
            {
                var spec = specs[i];
                var isDefault = i == defaultIndex;

                var button = new MenuButton
                {
                    Text = spec.Text,
                    Style = isDefault ? MenuButtonStyle.Primary : MenuButtonStyle.Secondary,
                    Size = S(ButtonWidth, ButtonHeight),
                    Location = P(left - ButtonWidth, designTop),
                    Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
                };

                var result = spec.Result;
                button.Click += (sender, e) => DialogResult = result;

                Controls.Add(button);

                if (isDefault)
                {
                    ActiveControl = button;
                }

                left -= ButtonWidth + ButtonGap;
            }
        }

        private static List<ButtonSpec> ResolveButtons(MessageBoxButtons buttons)
        {
            switch (buttons)
            {
                case MessageBoxButtons.OKCancel:
                    return new List<ButtonSpec>
                    {
                        new ButtonSpec("确定", DialogResult.OK),
                        new ButtonSpec("取消", DialogResult.Cancel),
                    };

                case MessageBoxButtons.YesNo:
                    return new List<ButtonSpec>
                    {
                        new ButtonSpec("是", DialogResult.Yes),
                        new ButtonSpec("否", DialogResult.No),
                    };

                case MessageBoxButtons.YesNoCancel:
                    return new List<ButtonSpec>
                    {
                        new ButtonSpec("是", DialogResult.Yes),
                        new ButtonSpec("否", DialogResult.No),
                        new ButtonSpec("取消", DialogResult.Cancel),
                    };

                default:
                    return new List<ButtonSpec>
                    {
                        new ButtonSpec("确定", DialogResult.OK),
                    };
            }
        }

        private static int ResolveDefaultIndex(MessageBoxDefaultButton defaultButton, int count)
        {
            var index = 0;

            switch (defaultButton)
            {
                case MessageBoxDefaultButton.Button2:
                    index = 1;
                    break;

                case MessageBoxDefaultButton.Button3:
                    index = 2;
                    break;
            }

            if (index >= count)
            {
                index = count - 1;
            }

            return index;
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape)
            {
                DialogResult = _closeResult;
                e.Handled = true;
            }

            base.OnKeyDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var graphics = e.Graphics;
            Theme.Setup(graphics);

            var scale = Theme.Px(this, 1f);

            using (var brush = new SolidBrush(Theme.WindowBg))
            {
                graphics.FillRectangle(brush, ClientRectangle);
            }

            var top = (TitleHeight + PadContentTop) * scale;
            var iconBounds = new RectangleF(PadLeft * scale, top, IconSize * scale, IconSize * scale);

            DrawIcon(graphics, iconBounds);

            // 按钮区和内容区之间压一条分隔线
            var separatorY = Height - (ButtonAreaHeight * scale);
            using (var pen = new Pen(Theme.CardBorder, scale))
            {
                graphics.DrawLine(
                    pen,
                    PadLeft * scale,
                    separatorY,
                    Width - (PadRight * scale),
                    separatorY);
            }
        }

        private void DrawIcon(Graphics graphics, RectangleF bounds)
        {
            if (_icon == MessageBoxIcon.None)
            {
                return;
            }

            var color = _icon == MessageBoxIcon.Error ? Theme.Danger : Theme.Brand;

            using (var brush = new SolidBrush(color))
            {
                graphics.FillEllipse(brush, bounds);
            }

            var glyph = "i";
            if (_icon == MessageBoxIcon.Question)
            {
                glyph = "?";
            }
            else if (_icon == MessageBoxIcon.Error)
            {
                glyph = "!";
            }

            using (var font = Theme.UiBold(this, 17f))
            {
                // 「i」的点会让视觉重心偏上，往下挪一点点看起来才是居中的
                var offset = glyph == "i" ? 2f : 0f;
                var rect = new RectangleF(bounds.X, bounds.Y + offset, bounds.Width, bounds.Height);

                TextRenderer.DrawText(
                    graphics,
                    glyph,
                    font,
                    Rectangle.Round(rect),
                    Theme.TextOnBrand,
                    Theme.TextOptions | TextFormatFlags.HorizontalCenter |
                    TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            }
        }

        private readonly struct ButtonSpec
        {
            public ButtonSpec(string text, DialogResult result)
            {
                Text = text;
                Result = result;
            }

            public string Text { get; }

            public DialogResult Result { get; }
        }
    }
}
