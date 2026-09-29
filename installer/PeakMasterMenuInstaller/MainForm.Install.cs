using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PeakMasterMenu.Installer
{
    /// <summary>
    /// 主窗口的行为部分：自动检测、状态刷新、以及那个「一键安装」按钮背后的一整套流程。
    /// </summary>
    internal sealed partial class MainForm
    {
        private bool _busy;
        private string _lastStatePath;
        private string _lastStatus;
        private CancellationTokenSource _cancellation;

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);

            var payloadVersion = ModPayload.GetVersion();
            if (!string.IsNullOrEmpty(payloadVersion))
                _modCheck.Text = "PeakMasterMenu 本体（安装器内置 v" + payloadVersion + "）";

            AppendLog("PeakMasterMenu 安装器已就绪，正在自动查找 PEAK …");
            DetectGame();
        }

        private void OnGamePathTextChanged(object sender, EventArgs e)
        {
            // 界面构造过程中也可能触发 TextChanged，此时控件还没建全，直接忽略
            if (_installButton == null)
                return;

            RefreshState();
        }

        private void OnOptionChanged(object sender, EventArgs e)
        {
            UpdateActionButtons();
        }

        private void OnDetectClicked(object sender, EventArgs e)
        {
            DetectGame();
        }

        private void OnBrowseClicked(object sender, EventArgs e)
        {
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.Description = "请选择 PEAK 的游戏根目录（里面有 PEAK.exe）";
                dialog.ShowNewFolderButton = false;

                var current = _gamePathBox.Text.Trim();
                if (Directory.Exists(current))
                    dialog.SelectedPath = current;

                if (dialog.ShowDialog(this) != DialogResult.OK)
                    return;

                _gamePathBox.Text = dialog.SelectedPath;
                AppendLog("已手动选择游戏目录：" + dialog.SelectedPath);
            }
        }

        /// <summary>自动检测游戏目录，找到就填进输入框。</summary>
        private async void DetectGame()
        {
            if (_busy)
                return;

            _detectButton.Enabled = false;
            SetStatus("正在查找 PEAK 的安装位置…");

            IList<string> candidates;
            try
            {
                candidates = await Task.Run(() => GameLocator.FindCandidates());
            }
            catch (Exception ex)
            {
                AppendLog("自动检测出错：" + ex.Message);
                candidates = new List<string>();
            }
            finally
            {
                _detectButton.Enabled = true;
            }

            if (candidates.Count == 0)
            {
                AppendLog("自动检测：没找到 PEAK，请点「浏览…」手动选择游戏根目录。");
                SetStatus("没找到 PEAK，请手动选择游戏目录。");
                RefreshState();
                return;
            }

            _gamePathBox.Text = candidates[0];
            AppendLog("自动检测到游戏目录：" + candidates[0]);

            if (candidates.Count > 1)
            {
                AppendLog("另外还发现：" + string.Join("、", candidates.Skip(1).ToArray()));
                AppendLog("（已选用第一个，如需切换请手动选择。）");
            }

            SetStatus("已找到游戏目录。");
            RefreshState();
        }

        /// <summary>根据当前路径刷新所有状态显示。</summary>
        private void RefreshState()
        {
            var gameRoot = _gamePathBox.Text.Trim();
            var pathChanged = !string.Equals(_lastStatePath, gameRoot, StringComparison.OrdinalIgnoreCase);
            _lastStatePath = gameRoot;

            if (!GameLocator.IsGameRoot(gameRoot))
            {
                SetLabel(
                    _pathStatusLabel,
                    gameRoot.Length == 0
                        ? "还没有选择游戏目录，点「自动检测」试试。"
                        : "× 这个目录里没有 PEAK.exe / PEAK_Data，不是 PEAK 的游戏根目录。",
                    gameRoot.Length == 0 ? TextSecondaryColor : DangerColor);

                SetToggleStatus(_prerequisiteCheck, string.Empty, TextSecondaryColor);
                SetToggleStatus(_modCheck, string.Empty, TextSecondaryColor);
                UpdateActionButtons();
                return;
            }

            SetLabel(_pathStatusLabel, "√ 已确认这是 PEAK 的游戏目录。", SuccessColor);

            var bepInExInstalled = GameInstaller.IsBepInExInstalled(gameRoot);

            if (bepInExInstalled)
            {
                var version = GameInstaller.GetBepInExVersion(gameRoot);
                SetToggleStatus(
                    _prerequisiteCheck,
                    version == null ? "已安装" : "已安装 " + version,
                    SuccessColor);

                if (pathChanged)
                    _prerequisiteCheck.Checked = false;
            }
            else
            {
                SetToggleStatus(_prerequisiteCheck, "未安装（必须）", DangerColor);

                if (pathChanged)
                    _prerequisiteCheck.Checked = true;
            }

            var installedModVersion = GameInstaller.GetInstalledModVersion(gameRoot);

            // 这里刻意不写日志：手打路径时每敲一个字符都会走到这，写日志会刷屏
            if (installedModVersion == null)
            {
                SetToggleStatus(_modCheck, "未安装", TextSecondaryColor);
            }
            else
            {
                SetToggleStatus(
                    _modCheck,
                    "已安装 " + installedModVersion + "（将覆盖）",
                    SuccessColor);
            }

            UpdateActionButtons();
        }

        /// <summary>刷新底部两个按钮的可用状态（安装按钮顺便换底色）。</summary>
        private void UpdateActionButtons()
        {
            var gameRoot = _gamePathBox.Text.Trim();
            var valid = GameLocator.IsGameRoot(gameRoot);
            var somethingSelected = _prerequisiteCheck.Checked || _modCheck.Checked;

            // 按钮的禁用配色由 MenuButton 自己处理（跟着 Enabled 变灰），这里只管能不能点
            _installButton.Enabled = !_busy && valid && somethingSelected;

            // 目录无效时短路，避免每敲一个字符都去读磁盘。
            // 只要本 Mod 的 dll 还在，删除按钮就可用 —— 它只删这一个文件。
            _uninstallButton.Enabled = !_busy && valid && GameInstaller.IsModInstalled(gameRoot);
        }

        private async void OnInstallClicked(object sender, EventArgs e)
        {
            if (_busy)
                return;

            var gameRoot = _gamePathBox.Text.Trim();
            if (!GameLocator.IsGameRoot(gameRoot))
            {
                MenuDialog.Show(
                    this,
                    "请先选择一个有效的 PEAK 游戏根目录（目录里应该有 PEAK.exe 和 PEAK_Data）。",
                    "无法安装",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            var installPrerequisite = _prerequisiteCheck.Checked;
            var installMod = _modCheck.Checked;

            if (!installPrerequisite && !installMod)
            {
                MenuDialog.Show(
                    this,
                    "请至少勾选一项要安装的内容。",
                    "无法安装",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            if (!GameInstaller.CanWrite(gameRoot, out var writeError))
            {
                var answer = MenuDialog.Show(
                    this,
                    "这个目录当前不可写：\r\n" + gameRoot + "\r\n\r\n原因：" + writeError +
                    "\r\n\r\n是否以管理员身份重新启动安装器？",
                    "需要管理员权限",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning);

                if (answer == DialogResult.Yes)
                {
                    if (GameInstaller.RelaunchAsAdministrator())
                        Close();
                    else
                        MenuDialog.Show(this, "提权被取消，安装器保持当前权限。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }

                return;
            }

            _logBox.Clear();
            _lastStatus = null;
            AppendLog("安装目标：" + gameRoot);
            AppendLog(installPrerequisite ? "· 将安装 BepInExPack_PEAK（自动下载最新版）" : "· 跳过 BepInEx 前置");
            AppendLog(installMod ? "· 将安装 PeakMasterMenu 本体" : "· 跳过 PeakMasterMenu 本体");
            AppendLog(string.Empty);

            SetBusy(true, _installButton, "正在安装…");
            SetProgress(0);

            // Progress<T> 在构造时捕获当前（UI）线程的同步上下文，
            // 因此安装线程调 Report 时，回调会被 Post 回 UI 线程执行。
            // 注意：Report 是 IProgress<T> 的显式实现，变量必须声明为接口类型才能取到方法组。
            IProgress<string> statusProgress = new Progress<string>(SetStatus);
            IProgress<string> logProgress = new Progress<string>(AppendLog);
            IProgress<int> percentProgress = new Progress<int>(SetProgress);
            var events = new InstallerEvents
            {
                Status = statusProgress.Report,
                Log = logProgress.Report,
                Percent = percentProgress.Report,
            };

            _cancellation = new CancellationTokenSource();
            var token = _cancellation.Token;

            try
            {
                var summary = await Task.Run(
                    () => GameInstaller.Install(gameRoot, installPrerequisite, installMod, events, token),
                    token);

                AppendLog(string.Empty);
                AppendLog("===== 安装完成 =====");
                foreach (var line in summary)
                    AppendLog("  · " + line);

                SetProgress(100);
                SetStatus("安装完成，祝游玩愉快！");

                RefreshState();
                SetLabel(_pathStatusLabel, "√ 安装完成，游戏目录已就绪。", SuccessColor);

                MenuDialog.Show(
                    this,
                    "安装完成！\r\n\r\n接下来：\r\n" +
                    "  1. 启动 PEAK；\r\n" +
                    "  2. 第一次启动会慢一些（BepInEx 在生成配置），等控制台窗口弹出来；\r\n" +
                    "  3. 进游戏后按 F8 打开菜单（可在 BepInEx 配置文件里改成别的键）。",
                    "安装完成",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            catch (OperationCanceledException)
            {
                AppendLog("安装已取消。");
                SetStatus("安装已取消。");
            }
            catch (Exception ex)
            {
                AppendLog("安装失败：" + ex.Message);
                SetStatus("安装失败，请查看上面的日志。");

                MenuDialog.Show(
                    this,
                    "安装过程中出错了：\r\n\r\n" + ex.Message +
                    "\r\n\r\n常见原因：网络被墙 / 杀毒软件拦截 / 游戏目录没有写权限。",
                    "安装失败",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            finally
            {
                _cancellation.Dispose();
                _cancellation = null;
                SetBusy(false, null, null);
            }
        }

        /// <summary>「删除 Mod」按钮：只删本 Mod 的那个 dll，BepInEx 前置和别的插件一概不动。</summary>
        private async void OnUninstallClicked(object sender, EventArgs e)
        {
            if (_busy)
                return;

            var gameRoot = _gamePathBox.Text.Trim();
            if (!GameLocator.IsGameRoot(gameRoot))
            {
                MenuDialog.Show(
                    this,
                    "请先选择一个有效的 PEAK 游戏根目录（目录里应该有 PEAK.exe 和 PEAK_Data）。",
                    "无法删除",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            if (!GameInstaller.IsModInstalled(gameRoot))
            {
                MenuDialog.Show(
                    this,
                    "这个游戏目录里没有找到 PeakMasterMenu，没有可删除的内容。",
                    "无需删除",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                RefreshState();
                return;
            }

            // 游戏开着的时候 dll 被锁住，删了也是白删，直接拦住
            if (GameInstaller.IsGameRunning())
            {
                MenuDialog.Show(
                    this,
                    "检测到 PEAK 正在运行。\r\n\r\n" +
                    "游戏运行时 Mod 的 dll 被占用，删不掉。请先完全退出游戏" +
                    "（包括可能弹出的 BepInEx 控制台窗口）再试。",
                    "请先关闭游戏",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            var pluginPath = Path.Combine(gameRoot, "BepInEx", "plugins", ModPayload.ModFileName);
            var confirmed = MenuDialog.Show(
                this,
                "确定要删除 PeakMasterMenu 吗？\r\n\r\n" +
                "将删除：\r\n" + pluginPath + "\r\n\r\n" +
                "BepInEx 前置和你装的别的 Mod 都会保留，不受影响。\r\n" +
                "以后想再玩，点「一键安装」就能装回来。",
                "确认删除",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2);

            if (confirmed != DialogResult.Yes)
                return;

            if (!GameInstaller.CanWrite(gameRoot, out var writeError))
            {
                var answer = MenuDialog.Show(
                    this,
                    "这个目录当前不可写：\r\n" + gameRoot + "\r\n\r\n原因：" + writeError +
                    "\r\n\r\n是否以管理员身份重新启动安装器？",
                    "需要管理员权限",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning);

                if (answer == DialogResult.Yes)
                {
                    if (GameInstaller.RelaunchAsAdministrator())
                        Close();
                    else
                        MenuDialog.Show(this, "提权被取消，安装器保持当前权限。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }

                return;
            }

            _logBox.Clear();
            _lastStatus = null;
            AppendLog("删除目标：" + gameRoot);
            AppendLog("· 只删除 " + ModPayload.ModFileName + "，BepInEx 前置和其他插件保持不动");
            AppendLog(string.Empty);

            SetBusy(true, _uninstallButton, "正在删除…");
            SetProgress(0);

            IProgress<string> statusProgress = new Progress<string>(SetStatus);
            IProgress<string> logProgress = new Progress<string>(AppendLog);
            IProgress<int> percentProgress = new Progress<int>(SetProgress);
            var events = new InstallerEvents
            {
                Status = statusProgress.Report,
                Log = logProgress.Report,
                Percent = percentProgress.Report,
            };

            _cancellation = new CancellationTokenSource();
            var token = _cancellation.Token;

            try
            {
                var summary = await Task.Run(
                    () => GameInstaller.Uninstall(gameRoot, events, token),
                    token);

                AppendLog(string.Empty);
                AppendLog("===== 删除完成 =====");
                foreach (var line in summary)
                    AppendLog("  · " + line);

                SetStatus("删除完成。");
                RefreshState();

                MenuDialog.Show(
                    this,
                    "已处理：\r\n\r\n  · " + string.Join("\r\n  · ", summary.ToArray()) +
                    "\r\n\r\nBepInEx 前置和其他 Mod 都没动，以后想再玩直接点「一键安装」即可。",
                    "删除完成",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            catch (OperationCanceledException)
            {
                AppendLog("删除已取消。");
                SetStatus("删除已取消。");
            }
            catch (Exception ex)
            {
                AppendLog("删除失败：" + ex.Message);
                SetStatus("删除失败，请查看上面的日志。");

                MenuDialog.Show(
                    this,
                    "删除过程中出错了：\r\n\r\n" + ex.Message +
                    "\r\n\r\n最常见的原因是游戏还在运行、文件被占用，其次是游戏目录没有写权限。",
                    "删除失败",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            finally
            {
                _cancellation.Dispose();
                _cancellation = null;
                SetBusy(false, null, null);
            }
        }

        private void SetBusy(bool busy, MenuButton activeButton, string activeText)
        {
            _busy = busy;

            _gamePathBox.Enabled = !busy;
            _detectButton.Enabled = !busy;
            _browseButton.Enabled = !busy;
            _prerequisiteCheck.Enabled = !busy;
            _modCheck.Enabled = !busy;

            // 只有正在干活的那个按钮换成进度文字，另一个保持原样、只是被禁用
            _installButton.Text = busy && activeButton == _installButton ? activeText : "一 键 安 装";
            _uninstallButton.Text = busy && activeButton == _uninstallButton ? activeText : "删除 Mod";

            UpdateActionButtons();
        }

        private void SetStatus(string message)
        {
            if (string.Equals(_lastStatus, message, StringComparison.Ordinal))
                return;

            _lastStatus = message;
            _statusLabel.Text = message;
        }

        private void SetProgress(int value)
        {
            if (value < 0)
            {
                _progressBar.Style = ProgressBarStyle.Marquee;
                _progressBar.MarqueeAnimationSpeed = 25;
                return;
            }

            _progressBar.Style = ProgressBarStyle.Continuous;
            _progressBar.MarqueeAnimationSpeed = 0;

            var clamped = Math.Max(0, Math.Min(_progressBar.Maximum, value));
            if (_progressBar.Value != clamped)
                _progressBar.Value = clamped;
        }

        private void AppendLog(string message)
        {
            _logBox.AppendText(message + Environment.NewLine);
            _logBox.SelectionStart = _logBox.TextLength;
            _logBox.ScrollToCaret();
        }

        private static void SetLabel(Label label, string text, Color color)
        {
            label.Text = text;
            label.ForeColor = color;
        }

        /// <summary>刷新开关行右侧的状态文字（例如「已安装 1.0.0」）。</summary>
        private static void SetToggleStatus(MenuToggle toggle, string text, Color color)
        {
            toggle.Status = text;
            toggle.StatusColor = color;
        }
    }
}
