using System.ComponentModel;
using System.Text;

namespace RitsukageGif.Desktop
{
    internal sealed class TesseractOcrBackend(string executable = "tesseract", string? workerRoot = null)
        : IPlatformOcrBackend
    {
        private readonly SemaphoreSlim _probeGate = new(1);
        private IReadOnlyList<OcrLanguageChoice> _languages = [];

        public string Id => "tesseract";

        public async Task<OcrCapabilities> GetCapabilitiesAsync(CancellationToken token)
        {
            await _probeGate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                OcrProcessResult result;
                try
                {
                    result = await OcrProcessRunner.RunAsync(executable, ["--list-langs"], null, token)
                        .ConfigureAwait(false);
                }
                catch (Win32Exception error) when (error.NativeErrorCode is 2 or 3)
                {
                    return new(false, 0, [],
                        "Tesseract OCR is not installed. Install tesseract-ocr and the required language data.");
                }
                catch (Win32Exception error)
                {
                    return new(false, 0, [], "Tesseract OCR could not start: " + error.Message);
                }

                if (result.ExitCode != 0)
                    return new(false, 0, [], "Tesseract language discovery failed: " + result.Error.Trim());
                _languages = OcrLanguageSelection.TesseractLanguages(result.Output);
                return new(_languages.Count != 0, 32768,
                    Array.AsReadOnly([.. _languages.Select(language => language.Language)]),
                    _languages.Count == 0 ? "No Tesseract OCR recognition language data is installed." : null);
            }
            finally
            {
                _probeGate.Release();
            }
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
            using var directory = OwnedWorkerDirectory.Create("ocr", workerRoot);
            var output = Path.Combine(directory.DirectoryPath, "recognized");
            var process = await OcrProcessRunner.RunAsync(executable,
                ["stdin", output, "-l", language.EngineId, "tsv", "txt"],
                (stream, cancellation) => OcrProcessRunner.WritePpmAsync(stream, pixels, width, height, cancellation),
                token).ConfigureAwait(false);
            if (process.ExitCode != 0)
                throw new IOException("Tesseract OCR failed: " + process.Error.Trim());
            var tsv = await File.ReadAllTextAsync(output + ".tsv", Encoding.UTF8, token).ConfigureAwait(false);
            var text = await File.ReadAllTextAsync(output + ".txt", Encoding.UTF8, token).ConfigureAwait(false);
            return TesseractOcrTsv.Parse(tsv, text, language.Language.Tag, width, height);
        }
    }
}
