namespace RitsukageGif.Core
{
    public sealed class OnnxFormulaRecognitionService(StructuredRecognitionModelPaths paths)
        : IFormulaRecognitionService, IAsyncDisposable
    {
        private static readonly ILogger Logger = LogHub.GetLogger("Recognition.Formula");
        private readonly SemaphoreSlim _gate = new(1);
        private OnnxFormulaRegionDetector? _detector;
        private bool _disposed;
        private OnnxFormulaRecognizer? _recognizer;
        private OnnxRuntimeLibrary? _runtime;

        public async ValueTask DisposeAsync()
        {
            await _gate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_disposed) return;
                _disposed = true;
                _recognizer?.Dispose();
                _detector?.Dispose();
                _runtime?.Dispose();
            }
            finally
            {
                _gate.Release();
            }
        }

        public Task<RecognitionProviderCapabilities> GetCapabilitiesAsync(CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            return Task.FromResult(paths.Capabilities("encoder.onnx", "decoder.onnx", "image_resizer.onnx",
                "tokenizer.json", "pp_doclayout_s.onnx"));
        }

        public async Task<FormulaRecognitionResult> RecognizeAsync(ReadOnlyMemory<byte> encoded,
            CancellationToken token = default)
        {
            await _gate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                Logger.ZLogDebug($"Formula recognition started. InputBytes={encoded.Length}");
                var result = await Task.Run(() => Recognize(encoded, token), token).ConfigureAwait(false);
                Logger.ZLogInformation(
                    $"Formula recognition completed. Width={result.Width} Height={result.Height} Formulas={result.Formulas.Count} OutputCharacters={result.Formulas.Sum(formula => formula.Latex.Length)}");
                return result;
            }
            finally
            {
                _gate.Release();
            }
        }

        private FormulaRecognitionResult Recognize(ReadOnlyMemory<byte> encoded, CancellationToken token)
        {
            var capabilities = paths.Capabilities("encoder.onnx", "decoder.onnx", "image_resizer.onnx",
                "tokenizer.json", "pp_doclayout_s.onnx");
            if (!capabilities.Available) throw new NotSupportedException(capabilities.UnavailableReason);
            using var image = RecognitionImageInput.Decode(encoded);
            token.ThrowIfCancellationRequested();
            var gray = GrayImage.FromImage(image);
            if (gray.Pixels.Min() == gray.Pixels.Max()) return new(gray.Width, gray.Height, []);
            if (_runtime is null)
            {
                var runtime = new OnnxRuntimeLibrary(paths.RuntimeLibrary);
                try
                {
                    _recognizer = new(runtime, paths.Directory);
                    _detector = new(runtime, paths.Directory);
                    _runtime = runtime;
                }
                catch
                {
                    _recognizer?.Dispose();
                    _recognizer = null;
                    runtime.Dispose();
                    throw;
                }
            }

            var regions = _detector!.Detect(image, token);
            if (regions.Count == 0)
            {
                var latex = _recognizer!.Recognize(gray, token);
                return new(gray.Width, gray.Height, string.IsNullOrWhiteSpace(latex)
                    ? []
                    : [new(latex, new(0, 0, gray.Width, gray.Height))]);
            }

            var formulas = new List<RecognizedFormula>(regions.Count);
            foreach (var region in regions)
            {
                token.ThrowIfCancellationRequested();
                var crop = gray.Crop(region.X, region.Y, region.Width, region.Height);
                var latex = _recognizer!.Recognize(crop, token);
                if (!string.IsNullOrWhiteSpace(latex))
                    formulas.Add(new(latex, new(region.X, region.Y, region.Width, region.Height), region.Confidence));
            }

            return new(gray.Width, gray.Height, formulas);
        }
    }
}
