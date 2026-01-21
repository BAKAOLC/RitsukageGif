using System;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;

namespace RitsukageGif.Class
{
    internal partial class Updater
    {
        private static readonly HttpClient HttpClient = new()
        {
            DefaultRequestHeaders =
            {
                UserAgent = { ProductInfoHeaderValue.Parse(UserAgent.Default) },
            },
        };

        private static string ProjectUrl => Assembly.GetExecutingAssembly()
            .GetCustomAttributes(false)
            .OfType<AssemblyMetadataAttribute>()
            .FirstOrDefault(x => x.Key == "RepositoryUrl")?.Value;

        private static Version CurrentVersion => Assembly.GetExecutingAssembly().GetName().Version;

        public static async Task CheckUpdateAsync()
        {
            if (!Settings.Default.CheckForUpdates)
                return;

            var match = GitHubUrlRegex().Match(ProjectUrl);
            if (!match.Success)
                return;

            var user = match.Groups["user"].Value;
            var repo = match.Groups["repo"].Value;
            var url = $"https://api.github.com/repos/{user}/{repo}/releases/latest";

            try
            {
                var release = await HttpClient.GetFromJsonAsync<GitHubRelease>(url).ConfigureAwait(false);
                if (release is null || string.IsNullOrEmpty(release.TagName))
                    return;

                var versionMatch = VersionRegex().Match(release.TagName);
                if (!versionMatch.Success)
                    return;

                var versionString = versionMatch.Groups["version"].Value;
                if (!Version.TryParse(versionString, out var gitVersion))
                    return;

                if (CurrentVersion >= gitVersion)
                    return;

                var downloadUrl = release.Assets?.FirstOrDefault()?.BrowserDownloadUrl
                                  ?? release.HtmlUrl;

                if (string.IsNullOrEmpty(downloadUrl))
                    return;

                var result = MessageBox.Show(
                    $"检测到新版本 {versionString}，是否下载？\n\n当前版本：{CurrentVersion}\n最新版本：{versionString}",
                    "更新提示",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Information);

                if (result == MessageBoxResult.Yes)
                    Process.Start(new ProcessStartInfo(downloadUrl) { UseShellExecute = true });
            }
            catch
            {
                // 忽略更新检查错误
            }
        }

        [GeneratedRegex(@"github\.com/(?<user>[^/]+)/(?<repo>[^/]+)", RegexOptions.IgnoreCase)]
        private static partial Regex GitHubUrlRegex();

        [GeneratedRegex(@"^v?(?<version>[\d.]+)", RegexOptions.IgnoreCase)]
        private static partial Regex VersionRegex();

        private record GitHubRelease
        {
            [JsonPropertyName("tag_name")] public string TagName { get; init; }

            [JsonPropertyName("html_url")] public string HtmlUrl { get; init; }

            [JsonPropertyName("assets")] public GitHubAsset[] Assets { get; init; }
        }

        private record GitHubAsset
        {
            [JsonPropertyName("browser_download_url")]
            public string BrowserDownloadUrl { get; init; }
        }

        internal class UserAgent
        {
            private const string Unknown = "unknown";
            private const string TagMozilla = "Mozilla/5.0";
            private const string TagChrome = "Chrome/114.0.0.0";
            private const string TagSafari = "Safari/537.36";
            private const string TagEdge = "Edg/114.0.1823.43";

            public const string TagMozillaWindows = $"{TagMozilla} (Windows NT 10.0; Win64; x64)";

            public const string TagMozillaAndroid = $"{TagMozilla} (Linux; Android 12)";

            public const string TagMozillaLinux = $"{TagMozilla} (X11; Linux x86_64)";

            public const string TagMozillaMac = $"{TagMozilla} (Macintosh; Intel Mac OS X 10_15_7)";

            // ReSharper disable once InconsistentNaming
            public const string TagMozillaIOS = $"{TagMozilla} (iPhone; CPU iPhone OS 15_0 like Mac OS X)";

            public const string TagAppleWebKit = "AppleWebKit/537.36 (KHTML, like Gecko)";

            private static readonly string AssemblyAuthor = typeof(UserAgent).Assembly
                .GetCustomAttributes(false)
                .OfType<AssemblyCompanyAttribute>()
                .FirstOrDefault()?.Company ?? Unknown;

            private static readonly string AssemblyName = typeof(UserAgent).Assembly.GetName().Name ?? Unknown;

            private static readonly string AssemblyVersion =
                typeof(UserAgent).Assembly.GetName().Version?.ToString() ?? Unknown;

            private static readonly string AssemblyRepositoryUrl = typeof(UserAgent).Assembly
                .GetCustomAttributes(false)
                .OfType<AssemblyMetadataAttribute>()
                .FirstOrDefault(x => x.Key == "RepositoryUrl")?.Value ?? Unknown;

            public static readonly string AssemblyUserAgent =
                $"{AssemblyAuthor}/{AssemblyName}/{AssemblyVersion}{(AssemblyRepositoryUrl == Unknown ? string.Empty : $" ({AssemblyRepositoryUrl})")}";

            public static readonly string Windows =
                $"{TagMozillaWindows} {TagAppleWebKit} {TagChrome} {TagSafari} {TagEdge} {AssemblyUserAgent}";

            public static readonly string Android =
                $"{TagMozillaAndroid} {TagAppleWebKit} {TagChrome} {TagSafari} {TagEdge} {AssemblyUserAgent}";

            public static readonly string Linux =
                $"{TagMozillaLinux} {TagAppleWebKit} {TagChrome} {TagSafari} {TagEdge} {AssemblyUserAgent}";

            public static readonly string Mac =
                $"{TagMozillaMac} {TagAppleWebKit} {TagChrome} {TagSafari} {TagEdge} {AssemblyUserAgent}";

            // ReSharper disable once InconsistentNaming
            public static readonly string IOS =
                $"{TagMozillaIOS} {TagAppleWebKit} {TagChrome} {TagSafari} {TagEdge} {AssemblyUserAgent}";

            public static string Default => Environment.OSVersion.Platform switch
            {
                PlatformID.Win32NT => Windows,
                PlatformID.Unix => Linux,
                PlatformID.MacOSX => Mac,
                _ => Windows,
            };
        }
    }
}