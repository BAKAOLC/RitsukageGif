using System;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows;

namespace RitsukageGif
{
    internal class Updater
    {
        internal class UserAgent
        {
            private static readonly string Unknown = "unknown";
            private static readonly string TagMozilla = "Mozilla/5.0";

            public static readonly string TagMozillaWindows = $"{TagMozilla} (Windows NT 10.0; Win64; x64)";

            public static readonly string TagMozillaAndroid = $"{TagMozilla} (Linux; Android 12)";

            public static readonly string TagMozillaLinux = $"{TagMozilla} (X11; Linux x86_64)";

            public static readonly string TagMozillaMac = $"{TagMozilla} (Macintosh; Intel Mac OS X 10_15_7)";

            public static readonly string TagMozillaIOS = $"{TagMozilla} (iPhone; CPU iPhone OS 15_0 like Mac OS X)";

            public static readonly string TagAppleWebKit = "AppleWebKit/537.36 (KHTML, like Gecko)";

            public static readonly string TagChrome = "Chrome/114.0.0.0";

            public static readonly string TagSafari = "Safari/537.36";

            public static readonly string TagEdge = "Edg/114.0.1823.43";

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

            public static readonly string IOS =
                $"{TagMozillaIOS} {TagAppleWebKit} {TagChrome} {TagSafari} {TagEdge} {AssemblyUserAgent}";

            public static string Default
            {
                get
                {
                    switch (Environment.OSVersion.Platform)
                    {
                        case PlatformID.Win32NT:
                            return Windows;
                        case PlatformID.Unix:
                            return Linux;
                        case PlatformID.MacOSX:
                            return Mac;
                        case PlatformID.Win32S:
                        case PlatformID.Win32Windows:
                        case PlatformID.WinCE:
                        case PlatformID.Xbox:
                        default:
                            return Windows;
                    }
                }
            }
        }

        private static string ProjectUrl => Assembly.GetExecutingAssembly()
            .GetCustomAttributes(false)
            .OfType<AssemblyMetadataAttribute>()
            .FirstOrDefault(x => x.Key == "RepositoryUrl")?.Value;

        private static Version CurrentVersion => Assembly.GetExecutingAssembly().GetName().Version;

        public static void CheckUpdate()
        {
            var match = Regex.Match(ProjectUrl, @"github\.com/(?<user>[^/]+)/(?<repo>[^/]+)");
            if (!match.Success)
            {
                //不是github的项目
                return;
            }

            var user = match.Groups["user"].Value;
            var repo = match.Groups["repo"].Value;
            var url = $"https://api.github.com/repos/{user}/{repo}/releases/latest";
            using (var wc = new WebClient())
            {
                wc.Headers.Add("User-Agent", UserAgent.Default);
                wc.DownloadStringCompleted += (sender, args) =>
                {
                    if (args.Error != null)
                    {
                        return;
                    }

                    var json = args.Result;
                    var versionMatch = Regex.Match(json, @"""tag_name"":\s*""v(?<version>[^""]+)""", RegexOptions.IgnoreCase).Groups["version"];
                    if (!versionMatch.Success) return;
                    var version = versionMatch.Value;
                    if (!Version.TryParse(version, out var gitVersion)) return;
                    if (CurrentVersion >= gitVersion) return;
                    var downloadUrl = Regex.Match(json, @"""browser_download_url"":\s*""(?<url>[^""]+)""")
                        .Groups["url"].Value;
                    if (MessageBox.Show($"检测到新版本{version}，是否下载？", "更新", MessageBoxButton.YesNo) ==
                        MessageBoxResult.Yes)
                    {
                        Process.Start(downloadUrl);
                    }
                };
                wc.DownloadStringAsync(new Uri(url));
            }
        }
    }
}