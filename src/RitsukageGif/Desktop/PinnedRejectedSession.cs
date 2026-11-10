using System.Buffers;
using System.Text;
using System.Text.Json;

namespace RitsukageGif.Desktop
{
    internal sealed class PinnedRejectedSession
    {
        private readonly Dictionary<string, (string Manifest, JsonElement Record)> _records =
            new(StringComparer.Ordinal);

        private bool _dirty;
        internal HashSet<string> ImageIds { get; } = new(StringComparer.Ordinal);
        internal HashSet<string> AnnotationImageIds { get; } = new(StringComparer.Ordinal);

        internal static async Task<PinnedRejectedSession> ReadAsync(string directory, long maximumBytes,
            CancellationToken token)
        {
            var result = new PinnedRejectedSession();
            var path = Path.Combine(directory, "rejected-session.json");
            if (!File.Exists(path)) return result;
            if (new FileInfo(path).Length > maximumBytes)
                throw new InvalidDataException("The rejected pin session metadata exceeds the size limit.");
            await using var stream = File.OpenRead(path);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: token);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("The rejected pin session metadata must be an array.");
            foreach (var item in document.RootElement.EnumerateArray())
            {
                var manifest = item.GetProperty("Manifest").GetString();
                if (manifest is not ("session.json" or "documents.json"))
                    throw new InvalidDataException("The rejected pin session metadata has an unknown manifest.");
                result.Add(manifest, item.GetProperty("Record"));
            }

            result._dirty = false;
            return result;
        }

        internal bool Add(string manifest, JsonElement record)
        {
            var buffer = new ArrayBufferWriter<byte>();
            using (var writer = new Utf8JsonWriter(buffer)) record.WriteTo(writer);
            var key = manifest + "\0" + Encoding.UTF8.GetString(buffer.WrittenSpan);
            if (_records.ContainsKey(key)) return false;
            _records.Add(key, (manifest, record.Clone()));
            _dirty = true;
            if (manifest == "session.json" && record.ValueKind == JsonValueKind.Object &&
                record.TryGetProperty("ImageId", out var imageId) && imageId.ValueKind == JsonValueKind.String &&
                imageId.GetString() is { } id && Guid.TryParseExact(id, "N", out _)) ImageIds.Add(id);
            if (manifest is "session.json" or "documents.json" && record.ValueKind == JsonValueKind.Object &&
                record.TryGetProperty(nameof(PinnedImageState.Annotations), out var annotations) &&
                annotations.ValueKind == JsonValueKind.Object &&
                annotations.TryGetProperty(nameof(ScreenshotAnnotationDocument.Layers), out var layers) &&
                layers.ValueKind == JsonValueKind.Array)
                foreach (var layer in layers.EnumerateArray())
                    if (layer.ValueKind == JsonValueKind.Object &&
                        layer.TryGetProperty(nameof(ScreenshotAnnotationRecord.ImageId), out var resource) &&
                        resource.ValueKind == JsonValueKind.String && resource.GetString() is { } image &&
                        Guid.TryParseExact(image, "N", out _))
                        AnnotationImageIds.Add(image);
            if (manifest == "session.json" && record.ValueKind == JsonValueKind.Object &&
                record.TryGetProperty(nameof(PinnedImageState.Translation), out var translation) &&
                translation.ValueKind == JsonValueKind.Object &&
                translation.TryGetProperty(nameof(PinnedImageTranslationState.Cache), out var cache) &&
                cache.ValueKind == JsonValueKind.Object &&
                cache.TryGetProperty(nameof(PinnedImageTranslationCache.ImageId), out var translationId) &&
                translationId.ValueKind == JsonValueKind.String && translationId.GetString() is { } translated &&
                Guid.TryParseExact(translated, "N", out _)) ImageIds.Add(translated);
            return true;
        }

        internal async Task WriteAsync(string directory, CancellationToken token)
        {
            if (!_dirty) return;
            Directory.CreateDirectory(directory);
            var target = Path.Combine(directory, "rejected-session.json");
            var temporary = target + ".tmp";
            try
            {
                await using (var stream = File.Create(temporary))
                {
                    await using var writer = new Utf8JsonWriter(stream, new() { Indented = true });
                    writer.WriteStartArray();
                    foreach (var (manifest, record) in _records.Values)
                    {
                        writer.WriteStartObject();
                        writer.WriteString("Manifest", manifest);
                        writer.WritePropertyName("Record");
                        record.WriteTo(writer);
                        writer.WriteEndObject();
                    }

                    writer.WriteEndArray();
                    await writer.FlushAsync(token);
                }

                token.ThrowIfCancellationRequested();
                File.Move(temporary, target, true);
                _dirty = false;
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }
    }
}
