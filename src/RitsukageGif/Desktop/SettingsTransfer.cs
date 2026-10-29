using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RitsukageGif.Desktop
{
    internal static partial class SettingsTransfer
    {
        private const int MaximumBytes = 4 * 1024 * 1024;

        private static readonly SettingsJsonContext StrictContext = new(new()
        {
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            RespectNullableAnnotations = true,
            WriteIndented = true,
            MaxDepth = 64,
        });

        internal static async Task<AppSettings> ImportAsync(Stream input, CancellationToken token = default)
        {
            using var buffer = new MemoryStream();
            var chunk = new byte[16384];
            while (true)
            {
                var count = await input.ReadAsync(chunk, token).ConfigureAwait(false);
                if (count == 0) break;
                if (buffer.Length + count > MaximumBytes)
                    throw new InvalidDataException(UiText.Get("SettingsTransferTooLarge"));
                await buffer.WriteAsync(chunk.AsMemory(0, count), token).ConfigureAwait(false);
            }

            token.ThrowIfCancellationRequested();
            var data = buffer.ToArray();
            return await Task.Run(() => Import(data, token), token).ConfigureAwait(false);
        }

        internal static async Task ExportAsync(string path, AppSettings settings, CancellationToken token = default)
        {
            Validate(settings);
            var bytes = JsonSerializer.SerializeToUtf8Bytes(settings, SettingsJsonContext.Default.AppSettings);
            if (bytes.Length > MaximumBytes) throw new InvalidDataException(UiText.Get("SettingsTransferTooLarge"));
            path = Path.GetFullPath(path);
            var directory = Path.GetDirectoryName(path)!;
            var temporary = Path.Combine(directory,
                "." + Path.GetFileName(path) + "." + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                token.ThrowIfCancellationRequested();
                await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write,
                                 FileShare.None,
                                 16384, true))
                    await output.WriteAsync(bytes, token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                File.Move(temporary, path, true);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }

        private static AppSettings Import(byte[] data, CancellationToken token)
        {
            using var reader = new StreamReader(new MemoryStream(data, false), new UTF8Encoding(false, true), true);
            var text = reader.ReadToEnd();
            token.ThrowIfCancellationRequested();
            using var document = JsonDocument.Parse(text, new() { MaxDepth = 64 });
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty(nameof(AppSettings.Fps), out _) ||
                !document.RootElement.TryGetProperty(nameof(AppSettings.OutputDirectory), out _))
                throw Invalid("settings");
            ValidateJson(document.RootElement, "");
            ValidateAnnotationPreferences(document.RootElement);
            var settings = JsonSerializer.Deserialize(text, StrictContext.AppSettings) ?? throw Invalid("settings");

            Validate(settings);
            token.ThrowIfCancellationRequested();
            return settings;
        }

        private static ArgumentException Invalid(string field)
        {
            return new(UiText.Get("SettingsTransferInvalid") + ": " + field);
        }
    }
}
