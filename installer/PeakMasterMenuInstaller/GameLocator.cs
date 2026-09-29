using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace PeakMasterMenu.Installer
{
    /// <summary>
    /// 负责把 PEAK 的游戏根目录找出来。
    ///
    /// 查找顺序（先准后全）：
    ///   1. 正在运行的游戏进程 —— 用户开着游戏时最准；
    ///   2. Steam 注册表里的安装目录 + 它的所有库目录（libraryfolders.vdf）；
    ///   3. 各盘符下的常见安装位置，作为最后的兜底。
    /// 任何一步找到的结果都会经过 <see cref="IsGameRoot"/> 校验，避免误报。
    /// </summary>
    internal static class GameLocator
    {
        public const string ExecutableName = "PEAK.exe";

        private const string GameFolderName = "PEAK";
        private const string SteamAppsFolder = "steamapps";
        private const string CommonFolder = "common";

        private static readonly string[] SteamRegistryKeys =
        {
            @"HKEY_CURRENT_USER\Software\Valve\Steam",
            @"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam",
            @"HKEY_LOCAL_MACHINE\SOFTWARE\Valve\Steam",
        };

        private static readonly string[] SteamPathValueNames = { "SteamPath", "InstallPath" };

        /// <summary>相对各个盘符根目录的候选位置，兜底用。</summary>
        private static readonly string[] RelativeGameLocations =
        {
            @"steamapps\common\PEAK",
            @"Steam\steamapps\common\PEAK",
            @"SteamLibrary\steamapps\common\PEAK",
            @"Games\Steam\steamapps\common\PEAK",
            @"Games\SteamLibrary\steamapps\common\PEAK",
            @"Program Files (x86)\Steam\steamapps\common\PEAK",
            @"Program Files\Steam\steamapps\common\PEAK",
        };

        /// <summary>判断给定目录是不是 PEAK 的游戏根目录（弹窗、校验都用它）。</summary>
        public static bool IsGameRoot(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return false;

            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(path.Trim().Trim('"'));
            }
            catch
            {
                return false;
            }

            if (!File.Exists(Path.Combine(fullPath, ExecutableName)))
                return false;

            // PEAK_Data 存在才说明这是真正的游戏目录，而不是随便一个放了 exe 的文件夹
            return Directory.Exists(Path.Combine(fullPath, "PEAK_Data"));
        }

        /// <summary>找出所有能确认的 PEAK 游戏根目录（已按出现顺序去重）。</summary>
        public static IList<string> FindCandidates()
        {
            var results = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var candidate in EnumerateCandidates())
            {
                if (!IsGameRoot(candidate))
                    continue;

                string fullPath;
                try
                {
                    fullPath = Path.GetFullPath(candidate).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                }
                catch
                {
                    continue;
                }

                if (seen.Add(fullPath))
                    results.Add(fullPath);
            }

            return results;
        }

        private static IEnumerable<string> EnumerateCandidates()
        {
            foreach (var path in FromRunningGame())
                yield return path;

            foreach (var path in FromSteamInstallations())
                yield return path;

            foreach (var path in FromCommonLocations())
                yield return path;
        }

        /// <summary>从正在运行的 PEAK 进程反推安装目录。</summary>
        private static IEnumerable<string> FromRunningGame()
        {
            Process[] processes;
            try
            {
                processes = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(ExecutableName));
            }
            catch
            {
                yield break;
            }

            foreach (var process in processes)
            {
                using (process)
                {
                    string fileName = null;
                    try
                    {
                        fileName = process.MainModule == null ? null : process.MainModule.FileName;
                    }
                    catch
                    {
                        // 权限不足时读不到主模块路径，忽略即可
                    }

                    if (!string.IsNullOrEmpty(fileName))
                    {
                        var directory = Path.GetDirectoryName(fileName);
                        if (!string.IsNullOrEmpty(directory))
                            yield return directory;
                    }
                }
            }
        }

        /// <summary>从 Steam 注册表 + 每个库目录推出游戏位置。</summary>
        private static IEnumerable<string> FromSteamInstallations()
        {
            var steamRoots = new List<string>();

            foreach (var key in SteamRegistryKeys)
            {
                foreach (var valueName in SteamPathValueNames)
                {
                    string value = null;
                    try
                    {
                        value = Registry.GetValue(key, valueName, null) as string;
                    }
                    catch
                    {
                        // 注册表项不存在 / 无权限，继续
                    }

                    if (!string.IsNullOrWhiteSpace(value))
                        steamRoots.Add(value);
                }
            }

            foreach (var steamRoot in steamRoots)
            {
                yield return Path.Combine(steamRoot, SteamAppsFolder, CommonFolder, GameFolderName);

                foreach (var library in ReadLibraryFolders(steamRoot))
                    yield return Path.Combine(library, SteamAppsFolder, CommonFolder, GameFolderName);
            }
        }

        /// <summary>
        /// 解析 steamapps/libraryfolders.vdf，取出所有 Steam 库的根目录。
        /// 文件结构简单，用正则抓 "path" 就够了，不必引入完整的 VDF 解析器。
        /// </summary>
        private static IEnumerable<string> ReadLibraryFolders(string steamRoot)
        {
            string filePath;
            string text;
            try
            {
                filePath = Path.Combine(steamRoot, SteamAppsFolder, "libraryfolders.vdf");
                if (!File.Exists(filePath))
                    yield break;

                text = File.ReadAllText(filePath);
            }
            catch
            {
                yield break;
            }

            foreach (Match match in Regex.Matches(text, "\"path\"\\s*\"(?<path>[^\"]+)\""))
            {
                // VDF 里的反斜杠是转义过的（D:\\SteamLibrary），还原成正常路径
                var raw = match.Groups["path"].Value.Replace("\\\\", "\\");
                if (!string.IsNullOrWhiteSpace(raw))
                    yield return raw;
            }
        }

        /// <summary>各盘符下的常见安装位置，纯粹是为了「什么都不用选」而做的兜底。</summary>
        private static IEnumerable<string> FromCommonLocations()
        {
            string[] drives;
            try
            {
                drives = Directory.GetLogicalDrives();
            }
            catch
            {
                yield break;
            }

            foreach (var drive in drives)
            {
                foreach (var relative in RelativeGameLocations)
                    yield return Path.Combine(drive, relative);
            }
        }
    }
}
