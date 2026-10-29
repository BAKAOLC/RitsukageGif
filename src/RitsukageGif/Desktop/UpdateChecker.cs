using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace RitsukageGif.Desktop
{
    internal static class UpdateChecker
    {
        private static readonly ILogger UpdateServiceLogger = LogHub.GetLogger("Desktop.Updates.Service");
        private static readonly Version CurrentVersion = ApplicationVersion.Numeric;

        private static readonly HttpClient Client = CreateClient();

        internal static void OpenDownload(string downloadUrl)
        {
            UpdateServiceLogger.ZLogDebug($"Update download handoff requested.");
            try
            {
                Process.Start(new ProcessStartInfo(downloadUrl) { UseShellExecute = true })?.Dispose();
                UpdateServiceLogger.ZLogInformation($"Update download handed off to the system.");
            }
            catch (Exception error)
            {
                UpdateServiceLogger.ZLogError(error, $"Update download handoff failed.");
                throw;
            }
        }

        internal static async Task<UpdateRelease?> CheckChannelAsync(UpdateChannel channel,
            CancellationToken cancellationToken)
        {
            UpdateServiceLogger.ZLogInformation($"Update check started.");
            try
            {
                using var response = await Client.GetAsync(
                        channel == UpdateChannel.Preview
                            ? "https://api.github.com/repos/BAKAOLC/RitsukageGif/releases?per_page=30"
                            : "https://api.github.com/repos/BAKAOLC/RitsukageGif/releases/latest", cancellationToken)
                    .ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                var architecture = RuntimeInformation.ProcessArchitecture;
                var rid = OperatingSystem.IsWindows() && architecture == Architecture.X64 ? "win-x64" :
                    OperatingSystem.IsMacOS() && architecture == Architecture.Arm64 ? "osx-arm64" :
                    OperatingSystem.IsLinux() && architecture == Architecture.X64 ? "linux-x64" :
                    OperatingSystem.IsLinux() && architecture == Architecture.Arm64 ? "linux-arm64" : "";
                var release = channel == UpdateChannel.Preview
                    ? ParsePreview(json, CurrentVersion, rid)
                    : Parse(json, CurrentVersion, rid);
                UpdateServiceLogger.ZLogInformation($"Update check completed. UpdateAvailable={release is not null}");
                return release;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                UpdateServiceLogger.ZLogDebug($"Update check cancelled.");
                throw;
            }
            catch (Exception error)
            {
                UpdateServiceLogger.ZLogWarning(error, $"Update check failed.");
                throw;
            }
        }

        private static UpdateRelease? Parse(string json, Version current, string rid)
        {
            using var document = JsonDocument.Parse(json, new() { MaxDepth = 32 });
            return ParseRelease(document.RootElement, current, rid, false);
        }

        private static UpdateRelease? ParsePreview(string json, Version current, string rid)
        {
            using var document = JsonDocument.Parse(json, new() { MaxDepth = 32 });
            if (document.RootElement.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("Invalid release response.");
            UpdateRelease? latest = null;
            foreach (var item in document.RootElement.EnumerateArray())
            {
                var candidate = ParseRelease(item, current, rid, true);
                if (candidate is not null && (latest is null || candidate.Version > latest.Version)) latest = candidate;
            }

            return latest;
        }

        private static UpdateRelease? ParseRelease(JsonElement release, Version current, string rid, bool preview)
        {
            if (release.ValueKind != JsonValueKind.Object ||
                !release.TryGetProperty("draft", out var draft) ||
                !release.TryGetProperty("prerelease", out var prerelease) ||
                draft.ValueKind is not (JsonValueKind.True or JsonValueKind.False) ||
                prerelease.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                throw new InvalidDataException("Invalid release response.");
            if (draft.GetBoolean() || (!preview && prerelease.GetBoolean())) return null;
            var tag = ReadString(release, "tag_name");
            var numericTag = tag;
            if (preview && tag?.IndexOf('-') is > 0 and var separator)
            {
                if (tag[(separator + 1)..].Length == 0 ||
                    tag[(separator + 1)..].Any(character =>
                        !char.IsAsciiLetterOrDigit(character) && character is not ('.' or '-')))
                    throw new InvalidDataException("Invalid release version.");
                numericTag = tag[..separator];
            }

            if (numericTag is null || !TryVersion(numericTag, out var version))
            {
                if (preview) return null;
                throw new InvalidDataException("Invalid release version.");
            }

            var normalizedCurrent = new Version(current.Major, current.Minor, Math.Max(0, current.Build),
                Math.Max(0, current.Revision));
            if (version <= normalizedCurrent) return null;

            var page = ReadString(release, "html_url");
            if (!IsReleaseUrl(page)) page = $"https://github.com/BAKAOLC/RitsukageGif/releases/tag/{tag}";
            var suffix = rid switch
            {
                "win-x64" or "osx-arm64" => $"{rid}.zip",
                "linux-x64" or "linux-arm64" => $"{rid}.tar.gz",
                _ => null,
            };
            if (suffix is not null && release.TryGetProperty("assets", out var assets) &&
                assets.ValueKind == JsonValueKind.Array)
                foreach (var asset in assets.EnumerateArray())
                {
                    var name = ReadString(asset, "name");
                    if (name != $"RitsukageGif-{tag![1..]}-{suffix}" &&
                        name != $"RitsukageGif-{version.ToString(4)}-{suffix}") continue;
                    var url = ReadString(asset, "browser_download_url");
                    if (IsReleaseUrl(url)) return new(version, url!);
                }

            return new(version, page!);
        }

        private static bool TryVersion(string tag, out Version version)
        {
            version = new(0, 0, 0, 0);
            if (!tag.StartsWith('v')) return false;
            var parts = tag[1..].Split('.');
            if (parts.Length is not (3 or 4)) return false;
            Span<int> numbers = stackalloc int[4];
            numbers.Clear();
            for (var index = 0; index < parts.Length; index++)
                if (!int.TryParse(parts[index], NumberStyles.None, CultureInfo.InvariantCulture, out numbers[index]))
                    return false;
            version = new(numbers[0], numbers[1], numbers[2], numbers[3]);
            return true;
        }

        private static string? ReadString(JsonElement value, string property)
        {
            return value.ValueKind == JsonValueKind.Object && value.TryGetProperty(property, out var item) &&
                   item.ValueKind == JsonValueKind.String
                ? item.GetString()
                : null;
        }

        private static bool IsReleaseUrl(string? value)
        {
            return Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps &&
                   uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) &&
                   uri is { Port: 443, UserInfo.Length: 0 } && uri.AbsolutePath.StartsWith(
                       "/BAKAOLC/RitsukageGif/releases/",
                       StringComparison.Ordinal);
        }

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
