using System.Text.Json;

namespace RitsukageGif.Desktop
{
    internal static class PinnedSessionRecovery
    {
        internal static async Task<string?> PreserveUnsupportedAsync(string directory, long maximumBytes,
            CancellationToken token)
        {
            if (!Directory.Exists(directory)) return null;
            var unsupported = await UnsupportedManifestAsync(Path.Combine(directory, "session.json"), false,
                maximumBytes, token) || await UnsupportedManifestAsync(Path.Combine(directory, "documents.json"),
                true, maximumBytes, token);
            if (!unsupported) return null;
            token.ThrowIfCancellationRequested();
            var recovery = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory)) + ".recovery";
            if (Directory.Exists(recovery) || File.Exists(recovery))
                throw new IOException($"The pin recovery destination already exists: {recovery}");
            Directory.Move(directory, recovery);
            Directory.CreateDirectory(directory);
            return recovery;
        }

        private static async Task<bool> UnsupportedManifestAsync(string path, bool documents, long maximumBytes,
            CancellationToken token)
        {
            if (!File.Exists(path)) return false;
            if (new FileInfo(path).Length > maximumBytes) return true;
            try
            {
                await using var stream = File.OpenRead(path);
                using var manifest = await JsonDocument.ParseAsync(stream, cancellationToken: token);
                if (manifest.RootElement.ValueKind != JsonValueKind.Array) return true;
                foreach (var item in manifest.RootElement.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.Object) continue;
                    var state = item;
                    if (documents && (!item.TryGetProperty("Window", out state) ||
                                      state.ValueKind != JsonValueKind.Object)) continue;
                    if (!state.TryGetProperty("Group", out var group) || group.ValueKind != JsonValueKind.String)
                        continue;
                    var id = group.GetString();
                    if (id is { Length: > 0 } && !Guid.TryParseExact(id, "N", out _)) return true;
                }

                return false;
            }
            catch (JsonException)
            {
                return true;
            }
        }
    }
}
