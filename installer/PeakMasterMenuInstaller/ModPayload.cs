using System;
using System.Diagnostics;
using System.IO;

namespace PeakMasterMenu.Installer
{
    /// <summary>
    /// Mod 本体 dll 的来源。
    ///
    /// 优先读安装器同目录下的同名 dll（方便换新版 dll 而不用重编安装器），
    /// 找不到就用编译时嵌进 exe 的那一份 —— 这样把 exe 单独发给别人也能装。
    /// </summary>
    internal static class ModPayload
    {
        public const string ModFileName = "PeakModder.PeakMasterMenu.dll";

        private const string EmbeddedResourceName = "PeakMasterMenu.Payload.dll";

        private static string _cachedVersion;
        private static bool _versionResolved;

        /// <summary>取出 Mod 本体的字节，并说明来源（用于日志）。</summary>
        public static byte[] Read(out string description)
        {
            var beside = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ModFileName);
            if (File.Exists(beside))
            {
                description = beside;
                return File.ReadAllBytes(beside);
            }

            using (var stream = typeof(ModPayload).Assembly.GetManifestResourceStream(EmbeddedResourceName))
            {
                if (stream == null)
                {
                    throw new InvalidOperationException(
                        "安装器不完整：既没有找到同目录下的 " + ModFileName + "，exe 里也没有内置的副本。请重新获取完整的安装器。");
                }

                description = "安装器内置";
                return ReadAllBytes(stream);
            }
        }

        /// <summary>Mod 本体的版本号（取不到时返回 null）。</summary>
        public static string GetVersion()
        {
            if (_versionResolved)
                return _cachedVersion;

            _versionResolved = true;

            var probePath = Path.Combine(Path.GetTempPath(), "PeakMasterMenuInstaller-probe-" + Guid.NewGuid().ToString("N") + ".dll");
            try
            {
                string description;
                var bytes = Read(out description);

                // 如果来源本来就是磁盘上的文件，直接读它的版本信息即可
                var sourcePath = File.Exists(description) ? description : null;
                if (sourcePath != null)
                {
                    _cachedVersion = ReadVersion(sourcePath);
                    return _cachedVersion;
                }

                File.WriteAllBytes(probePath, bytes);
                _cachedVersion = ReadVersion(probePath);
            }
            catch
            {
                _cachedVersion = null;
            }
            finally
            {
                TryDelete(probePath);
            }

            return _cachedVersion;
        }

        private static string ReadVersion(string path)
        {
            try
            {
                var info = FileVersionInfo.GetVersionInfo(path);
                var version = info.ProductVersion;
                if (string.IsNullOrWhiteSpace(version))
                    version = info.FileVersion;

                if (string.IsNullOrWhiteSpace(version))
                    return null;

                // ProductVersion 有时会带 "+commit" 之类的后缀，只保留主体版本号
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

        private static byte[] ReadAllBytes(Stream stream)
        {
            using (var buffer = new MemoryStream())
            {
                var chunk = new byte[81920];
                while (true)
                {
                    var read = stream.Read(chunk, 0, chunk.Length);
                    if (read <= 0)
                        break;

                    buffer.Write(chunk, 0, read);
                }

                return buffer.ToArray();
            }
        }

        private static void TryDelete(string path)
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
    }
}
