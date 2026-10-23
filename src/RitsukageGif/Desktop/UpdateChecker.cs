using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace RitsukageGif.Desktop
{
    internal static class UpdateChecker
    {
        private static readonly Version CurrentVersion =
            typeof(UpdateChecker).Assembly.GetName().Version ?? new Version(0, 0, 0, 0);

        private static readonly HttpClient Client = CreateClient();

        public static async Task<UpdateRelease?> CheckAsync(CancellationToken cancellationToken)
        {
            using var response = await Client.GetAsync(
                    "https://api.github.com/repos/BAKAOLC/RitsukageGif/releases/latest", cancellationToken)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var architecture = RuntimeInformation.ProcessArchitecture;
            var rid = OperatingSystem.IsWindows() && architecture == Architecture.X64 ? "win-x64" :
                OperatingSystem.IsMacOS() && architecture == Architecture.Arm64 ? "osx-arm64" :
                OperatingSystem.IsLinux() && architecture == Architecture.X64 ? "linux-x64" :
                OperatingSystem.IsLinux() && architecture == Architecture.Arm64 ? "linux-arm64" : "";
            return Parse(json, CurrentVersion, rid);
        }

        internal static UpdateRelease? Parse(string json, Version current, string rid)
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 32 });
            var release = document.RootElement;
            if (release.ValueKind != JsonValueKind.Object ||
                !release.TryGetProperty("draft", out var draft) ||
                !release.TryGetProperty("prerelease", out var prerelease) ||
                draft.ValueKind is not (JsonValueKind.True or JsonValueKind.False) ||
                prerelease.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                throw new InvalidDataException("Invalid release response.");
            if (draft.GetBoolean() || prerelease.GetBoolean()) return null;
            var tag = ReadString(release, "tag_name");
            if (tag is null || !TryVersion(tag, out var version))
                throw new InvalidDataException("Invalid release version.");
            var normalizedCurrent = new Version(current.Major, current.Minor, Math.Max(0, current.Build),
                Math.Max(0, current.Revision));
            if (version <= normalizedCurrent) return null;

            var page = ReadString(release, "html_url");
            if (!IsReleaseUrl(page)) page = $"https://github.com/BAKAOLC/RitsukageGif/releases/tag/{tag}";
            var suffix = rid switch
            {
                "win-x64" or "osx-arm64" => $"{rid}.zip",
                "linux-x64" or "linux-arm64" => $"{rid}.tar.gz",
                _ => null
            };
            if (suffix is not null && release.TryGetProperty("assets", out var assets) &&
                assets.ValueKind == JsonValueKind.Array)
                foreach (var asset in assets.EnumerateArray())
                {
                    var name = ReadString(asset, "name");
                    if (name != $"RitsukageGif-{tag[1..]}-{suffix}" &&
                        name != $"RitsukageGif-{version.ToString(4)}-{suffix}") continue;
                    var url = ReadString(asset, "browser_download_url");
                    if (IsReleaseUrl(url)) return new UpdateRelease(version, url!);
                }

            return new UpdateRelease(version, page!);
        }

        private static bool TryVersion(string tag, out Version version)
        {
            version = new Version(0, 0, 0, 0);
            if (!tag.StartsWith('v')) return false;
            var parts = tag[1..].Split('.');
            if (parts.Length is not (3 or 4)) return false;
            Span<int> numbers = stackalloc int[4];
            numbers.Clear();
            for (var index = 0; index < parts.Length; index++)
                if (!int.TryParse(parts[index], NumberStyles.None, CultureInfo.InvariantCulture, out numbers[index]))
                    return false;
            version = new Version(numbers[0], numbers[1], numbers[2], numbers[3]);
            return true;
        }

        private static string? ReadString(JsonElement value, string property) =>
            value.ValueKind == JsonValueKind.Object && value.TryGetProperty(property, out var item) &&
            item.ValueKind == JsonValueKind.String
                ? item.GetString()
                : null;

        private static bool IsReleaseUrl(string? value) =>
            Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps &&
            uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) && uri.Port == 443 &&
            uri.UserInfo.Length == 0 && uri.AbsolutePath.StartsWith("/BAKAOLC/RitsukageGif/releases/",
                StringComparison.Ordinal);

        private static HttpClient CreateClient()
        {
            var client = new HttpClient
                { Timeout = TimeSpan.FromSeconds(10), MaxResponseContentBufferSize = 2 * 1024 * 1024 };
            client.DefaultRequestHeaders.UserAgent.ParseAdd($"RitsukageGif/{CurrentVersion}");
            client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            return client;
        }
    }
}
