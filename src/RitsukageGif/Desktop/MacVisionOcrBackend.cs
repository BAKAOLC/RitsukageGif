using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;

namespace RitsukageGif.Desktop
{
    internal sealed class MacVisionOcrBackend(string? executable = null) : IPlatformOcrBackend
    {
        private readonly string _executable = executable ?? Path.Combine(AppContext.BaseDirectory, "RitsukageGif.Ocr");
        private ReadOnlyCollection<OcrLanguageChoice> _languages = new([]);

        public string Id => "vision";

        public async Task<OcrCapabilities> GetCapabilitiesAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (!File.Exists(_executable))
                return new(false, 0, [], "The macOS OCR helper is missing from the application package.");
            var result = await OcrProcessRunner.RunAsync(_executable, ["capabilities"], null, token)
                .ConfigureAwait(false);
            if (result.ExitCode != 0)
                return new(false, 0, [], "Vision OCR language discovery failed: " + result.Error.Trim());
            var capabilities = JsonSerializer.Deserialize(result.Output,
                                   PlatformOcrJsonContext.Default.MacVisionOcrCapabilities)
                               ?? throw new InvalidDataException("The Vision OCR helper returned no capabilities.");
            _languages = Array.AsReadOnly([
                .. capabilities.Languages
                    .Select(tag => new OcrLanguageChoice(tag, OcrLanguageSelection.Describe(tag))),
            ]);
            return new(_languages.Count != 0, capabilities.MaxImageDimension,
                Array.AsReadOnly([.. _languages.Select(language => language.Language)]),
                _languages.Count == 0 ? "No Vision OCR recognition language is available." : null);
        }

        public async Task<OcrRecognitionResult> RecognizeAsync(byte[] pixels, int width, int height,
            string? languageTag, CancellationToken token)
        {
            if (_languages.Count == 0)
            {
                var capabilities = await GetCapabilitiesAsync(token).ConfigureAwait(false);
                if (!capabilities.Available) throw new NotSupportedException(capabilities.FailureReason);
            }

            var language = OcrLanguageSelection.Resolve(_languages, languageTag);
            var process = await OcrProcessRunner.RunAsync(_executable,
                [
                    "recognize", width.ToString(CultureInfo.InvariantCulture),
                    height.ToString(CultureInfo.InvariantCulture),
                    language.EngineId,
                ],
                async (stream, cancellation) => await stream.WriteAsync(pixels, cancellation).ConfigureAwait(false),
                token).ConfigureAwait(false);
            if (process.ExitCode != 0)
                throw new IOException("Vision OCR failed: " + process.Error.Trim());
            var result = JsonSerializer.Deserialize(process.Output,
                             PlatformOcrJsonContext.Default.OcrRecognitionResult)
                         ?? throw new InvalidDataException("The Vision OCR helper returned no recognition result.");
            if (result.Width != width || result.Height != height)
                throw new InvalidDataException("The Vision OCR helper returned mismatched image dimensions.");
            return result;
        }
    }
}
