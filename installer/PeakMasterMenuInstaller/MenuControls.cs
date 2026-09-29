using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace PeakMasterMenu.Installer
{
    /// <summary>
    /// 卡片：一块圆角深色面板，左上角是「橙色小竖条 + 标题」，可选右上角说明文字。
    ///
    /// 子控件的位置全部由调用方手工指定 —— 这个项目控件不多，绝对坐标比布局引擎更好读，
    /// 出问题时对着坐标一眼就能看出是哪儿错位了。
    /// </summary>
    internal sealed class MenuCard : Panel
    {
        private string _caption = string.Empty;
        private string _hint = string.Empty;

        public MenuCard()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.UserPaint |
                ControlStyles.ResizeRedraw,
                true);

            // 圆角以外的四个角会露出这个底色，所以要和窗口底色一致
            BackColor = Theme.WindowBg;
            Font = Theme.Ui(this, 9f);
            ForeColor = Theme.TextMain;
        }

        /// <summary>卡片标题，画在左上角。留空则不画标题。</summary>
        public string Caption
        {
            get => _caption;
            set
            {
                _caption = value ?? string.Empty;
                Invalidate();
            }
        }

        /// <summary>右上角的补充说明（小字）。</summary>
        public string Hint
        {
            get => _hint;
            set
            {
                _hint = value ?? string.Empty;
                Invalidate();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var graphics = e.Graphics;
            Theme.Setup(graphics);

            var scale = Theme.Px(this, 1f);
            var bounds = new RectangleF(
                0.5f * scale,
                0.5f * scale,
                Width - scale,
                Height - scale);

            Theme.FillRounded(graphics, bounds, 12f * scale, Theme.CardBg, Theme.CardBorder, scale);

            DrawCaption(graphics, scale);
        }

        private void DrawCaption(Graphics graphics, float scale)
        {
            if (_caption.Length == 0)
            {
                return;
            }

            var left = 18f * scale;
            var top = 15f * scale;

            // 橙色小竖条，取代「标题前加个圆点」的常见做法，更像游戏里的分组标题
            using (var brush = new SolidBrush(Theme.Brand))
            {
                graphics.FillRectangle(brush, left, top + (2f * scale), 3f * scale, 13f * scale);
            }

            using (var font = Theme.UiBold(this, 9.5f))
            {
                TextRenderer.DrawText(
                    graphics,
                    _caption,
                    font,
                    new Point((int)(left + (12f * scale)), (int)top),
                    Theme.TextMain,
                    Theme.TextOptions);
            }

            if (_hint.Length == 0)
            {
                return;
            }

            using (var font = Theme.Ui(this, 8.25f))
            {
                var size = TextRenderer.MeasureText(
                    graphics,
                    _hint,
                    font,
                    new Size(int.MaxValue, int.MaxValue),
                    Theme.TextOptions);

                var x = Width - (18f * scale) - size.Width;
                TextRenderer.DrawText(
                    graphics,
                    _hint,
                    font,
                    new Point((int)x, (int)top + (int)(2f * scale)),
                    Theme.TextMuted,
                    Theme.TextOptions);
            }
        }
    }

    internal enum MenuButtonStyle
    {
        /// <summary>橙底深字，一屏只该有一个。</summary>
        Primary,

        /// <summary>深灰底米白字。</summary>
        Secondary,

        /// <summary>深灰底 + 红字红边，用于不可逆的操作。</summary>
        Danger,
    }

    /// <summary>
    /// 自绘按钮。
    ///
    /// 没有用 WinForms 的 Button：FlatStyle.Flat 在深色主题下会有莫名其妙的浅色描边，
    /// 而且圆角、悬停过渡都得靠自绘才能和游戏 UI 对上。
    /// </summary>
    internal sealed class MenuButton : Control, IButtonControl
    {
        private bool _hovered;
        private bool _pressed;
        private bool _isDefault;

        public MenuButton()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.UserPaint |
                ControlStyles.ResizeRedraw |
                ControlStyles.Selectable,
                true);

            Style = MenuButtonStyle.Secondary;
            Font = Theme.UiBold(this, 10.5f);
            Cursor = Cursors.Hand;
            TabStop = true;
            Size = new Size(140, 44);
        }

        public MenuButtonStyle Style { get; set; }

        /// <summary>实现 IButtonControl 的成员，这样它能被 Form 当成 AcceptButton（回车触发）。</summary>
        public DialogResult DialogResult { get; set; }

        /// <summary>供键盘（Enter/空格）和外部代码触发点击。</summary>
        public void PerformClick()
        {
            if (!Enabled)
            {
                return;
            }

            OnClick(EventArgs.Empty);
        }

        public void NotifyDefault(bool value)
        {
            if (_isDefault == value)
            {
                return;
            }

            _isDefault = value;
            Invalidate();
        }

        protected override void OnTextChanged(EventArgs e)
        {
            base.OnTextChanged(e);
            Invalidate();
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            _hovered = false;
            _pressed = false;
            Invalidate();
            base.OnEnabledChanged(e);
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            _hovered = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hovered = false;
            _pressed = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                _pressed = true;
                Focus();
                Invalidate();
            }

            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            _pressed = false;
            Invalidate();
            base.OnMouseUp(e);
        }

        protected override bool IsInputKey(Keys keyData)
        {
            if (keyData == Keys.Enter || keyData == Keys.Space)
            {
                return true;
            }

            return base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space)
            {
                PerformClick();
                e.Handled = true;
            }

            base.OnKeyDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var graphics = e.Graphics;
            Theme.Setup(graphics);

            var scale = Theme.Px(this, 1f);
            var bounds = new RectangleF(
                0.5f * scale,
                0.5f * scale,
                Width - scale,
                Height - scale);

            ResolveColors(out var back, out var fore, out var border, out var borderWidth);

            Theme.FillRounded(graphics, bounds, 10f * scale, back, border, borderWidth);

            TextRenderer.DrawText(
                graphics,
                Text,
                Font,
                Rectangle.Round(bounds),
                fore,
                Theme.TextOptions | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
        }

        private void ResolveColors(out Color back, out Color fore, out Color border, out float borderWidth)
        {
            var scale = Theme.Px(this, 1f);
            border = Color.Transparent;
            borderWidth = 0f;

            if (!Enabled)
            {
                if (Style == MenuButtonStyle.Primary)
                {
                    back = Theme.BrandDisabled;
                    fore = Theme.Blend(Theme.TextOnBrand, Theme.BrandDisabled, 0.5f);
                }
                else if (Style == MenuButtonStyle.Danger)
                {
                    back = Theme.RowBg;
                    fore = Theme.TextMuted;
                    border = Theme.CardBorder;
                    borderWidth = 1.6f * scale;
                }
                else
                {
                    back = Theme.RowBg;
                    fore = Theme.TextMuted;
                }

                return;
            }

            switch (Style)
            {
                case MenuButtonStyle.Primary:
                    back = _pressed ? Theme.BrandPressed : (_hovered ? Theme.BrandHover : Theme.Brand);
                    fore = Theme.TextOnBrand;
                    return;

                case MenuButtonStyle.Danger:
                    back = _pressed ? Theme.RowPressed : (_hovered ? Theme.RowHover : Theme.RowBg);
                    fore = Theme.Danger;
                    border = Theme.Danger;
                    borderWidth = 1.6f * scale;
                    return;

                default:
                    back = _pressed ? Theme.RowPressed : (_hovered ? Theme.RowHover : Theme.RowBg);
                    fore = Theme.TextMain;

                    // 次要按钮当默认按钮时，靠一圈橙色描边来说明「回车就是它」
                    if (_isDefault)
                    {
                        border = Theme.Brand;
                        borderWidth = 1.6f * scale;
                    }

                    return;
            }
        }
    }

    /// <summary>
    /// 开关行：左边是名称，右边是一个胶囊形的「开 / 关」。
    ///
    /// 刻意做成整行可点 —— 游戏里的 Switch 控件就是这样，几十像素高的胶囊很难瞄，
    /// 整行点得中才好用。胶囊左边还能放一段状态文字（比如"已安装 1.0.0"），
    /// 这样一行里就把「装不装」和「装了什么」都说清楚了。
    /// </summary>
    internal sealed class MenuToggle : Control
    {
        private bool _checked = true;
        private bool _hovered;
        private string _status = string.Empty;
        private Color _statusColor = Theme.TextMuted;

        public MenuToggle()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.UserPaint |
                ControlStyles.ResizeRedraw |
                ControlStyles.Selectable,
                true);

            Font = Theme.Ui(this, 9.5f);
            Cursor = Cursors.Hand;
            TabStop = true;
            Size = new Size(600, 46);
        }

        public event EventHandler CheckedChanged;

        public bool Checked
        {
            get => _checked;
            set
            {
                if (_checked == value)
                {
                    return;
                }

                _checked = value;
                Invalidate();
                CheckedChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        /// <summary>胶囊左侧的状态文字，例如「已安装 1.0.0」。</summary>
        public string Status
        {
            get => _status;
            set
            {
                _status = value ?? string.Empty;
                Invalidate();
            }
        }

        public Color StatusColor
        {
            get => _statusColor;
            set
            {
                _statusColor = value;
                Invalidate();
            }
        }

        protected override void OnTextChanged(EventArgs e)
        {
            base.OnTextChanged(e);
            Invalidate();
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            _hovered = false;
            Invalidate();
            base.OnEnabledChanged(e);
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            _hovered = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hovered = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (Enabled && e.Button == MouseButtons.Left)
            {
                Focus();
                Checked = !Checked;
            }

            base.OnMouseDown(e);
        }

        protected override bool IsInputKey(Keys keyData)
        {
            if (keyData == Keys.Space)
            {
                return true;
            }

            return base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (Enabled && e.KeyCode == Keys.Space)
            {
                Checked = !Checked;
                e.Handled = true;
            }

            base.OnKeyDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var graphics = e.Graphics;
            Theme.Setup(graphics);

            var scale = Theme.Px(this, 1f);
            var bounds = new RectangleF(
                0.5f * scale,
                0.5f * scale,
                Width - scale,
                Height - scale);

            var back = !Enabled
                ? Theme.CardBg
                : (_hovered ? Theme.RowHover : Theme.RowBg);

            Theme.FillRounded(graphics, bounds, 9f * scale, back, Color.Transparent, 0f);

            var chipWidth = 96f * scale;
            var chipHeight = 30f * scale;
            var chipLeft = Width - chipWidth - (14f * scale);
            var chipTop = (Height - chipHeight) / 2f;
            var chip = new RectangleF(chipLeft, chipTop, chipWidth, chipHeight);

            var statusWidth = 250f * scale;
            var statusRight = chipLeft - (12f * scale);
            var labelRight = statusRight - statusWidth - (10f * scale);

            var labelColor = Enabled ? Theme.TextMain : Theme.TextMuted;
            TextRenderer.DrawText(
                graphics,
                Text,
                Font,
                Rectangle.Round(new RectangleF(18f * scale, 0f, labelRight - (18f * scale), Height)),
                labelColor,
                Theme.TextOptions | TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
                TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);

            if (_status.Length > 0)
            {
                TextRenderer.DrawText(
                    graphics,
                    _status,
                    Theme.Ui(this, 8.5f),
                    Rectangle.Round(new RectangleF(statusRight - statusWidth, 0f, statusWidth, Height)),
                    Enabled ? _statusColor : Theme.TextMuted,
                    Theme.TextOptions | TextFormatFlags.Right | TextFormatFlags.VerticalCenter |
                    TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
            }

            Theme.FillRounded(
                graphics,
                chip,
                chipHeight / 2f,
                _checked ? Theme.OnBg : Theme.OffBg,
                Color.Transparent,
                0f);

            TextRenderer.DrawText(
                graphics,
                _checked ? "开" : "关",
                Theme.UiBold(this, 9.5f),
                Rectangle.Round(chip),
                _checked ? Theme.TextMain : Theme.TextMuted,
                Theme.TextOptions | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                TextFormatFlags.SingleLine);
        }
    }

    /// <summary>
    /// 自绘进度条。
    ///
    /// 属性刻意和 WinForms 的 ProgressBar 保持一致（Style / Value / Maximum / MarqueeAnimationSpeed），
    /// 这样原来那段进度控制代码一个字都不用改。
    /// </summary>
    internal sealed class MenuProgressBar : Control
    {
        private readonly Timer _timer;
        private int _minimum;
        private int _maximum = 100;
        private int _value;
        private ProgressBarStyle _style = ProgressBarStyle.Continuous;
        private int _marqueeSpeed = 25;
        private float _marqueeOffset;

        public MenuProgressBar()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.UserPaint |
                ControlStyles.ResizeRedraw,
                true);

            Size = new Size(400, 10);

            _timer = new Timer { Interval = 33 };
            _timer.Tick += OnMarqueeTick;
        }

        public int Minimum
        {
            get => _minimum;
            set
            {
                _minimum = value;
                Invalidate();
            }
        }

        public int Maximum
        {
            get => _maximum;
            set
            {
                _maximum = Math.Max(1, value);
                Invalidate();
            }
        }

        public int Value
        {
            get => _value;
            set
            {
                var clamped = Math.Max(_minimum, Math.Min(_maximum, value));
                if (_value == clamped)
                {
                    return;
                }

                _value = clamped;
                Invalidate();
            }
        }

        public ProgressBarStyle Style
        {
            get => _style;
            set
            {
                if (_style == value)
                {
                    return;
                }

                _style = value;
                UpdateTimer();
                Invalidate();
            }
        }

        public int MarqueeAnimationSpeed
        {
            get => _marqueeSpeed;
            set
            {
                _marqueeSpeed = Math.Max(0, value);
                UpdateTimer();
            }
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            UpdateTimer();
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            base.OnEnabledChanged(e);
            UpdateTimer();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var graphics = e.Graphics;
            Theme.Setup(graphics);

            var radius = Height / 2f;
            var bounds = new RectangleF(0f, 0f, Width, Height);

            Theme.FillRounded(graphics, bounds, radius, Theme.OffBg, Color.Transparent, 0f);

            if (_style == ProgressBarStyle.Marquee)
            {
                var blockWidth = Math.Max(Height, Width * 0.28f);
                var left = (_marqueeOffset * (Width + blockWidth)) - blockWidth;
                var block = new RectangleF(left, 0f, blockWidth, Height);
                Theme.FillRounded(graphics, block, radius, Theme.Brand, Color.Transparent, 0f);
                return;
            }

            var span = Math.Max(1, _maximum - _minimum);
            var ratio = Math.Max(0f, Math.Min(1f, (float)(_value - _minimum) / span));
            if (ratio <= 0f)
            {
                return;
            }

            var fill = new RectangleF(0f, 0f, Math.Max(Height, Width * ratio), Height);
            Theme.FillRounded(graphics, fill, radius, Theme.Brand, Color.Transparent, 0f);
        }

        private void UpdateTimer()
        {
            var shouldRun = _style == ProgressBarStyle.Marquee
                && _marqueeSpeed > 0
                && Enabled
                && Visible
                && IsHandleCreated;

            if (shouldRun == _timer.Enabled)
            {
                return;
            }

            _timer.Enabled = shouldRun;

            if (!shouldRun)
            {
                _marqueeOffset = 0f;
                Invalidate();
            }
        }

        private void OnMarqueeTick(object sender, EventArgs e)
        {
            // 25（WinForms 的默认值）大致对应每秒半圈
            _marqueeOffset += 0.018f * (_marqueeSpeed / 25f);
            if (_marqueeOffset > 1f)
            {
                _marqueeOffset -= 1f;
            }

            Invalidate();
        }
    }

    /// <summary>
    /// 圆角输入框。
    ///
    /// 没有去自绘一个文本控件 —— 输入法、选区、右键菜单全都要自己实现，太容易出岔子。
    /// 这里只是给一个标准的 TextBox 套一层圆角深色的壳，把常用成员转发出去，
    /// 所以调用方仍然可以像用 TextBox 一样用它。
    /// </summary>
    internal sealed class MenuTextBox : Panel
    {
        private readonly TextBox _inner;
        private bool _focused;

        public MenuTextBox()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.UserPaint |
                ControlStyles.ResizeRedraw,
                true);

            BackColor = Theme.CardBg;
            Font = Theme.Ui(this, 9.5f);

            _inner = new TextBox
            {
                BorderStyle = BorderStyle.None,
                BackColor = Theme.LogBg,
                ForeColor = Theme.LogText,
                Font = Font,
                Dock = DockStyle.Fill,
            };

            _inner.TextChanged += (sender, e) => OnTextChanged(e);
            _inner.GotFocus += (sender, e) =>
            {
                _focused = true;
                Invalidate();
            };
            _inner.LostFocus += (sender, e) =>
            {
                _focused = false;
                Invalidate();
            };

            Controls.Add(_inner);
            ApplyPadding();
        }

        public override string Text
        {
            get => _inner.Text;
            set => _inner.Text = value;
        }

        public bool Multiline
        {
            get => _inner.Multiline;
            set
            {
                _inner.Multiline = value;
                ApplyPadding();
            }
        }

        public bool ReadOnly
        {
            get => _inner.ReadOnly;
            set => _inner.ReadOnly = value;
        }

        public bool WordWrap
        {
            get => _inner.WordWrap;
            set => _inner.WordWrap = value;
        }

        public ScrollBars ScrollBars
        {
            get => _inner.ScrollBars;
            set => _inner.ScrollBars = value;
        }

        public int TextLength => _inner.TextLength;

        public int SelectionStart
        {
            get => _inner.SelectionStart;
            set => _inner.SelectionStart = value;
        }

        public void AppendText(string text)
        {
            _inner.AppendText(text);
        }

        public void Clear()
        {
            _inner.Clear();
        }

        public void ScrollToCaret()
        {
            _inner.ScrollToCaret();
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            base.OnEnabledChanged(e);

            if (_inner != null)
            {
                _inner.Enabled = Enabled;
            }

            Invalidate();
        }

        protected override void OnFontChanged(EventArgs e)
        {
            base.OnFontChanged(e);

            // 外层的 Font 只是"代理"，真正显示文字的是内部的 TextBox，
            // 所以字体变化（比如日志框改成等宽字体）必须同步下去
            if (_inner != null)
            {
                _inner.Font = Font;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var graphics = e.Graphics;
            Theme.Setup(graphics);

            var scale = Theme.Px(this, 1f);
            var bounds = new RectangleF(
                0.5f * scale,
                0.5f * scale,
                Width - scale,
                Height - scale);

            var border = !Enabled
                ? Theme.CardBorder
                : (_focused ? Theme.Brand : Theme.CardBorder);
            var width = (_focused ? 1.6f : 1f) * scale;

            Theme.FillRounded(graphics, bounds, 9f * scale, Theme.LogBg, border, width);
        }

        /// <summary>内边距决定了文字离边框多远，单行留得窄一点，多行留出上下呼吸空间。</summary>
        private void ApplyPadding()
        {
            Padding = _inner.Multiline
                ? new Padding(12, 10, 12, 10)
                : new Padding(14, 0, 14, 0);
        }
    }

    /// <summary>标题栏右上角的关闭按钮：平时是灰色的叉，悬停时整块变红。</summary>
    internal sealed class MenuCloseButton : Control
    {
        private bool _hovered;

        public MenuCloseButton()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.UserPaint |
                ControlStyles.ResizeRedraw,
                true);

            Cursor = Cursors.Hand;
            Size = new Size(38, 30);
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            _hovered = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hovered = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var graphics = e.Graphics;
            Theme.Setup(graphics);

            var scale = Theme.Px(this, 1f);
            var bounds = new RectangleF(
                0.5f * scale,
                0.5f * scale,
                Width - scale,
                Height - scale);

            if (_hovered)
            {
                Theme.FillRounded(graphics, bounds, 7f * scale, Theme.Danger, Color.Transparent, 0f);
            }

            var color = _hovered ? Theme.TextMain : Theme.TextMuted;
            var centerX = Width / 2f;
            var centerY = Height / 2f;
            var arm = 5.5f * scale;

            using (var pen = new Pen(color, 1.8f * scale)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
            })
            {
                graphics.DrawLine(pen, centerX - arm, centerY - arm, centerX + arm, centerY + arm);
                graphics.DrawLine(pen, centerX + arm, centerY - arm, centerX - arm, centerY + arm);
            }
        }
    }

    /// <summary>
    /// 自绘标题栏。因为窗口去掉了系统边框（要做深色圆角风格，系统那圈浅灰边框很出戏），
    /// 拖动、关闭这些事就得自己接管。
    /// </summary>
    internal sealed class MenuTitleBar : Control
    {
        private readonly MenuCloseButton _closeButton;
        private string _caption = string.Empty;
        private string _subtitle = string.Empty;
        private bool _showLogo = true;
        private bool _showCloseButton = true;
        private bool _dragging;
        private Point _dragOffset;

        public MenuTitleBar()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.UserPaint |
                ControlStyles.ResizeRedraw,
                true);

            BackColor = Theme.HeaderBg;
            Cursor = Cursors.SizeAll;
            Font = Theme.UiBold(this, 12.5f);

            _closeButton = new MenuCloseButton();
            _closeButton.Click += (sender, e) => CloseRequested?.Invoke(this, EventArgs.Empty);
            Controls.Add(_closeButton);
        }

        /// <summary>用户点了关闭按钮。</summary>
        public event EventHandler CloseRequested;

        public string Caption
        {
            get => _caption;
            set
            {
                _caption = value ?? string.Empty;
                Invalidate();
            }
        }

        public string Subtitle
        {
            get => _subtitle;
            set
            {
                _subtitle = value ?? string.Empty;
                Invalidate();
            }
        }

        public bool ShowLogo
        {
            get => _showLogo;
            set
            {
                _showLogo = value;
                Invalidate();
            }
        }

        public bool ShowCloseButton
        {
            get => _showCloseButton;
            set
            {
                if (_showCloseButton == value)
                {
                    return;
                }

                _showCloseButton = value;

                if (value)
                {
                    Controls.Add(_closeButton);
                }
                else
                {
                    Controls.Remove(_closeButton);
                }

                LayoutCloseButton();
                Invalidate();
            }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            LayoutCloseButton();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                _dragging = true;
                _dragOffset = e.Location;
            }

            base.OnMouseDown(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (_dragging)
            {
                var form = FindForm();
                if (form != null)
                {
                    var screen = PointToScreen(e.Location);
                    form.Location = new Point(screen.X - _dragOffset.X, screen.Y - _dragOffset.Y);
                }
            }

            base.OnMouseMove(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            _dragging = false;
            base.OnMouseUp(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var graphics = e.Graphics;
            Theme.Setup(graphics);

            var scale = Theme.Px(this, 1f);

            using (var brush = new SolidBrush(Theme.HeaderBg))
            {
                graphics.FillRectangle(brush, ClientRectangle);
            }

            // 底部一条橙色分隔线，游戏里的面板标题也是这么压边的
            using (var brush = new SolidBrush(Theme.Brand))
            {
                graphics.FillRectangle(brush, 0f, Height - (2f * scale), Width, 2f * scale);
            }

            var left = 20f * scale;

            if (_showLogo)
            {
                DrawLogo(graphics, left, scale);
                left += 30f * scale;
            }

            using (var font = Theme.UiBold(this, 12.5f))
            {
                var size = TextRenderer.MeasureText(
                    graphics,
                    _caption,
                    font,
                    new Size(int.MaxValue, int.MaxValue),
                    Theme.TextOptions);

                TextRenderer.DrawText(
                    graphics,
                    _caption,
                    font,
                    new Point((int)left, (int)((Height - size.Height) / 2f)),
                    Theme.TextMain,
                    Theme.TextOptions);

                if (_subtitle.Length > 0)
                {
                    using (var subFont = Theme.Ui(this, 9f))
                    {
                        var subSize = TextRenderer.MeasureText(
                            graphics,
                            _subtitle,
                            subFont,
                            new Size(int.MaxValue, int.MaxValue),
                            Theme.TextOptions);

                        TextRenderer.DrawText(
                            graphics,
                            _subtitle,
                            subFont,
                            new Point(
                                (int)(left + size.Width + (10f * scale)),
                                (int)((Height - subSize.Height) / 2f)),
                            Theme.TextMuted,
                            Theme.TextOptions);
                    }
                }
            }
        }

        /// <summary>三条橙色横杠，是个「菜单」的意思，也省得为标题栏单独准备一张图。</summary>
        private void DrawLogo(Graphics graphics, float left, float scale)
        {
            var width = 17f * scale;
            var thickness = 2.6f * scale;
            var gap = 5.2f * scale;
            var blockHeight = (gap * 2f) + thickness;
            var top = (Height - blockHeight) / 2f;

            using (var brush = new SolidBrush(Theme.Brand))
            {
                graphics.FillRectangle(brush, left, top, width, thickness);
                graphics.FillRectangle(brush, left, top + gap, width, thickness);
                graphics.FillRectangle(brush, left, top + (gap * 2f), width * 0.62f, thickness);
            }
        }

        private void LayoutCloseButton()
        {
            if (!_showCloseButton)
            {
                return;
            }

            _closeButton.Location = new Point(
                Width - _closeButton.Width - 8,
                (Height - _closeButton.Height) / 2);
        }
    }
}
