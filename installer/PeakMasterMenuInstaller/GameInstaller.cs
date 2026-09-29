using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Windows.Forms;

namespace PeakMasterMenu.Installer
{
    /// <summary>安装过程中的回调集合。三个委托都可能为空，调用方按需给。</summary>
    internal sealed class InstallerEvents
    {
        /// <summary>更新那一行状态文字。</summary>
        public Action<string> Status { get; set; }

        /// <summary>往日志框追加一行。</summary>
        public Action<string> Log { get; set; }

        /// <summary>0-100 的总体进度；传 -1 表示进度未知（UI 走跑马灯）。</summary>
        public Action<int> Percent { get; set; }
    }

    /// <summary>
    /// 真正干活的安装逻辑：下载前置 → 解压到游戏目录 → 写入 Mod 本体。
    /// 全部是同步方法，由调用方丢到后台线程上跑。
    /// </summary>
    internal static class GameInstaller
    {
        public const string PrerequisiteNamespace = "BepInEx";
        public const string PrerequisitePackage = "BepInExPack_PEAK";

        /// <summary>查不到最新版时用来拼下载地址的兜底版本（与 csproj 里的依赖声明保持一致）。</summary>
        public const string PrerequisiteFallbackVersion = "5.4.75301";

        /// <summary>下载阶段占用的进度区间（剩下的留给解压和写文件）。</summary>
        private const int DownloadPercentEnd = 60;
        private const int ExtractPercentEnd = 95;

        /// <summary>Thunderstore 包里属于「商店元数据」的文件，不需要进游戏目录。</summary>
        private static readonly string[] PackageMetadataFiles =
        {
            "manifest.json",
            "icon.png",
            "readme.md",
            "changelog.md",
            "license",
        };

        public static bool IsBepInExInstalled(string gameRoot)
        {
            if (string.IsNullOrWhiteSpace(gameRoot) || !Directory.Exists(gameRoot))
                return false;

            if (File.Exists(Path.Combine(gameRoot, "winhttp.dll")))
                return true;

            if (File.Exists(Path.Combine(gameRoot, "doorstop_config.ini")))
                return true;

            return File.Exists(Path.Combine(gameRoot, "BepInEx", "core", "BepInEx.Core.dll"));
        }

        /// <summary>
        /// 已安装的 BepInEx 版本。
        /// 优先读 core 里 dll 的版本信息（那才是 BepInEx 自己的版本号）；
        /// 读不到再退回 .doorstop_version —— 注意那个是 doorstop 的版本，不是 BepInEx 的。
        /// </summary>
        public static string GetBepInExVersion(string gameRoot)
        {
            foreach (var fileName in new[] { "BepInEx.Preloader.dll", "BepInEx.dll" })
            {
                var path = Path.Combine(gameRoot, "BepInEx", "core", fileName);
                if (!File.Exists(path))
                    continue;

                var version = ReadFileVersion(path);
                if (!string.IsNullOrWhiteSpace(version))
                    return version;
            }

            try
            {
                var path = Path.Combine(gameRoot, ".doorstop_version");
                if (!File.Exists(path))
                    return null;

                var text = File.ReadAllText(path).Trim();
                return text.Length == 0 ? null : text;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>游戏目录里已装的 Mod 版本，没装返回 null。</summary>
        public static string GetInstalledModVersion(string gameRoot)
        {
            var path = Path.Combine(gameRoot, "BepInEx", "plugins", ModPayload.ModFileName);
            if (!File.Exists(path))
                return null;

            return ReadFileVersion(path);
        }

        /// <summary>读取文件版本号，顺手去掉 "+commit" 之类的后缀。</summary>
        private static string ReadFileVersion(string path)
        {
            try
            {
                var info = FileVersionInfo.GetVersionInfo(path);
                var version = info.ProductVersion;
                if (string.IsNullOrWhiteSpace(version))
                    version = info.FileVersion;

                if (string.IsNullOrWhiteSpace(version))
                    return null;

                var plusIndex = version.IndexOf('+');
                if (plusIndex > 0)
                    version = version.Substring(0, plusIndex);

                return version.Trim();
            }
            catch
            {
                return null;
            }
        }

        /// <summary>探测目录是否可写（游戏装在 Program Files 时常常不可写）。</summary>
        public static bool CanWrite(string directory, out string error)
        {
            error = null;
            try
            {
                Directory.CreateDirectory(directory);
                var probe = Path.Combine(directory, ".peakmastermenu-write-test-" + Guid.NewGuid().ToString("N") + ".tmp");
                using (File.Create(probe, 1, FileOptions.DeleteOnClose))
                {
                }

                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        /// <summary>以管理员身份重新拉起自己（会弹 UAC）。</summary>
        public static bool RelaunchAsAdministrator()
        {
            try
            {
                var startInfo = new ProcessStartInfo(Application.ExecutablePath)
                {
                    UseShellExecute = true,
                    Verb = "runas",
                    WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory,
                };

                Process.Start(startInfo);
                return true;
            }
            catch
            {
                // 用户在 UAC 弹窗点了「否」
                return false;
            }
        }

        /// <summary>执行一次完整安装，返回给用户看的成果摘要。</summary>
        public static IList<string> Install(string gameRoot, bool installPrerequisite, bool installMod, InstallerEvents events, CancellationToken token)
        {
            var summary = new List<string>();
            var tempDirectory = CreateTempDirectory();

            try
            {
                if (installPrerequisite)
                    summary.Add(InstallPrerequisite(gameRoot, tempDirectory, events, token));

                if (installMod)
                    summary.Add(InstallModBody(gameRoot, events, token));

                SetStatus(events, "安装完成");
                SetPercent(events, 100);
            }
            finally
            {
                TryDeleteDirectory(tempDirectory);
            }

            return summary;
        }

        private static string InstallPrerequisite(string gameRoot, string tempDirectory, InstallerEvents events, CancellationToken token)
        {
            SetStatus(events, "正在查询 BepInEx 前置的最新版本…");
            SetPercent(events, -1);

            var release = ThunderstoreClient.ResolveLatest(
                PrerequisiteNamespace,
                PrerequisitePackage,
                PrerequisiteFallbackVersion,
                token);

            LogMessage(events, "前置包：" + release.DisplayText);

            var archivePath = Path.Combine(tempDirectory, release.FullName + "-" + release.VersionNumber + ".zip");

            try
            {
                SetStatus(events, "正在下载 " + release.FullName + " …");
                var progress = new Progress<DownloadProgress>(p => ReportDownloadProgress(events, release, p));
                ThunderstoreClient.Download(release.DownloadUrl, archivePath, progress, token);

                SetStatus(events, "正在解压前置包到游戏目录…");
                var fileCount = ExtractPackage(archivePath, gameRoot, token, events, DownloadPercentEnd, ExtractPercentEnd);
                LogMessage(events, string.Format(CultureInfo.InvariantCulture, "前置包解压完成，共写入 {0} 个文件。", fileCount));

                return "已安装前置 " + release.DisplayText;
            }
            finally
            {
                TryDeleteFile(archivePath);
            }
        }

        private static string InstallModBody(string gameRoot, InstallerEvents events, CancellationToken token)
        {
            SetStatus(events, "正在写入 PeakMasterMenu 本体…");
            SetPercent(events, ExtractPercentEnd);

            token.ThrowIfCancellationRequested();

            string description;
            var bytes = ModPayload.Read(out description);

            var pluginsDirectory = Path.Combine(gameRoot, "BepInEx", "plugins");
            Directory.CreateDirectory(pluginsDirectory);

            var targetPath = Path.Combine(pluginsDirectory, ModPayload.ModFileName);
            File.WriteAllBytes(targetPath, bytes);

            LogMessage(events, "已写入 " + targetPath);
            LogMessage(events, string.Format(
                CultureInfo.InvariantCulture,
                "来源：{0}（{1}）",
                description,
                FormatSize(bytes.Length)));

            return "已安装 " + ModPayload.ModFileName;
        }

        /// <summary>Mod 本体是不是已经装在游戏目录里了（只看文件在不在，不依赖能否读到版本号）。</summary>
        public static bool IsModInstalled(string gameRoot)
        {
            if (string.IsNullOrWhiteSpace(gameRoot) || !Directory.Exists(gameRoot))
                return false;

            return File.Exists(Path.Combine(gameRoot, "BepInEx", "plugins", ModPayload.ModFileName));
        }

        /// <summary>PEAK 是不是正在运行 —— 运行时 dll 被占用，删不掉，得先让用户关游戏。</summary>
        public static bool IsGameRunning()
        {
            Process[] processes;
            try
            {
                processes = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(GameLocator.ExecutableName));
            }
            catch
            {
                return false;
            }

            try
            {
                return processes.Length > 0;
            }
            finally
            {
                foreach (var process in processes)
                    process.Dispose();
            }
        }

        /// <summary>
        /// 删除 Mod 本体。
        ///
        /// 只动 BepInEx\plugins 下属于本 Mod 的那一个 dll —— BepInEx 前置、配置文件
        /// 以及用户装的别的插件一概不碰。删的时候顺手把别人的 Mod 干掉才是最糟糕的事。
        /// </summary>
        public static IList<string> Uninstall(string gameRoot, InstallerEvents events, CancellationToken token)
        {
            if (string.IsNullOrWhiteSpace(gameRoot))
                throw new ArgumentException("游戏目录不能为空。", "gameRoot");

            var summary = new List<string>();
            var pluginPath = Path.Combine(Path.GetFullPath(gameRoot), "BepInEx", "plugins", ModPayload.ModFileName);

            SetStatus(events, "正在删除 Mod 本体…");
            SetPercent(events, 30);
            token.ThrowIfCancellationRequested();

            if (!File.Exists(pluginPath))
            {
                LogMessage(events, "没找到 " + pluginPath + "，跳过。");
                summary.Add(ModPayload.ModFileName + " 本来就不在，已跳过");
            }
            else
            {
                File.Delete(pluginPath);
                LogMessage(events, "已删除 " + pluginPath);
                summary.Add("已删除 " + ModPayload.ModFileName);
            }

            SetStatus(events, "删除完成");
            SetPercent(events, 100);

            return summary;
        }

        private static void ReportDownloadProgress(InstallerEvents events, PackageRelease release, DownloadProgress progress)
        {
            if (progress.Total > 0)
            {
                var percent = (int)(progress.Received * DownloadPercentEnd / progress.Total);
                SetPercent(events, percent);
                SetStatus(events, string.Format(
                    CultureInfo.InvariantCulture,
                    "正在下载 {0}：{1} / {2}",
                    release.FullName,
                    FormatSize(progress.Received),
                    FormatSize(progress.Total)));
            }
            else
            {
                SetStatus(events, string.Format(
                    CultureInfo.InvariantCulture,
                    "正在下载 {0}：{1}",
                    release.FullName,
                    FormatSize(progress.Received)));
            }
        }

        /// <summary>
        /// 把 Thunderstore 的包解开铺到游戏目录。
        ///
        /// 两个必须处理的点：
        ///   * 包内通常裹着一层以包名命名的顶层目录（BepInExPack_PEAK/…），要剥掉，
        ///     否则会变成 PEAK/BepInExPack_PEAK/winhttp.dll 这种错误结构；
        ///   * manifest.json / icon.png 之类的商店元数据不该进游戏目录，跳过。
        /// </summary>
        private static int ExtractPackage(string archivePath, string destinationRoot, CancellationToken token, InstallerEvents events, int percentFrom, int percentTo)
        {
            var destinationFull = Path.GetFullPath(destinationRoot);
            Directory.CreateDirectory(destinationFull);

            using (var archive = ZipFile.OpenRead(archivePath))
            {
                var entries = new List<ZipArchiveEntry>();
                foreach (var entry in archive.Entries)
                {
                    // 目录条目（Name 为空）不需要单独处理，写文件时会连带创建
                    if (string.IsNullOrEmpty(entry.Name))
                        continue;

                    // 商店元数据（manifest.json / icon.png / README.md …）不该进游戏目录。
                    // 必须在这就算「公共顶层目录」之前剔掉：它们通常躺在包根，
                    // 留着会让算法误以为这个包没有统一的顶层目录（真实包结构是
                    // BepInExPack_PEAK/BepInEx/... 加根目录几个元数据文件）。
                    if (IsPackageMetadata(entry.FullName))
                        continue;

                    entries.Add(entry);
                }

                var prefix = FindCommonDirectoryPrefix(entries);
                var written = 0;

                for (var index = 0; index < entries.Count; index++)
                {
                    token.ThrowIfCancellationRequested();

                    var entry = entries[index];
                    var relative = prefix == null
                        ? entry.FullName
                        : entry.FullName.Substring(prefix.Length);

                    relative = relative.Replace('/', Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar);
                    if (relative.Length == 0)
                        continue;

                    var targetPath = Path.GetFullPath(Path.Combine(destinationFull, relative));

                    // 防目录穿越：解压目标必须仍然在游戏目录里
                    if (!targetPath.StartsWith(destinationFull, StringComparison.OrdinalIgnoreCase))
                        continue;

                    // BepInEx 的配置文件是给用户自己改的（改热键之类），重复安装时别覆盖掉
                    if (IsUserConfiguration(relative) && File.Exists(targetPath))
                        continue;

                    var targetDirectory = Path.GetDirectoryName(targetPath);
                    if (!string.IsNullOrEmpty(targetDirectory))
                        Directory.CreateDirectory(targetDirectory);

                    entry.ExtractToFile(targetPath, true);
                    written++;

                    if (percentTo > percentFrom)
                        SetPercent(events, percentFrom + (int)((long)(index + 1) * (percentTo - percentFrom) / entries.Count));
                }

                return written;
            }
        }

        /// <summary>所有条目是否都在同一个顶层目录下；是则返回「顶层目录名/」，否则返回 null。</summary>
        private static string FindCommonDirectoryPrefix(IList<ZipArchiveEntry> entries)
        {
            if (entries.Count == 0)
                return null;

            string prefix = null;

            foreach (var entry in entries)
            {
                var slashIndex = entry.FullName.IndexOf('/');
                if (slashIndex <= 0)
                    return null; // 有文件直接躺在包根，说明没有统一的顶层目录

                var segment = entry.FullName.Substring(0, slashIndex + 1);

                if (prefix == null)
                    prefix = segment;
                else if (!string.Equals(prefix, segment, StringComparison.Ordinal))
                    return null;
            }

            return prefix;
        }

        /// <summary>是不是躺在压缩包根目录的商店元数据文件。</summary>
        private static bool IsPackageMetadata(string zipEntryName)
        {
            // zip 内部一律用 '/' 作分隔符；带分隔符说明不在包根，不可能是元数据
            if (zipEntryName.IndexOf('/') >= 0)
                return false;

            foreach (var name in PackageMetadataFiles)
            {
                if (string.Equals(zipEntryName, name, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        /// <summary>用户自己会改的配置文件，重复安装时不该覆盖。</summary>
        private static bool IsUserConfiguration(string relativePath)
        {
            return relativePath.StartsWith(@"BepInEx\config\", StringComparison.OrdinalIgnoreCase);
        }

        private static string CreateTempDirectory()
        {
            var path = Path.Combine(Path.GetTempPath(), "PeakMasterMenuInstaller", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }

        private static void SetStatus(InstallerEvents events, string message)
        {
            if (events != null && events.Status != null)
                events.Status(message);
        }

        private static void LogMessage(InstallerEvents events, string message)
        {
            if (events != null && events.Log != null)
                events.Log(message);
        }

        private static void SetPercent(InstallerEvents events, int value)
        {
            if (events != null && events.Percent != null)
                events.Percent(value);
        }

        private static string FormatSize(long bytes)
        {
            if (bytes < 1024)
                return bytes.ToString(CultureInfo.InvariantCulture) + " B";

            if (bytes < 1024L * 1024L)
                return (bytes / 1024.0).ToString("0.0", CultureInfo.InvariantCulture) + " KB";

            return (bytes / (1024.0 * 1024.0)).ToString("0.00", CultureInfo.InvariantCulture) + " MB";
        }

        private static void TryDeleteFile(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
            }
        }

        private static void TryDeleteDirectory(string path)
        {
            try
            {
                if (Directory.Exists(path))
                    Directory.Delete(path, true);
            }
            catch
            {
            }
        }
    }
}
