using System.Drawing;
using System.Windows.Forms;

namespace PeakMasterMenu.Installer
{
    /// <summary>
    /// 安装器主窗口。
    ///
    /// 视觉上刻意向游戏内菜单靠拢（暖褐底 + 橙黄主色 + 圆角卡片），
    /// 所以窗口去掉了系统边框、控件也全部是自绘的 —— 具体见 Theme.cs 和 MenuControls.cs。
    /// 界面依然全部手写（没有用 WinForms 设计器），位置都是绝对坐标，改起来直接看数字就行。
    ///
    /// 缩放策略：WinForms 的 AutoScaleMode.Font 在高 DPI 下会把 XY 缩得不一样（因为字体宽高缩放比例不同），
    /// 窗口会变形。这里用 AutoScaleMode.None，然后每个坐标和字号都通过 Theme.Px 按当前 DPI 手动放大，
    /// 保证 800×700 的比例始终一致。
    /// </summary>
    internal sealed partial class MainForm : Form
    {
        // 所有尺寸都是 96 DPI 下的设计像素，运行时会按当前显示器 DPI 等比放大
        private const float WindowWidth = 800f;
        private const float WindowHeight = 700f;
        private const float TitleBarHeight = 46f;
        private const float PadX = 20f;
        private const float ContentWidth = WindowWidth - (PadX * 2f);

        // 行为代码（MainForm.Install.cs）里用的还是这些名字，换配色只要改 Theme 一处
        private static readonly Color WindowBackColor = Theme.WindowBg;
        private static readonly Color TextPrimaryColor = Theme.TextMain;
        private static readonly Color TextSecondaryColor = Theme.TextMuted;
        private static readonly Color SuccessColor = Theme.Success;
        private static readonly Color DangerColor = Theme.Danger;

        private MenuTitleBar _titleBar;
        private MenuTextBox _gamePathBox;
        private MenuButton _detectButton;
        private MenuButton _browseButton;
        private Label _pathStatusLabel;

        private MenuToggle _prerequisiteCheck;
        private MenuToggle _modCheck;

        private readonly ToolTip _toolTips = new ToolTip();

        private MenuButton _installButton;
        private MenuButton _uninstallButton;
        private MenuProgressBar _progressBar;
        private Label _statusLabel;
        private MenuTextBox _logBox;

        public MainForm()
        {
            BuildInterface();
        }

        /// <summary>快捷生成按当前 DPI 缩放的坐标和尺寸。</summary>
        private Point P(float x, float y) => new Point((int)Theme.Px(this, x), (int)Theme.Px(this, y));
        private Size S(float w, float h) => new Size((int)Theme.Px(this, w), (int)Theme.Px(this, h));

        /// <summary>无边框窗口要自己加投影，否则看着像贴在桌面上的一张纸。</summary>
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

        private void BuildInterface()
        {
            SuspendLayout();

            Text = "PeakMasterMenu 安装器";
            Font = Theme.Ui(this, 9f);
            BackColor = WindowBackColor;
            ForeColor = TextPrimaryColor;
            FormBorderStyle = FormBorderStyle.None;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.None;
            ClientSize = S(WindowWidth, WindowHeight);
            DoubleBuffered = true;

            BuildTitleBar();
            BuildGameCard();
            BuildOptionsCard();
            BuildActionArea();
            BuildLogCard();

            ResumeLayout(false);
        }

        private void BuildTitleBar()
        {
            _titleBar = new MenuTitleBar
            {
                Caption = "PeakMasterMenu",
                Subtitle = "安装器",
                Location = P(0, 0),
                Size = S(WindowWidth, TitleBarHeight),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            };

            _titleBar.CloseRequested += (sender, e) => Close();

            Controls.Add(_titleBar);
        }

        private void BuildGameCard()
        {
            var card = new MenuCard
            {
                Caption = "游戏目录",
                Hint = "PEAK 根目录，里面应该有 PEAK.exe 和 PEAK_Data",
                Location = P(PadX, 58f),
                Size = S(ContentWidth, 116f),
            };

            _gamePathBox = new MenuTextBox
            {
                Location = P(18f, 38f),
                Size = S(534f, 34f),
                Font = Theme.Ui(this, 9.5f),
            };
            _gamePathBox.TextChanged += OnGamePathTextChanged;

            _detectButton = new MenuButton
            {
                Text = "自动检测",
                Style = MenuButtonStyle.Secondary,
                Location = P(562f, 38f),
                Size = S(86f, 34f),
                Font = Theme.UiBold(this, 9f),
            };
            _detectButton.Click += OnDetectClicked;

            _browseButton = new MenuButton
            {
                Text = "浏览…",
                Style = MenuButtonStyle.Secondary,
                Location = P(656f, 38f),
                Size = S(86f, 34f),
                Font = Theme.UiBold(this, 9f),
            };
            _browseButton.Click += OnBrowseClicked;

            _pathStatusLabel = new Label
            {
                Location = P(20f, 82f),
                Size = S(720f, 20f),
                AutoSize = false,
                BackColor = Color.Transparent,
                Font = Theme.Ui(this, 8.75f),
                ForeColor = TextSecondaryColor,
                Text = "还没有选择游戏目录，点「自动检测」试试。",
            };

            card.Controls.Add(_gamePathBox);
            card.Controls.Add(_detectButton);
            card.Controls.Add(_browseButton);
            card.Controls.Add(_pathStatusLabel);

            Controls.Add(card);
        }

        private void BuildOptionsCard()
        {
            var card = new MenuCard
            {
                Caption = "安装内容",
                Location = P(PadX, 186f),
                Size = S(ContentWidth, 180f),
            };

            _prerequisiteCheck = new MenuToggle
            {
                Text = "BepInEx 前置（BepInExPack_PEAK）—— 菜单 Mod 的运行环境",
                Location = P(14f, 38f),
                Size = S(732f, 46f),
            };
            _prerequisiteCheck.CheckedChanged += OnOptionChanged;

            _modCheck = new MenuToggle
            {
                Text = "PeakMasterMenu 本体（菜单 Mod 的 dll）",
                Location = P(14f, 90f),
                Size = S(732f, 46f),
            };
            _modCheck.CheckedChanged += OnOptionChanged;

            var hint = new Label
            {
                Text = "前置已经装过的话会自动取消勾选；想更新到最新版可以重新勾上，安装器会重新下载并覆盖。",
                Location = P(20f, 146f),
                Size = S(720f, 20f),
                AutoSize = false,
                BackColor = Color.Transparent,
                Font = Theme.Ui(this, 8.25f),
                ForeColor = TextSecondaryColor,
            };

            card.Controls.Add(_prerequisiteCheck);
            card.Controls.Add(_modCheck);
            card.Controls.Add(hint);

            Controls.Add(card);
        }

        private void BuildActionArea()
        {
            _installButton = new MenuButton
            {
                Text = "一 键 安 装",
                Style = MenuButtonStyle.Primary,
                Location = P(PadX, 380f),
                Size = S(560f, 52f),
                Font = Theme.UiBold(this, 11.5f),
                Enabled = false,
            };
            _installButton.Click += OnInstallClicked;

            // 深色底 + 红字红边比「红底白字」克制：和主按钮并排时不会抢走「一键安装」的注意力，
            // 但颜色已经足够提示这是一个不可逆的操作。
            _uninstallButton = new MenuButton
            {
                Text = "删除 Mod",
                Style = MenuButtonStyle.Danger,
                Location = P(592f, 380f),
                Size = S(188f, 52f),
                Font = Theme.UiBold(this, 10f),
                Enabled = false,
            };
            _uninstallButton.Click += OnUninstallClicked;
            _toolTips.SetToolTip(_uninstallButton, "只删除 PeakMasterMenu 这一个 dll。BepInEx 前置、配置文件和你装的别的 Mod 都不会被动。");

            _progressBar = new MenuProgressBar
            {
                Location = P(PadX, 448f),
                Size = S(ContentWidth, 10f),
            };

            _statusLabel = new Label
            {
                Location = P(PadX, 466f),
                Size = S(ContentWidth, 20f),
                AutoSize = false,
                BackColor = Color.Transparent,
                Font = Theme.Ui(this, 8.75f),
                ForeColor = TextSecondaryColor,
                Text = "准备就绪。",
            };

            Controls.Add(_installButton);
            Controls.Add(_uninstallButton);
            Controls.Add(_progressBar);
            Controls.Add(_statusLabel);
        }

        private void BuildLogCard()
        {
            var card = new MenuCard
            {
                Caption = "安装日志",
                Location = P(PadX, 500f),
                Size = S(ContentWidth, 180f),
            };

            _logBox = new MenuTextBox
            {
                Location = P(14f, 36f),
                Size = S(732f, 128f),
                Multiline = true,
                ReadOnly = true,
                WordWrap = false,
                ScrollBars = ScrollBars.Vertical,
                Font = Theme.Mono(this, 8.5f),
            };

            card.Controls.Add(_logBox);

            Controls.Add(card);
        }
    }
}
