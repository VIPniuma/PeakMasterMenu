using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;

namespace PeakMasterMenu.Installer
{
    /// <summary>
    /// Thunderstore 的 experimental API 返回的包信息。字段名和 JSON 一一对应，
    /// 只会用到我们关心的几个，其余字段反序列化时自动忽略。
    /// </summary>
    [DataContract]
    internal sealed class ThunderstorePackageDto
    {
        [DataMember(Name = "name")]
        public string Name { get; set; }

        [DataMember(Name = "full_name")]
        public string FullName { get; set; }

        [DataMember(Name = "version_number")]
        public string VersionNumber { get; set; }

        [DataMember(Name = "download_url")]
        public string DownloadUrl { get; set; }

        [DataMember(Name = "latest")]
        public ThunderstorePackageDto Latest { get; set; }
    }

    /// <summary>已经确定好下载地址的一个前置包。</summary>
    internal sealed class PackageRelease
    {
        public string NamespaceName { get; set; }

        public string PackageName { get; set; }

        public string VersionNumber { get; set; }

        public string DownloadUrl { get; set; }

        public string FullName
        {
            get { return NamespaceName + "-" + PackageName; }
        }

        public string DisplayText
        {
            get { return FullName + " " + VersionNumber; }
        }
    }

    /// <summary>下载进度。Total 为 -1 表示服务器没给 Content-Length。</summary>
    internal sealed class DownloadProgress
    {
        public long Received { get; set; }

        public long Total { get; set; }
    }

    /// <summary>
    /// 负责「自动补全前置」：查最新版本 → 拿下载地址 → 下载到本地临时文件。
    /// </summary>
    internal static class ThunderstoreClient
    {
        private const string ApiRoot = "https://thunderstore.io/api/experimental/package/";
        private const string DownloadUrlFormat = "https://thunderstore.io/package/download/{0}/{1}/{2}/";

        private static readonly HttpClient Client = CreateHttpClient();

        private static HttpClient CreateHttpClient()
        {
            // .NET Framework 4.8 默认不一定启用 TLS 1.2，显式打开，免得 HTTPS 请求直接失败
            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            }
            catch
            {
            }

            var handler = new HttpClientHandler
            {
                AllowAutoRedirect = true,
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
            };

            var client = new HttpClient(handler);
            // Thunderstore 要求请求带可识别的 User-Agent，否则可能被拒
            client.DefaultRequestHeaders.UserAgent.ParseAdd("PeakMasterMenuInstaller/1.0 (+https://thunderstore.io/c/peak/)");
            return client;
        }

        /// <summary>
        /// 查询某个包的最新版本。查询失败时退化为用 <paramref name="fallbackVersion"/>
        /// 直接拼官方下载地址，这样即使 API 抽风，只要版本号没变就还能装上。
        /// </summary>
        public static PackageRelease ResolveLatest(string namespaceName, string packageName, string fallbackVersion, CancellationToken token)
        {
            var release = new PackageRelease
            {
                NamespaceName = namespaceName,
                PackageName = packageName,
                VersionNumber = fallbackVersion,
            };

            try
            {
                var requestUri = ApiRoot
                    + Uri.EscapeDataString(namespaceName) + "/"
                    + Uri.EscapeDataString(packageName) + "/";

                string json;
                using (var response = Client.GetAsync(requestUri, token).GetAwaiter().GetResult())
                {
                    response.EnsureSuccessStatusCode();
                    json = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                }

                var dto = DeserializePackage(json);
                // 接口既可能直接返回最新版信息，也可能把它塞在 latest 里，两边都认
                var effective = dto != null && dto.Latest != null ? dto.Latest : dto;

                if (effective != null)
                {
                    if (!string.IsNullOrWhiteSpace(effective.VersionNumber))
                        release.VersionNumber = effective.VersionNumber.Trim();

                    if (!string.IsNullOrWhiteSpace(effective.DownloadUrl))
                        release.DownloadUrl = effective.DownloadUrl.Trim();
                }
            }
            catch (Exception)
            {
                // 故意吞掉：下面会用兜底地址继续
            }

            if (string.IsNullOrWhiteSpace(release.DownloadUrl))
            {
                release.DownloadUrl = string.Format(
                    DownloadUrlFormat,
                    Uri.EscapeDataString(namespaceName),
                    Uri.EscapeDataString(packageName),
                    Uri.EscapeDataString(release.VersionNumber));
            }

            return release;
        }

        /// <summary>把包下载到指定文件，过程中持续回报进度。</summary>
        public static void Download(string url, string destinationPath, IProgress<DownloadProgress> progress, CancellationToken token)
        {
            using (var response = Client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token).GetAwaiter().GetResult())
            {
                response.EnsureSuccessStatusCode();

                var total = response.Content.Headers.ContentLength ?? -1L;

                using (var source = response.Content.ReadAsStreamAsync().GetAwaiter().GetResult())
                using (var destination = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920))
                {
                    var buffer = new byte[81920];
                    long received = 0;

                    while (true)
                    {
                        token.ThrowIfCancellationRequested();

                        var read = source.Read(buffer, 0, buffer.Length);
                        if (read <= 0)
                            break;

                        destination.Write(buffer, 0, read);
                        received += read;

                        if (progress != null)
                            progress.Report(new DownloadProgress { Received = received, Total = total });
                    }

                    destination.Flush();
                }
            }
        }

        private static ThunderstorePackageDto DeserializePackage(string json)
        {
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
            {
                var serializer = new DataContractJsonSerializer(typeof(ThunderstorePackageDto));
                return (ThunderstorePackageDto)serializer.ReadObject(stream);
            }
        }
    }
}
