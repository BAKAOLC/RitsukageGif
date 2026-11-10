namespace RitsukageGif.Core
{
    public sealed class OnnxTableRecognitionService(StructuredRecognitionModelPaths paths)
        : ITableRecognitionService, IAsyncDisposable
    {
        private static readonly ILogger Logger = LogHub.GetLogger("Recognition.Table");
        private readonly SemaphoreSlim _gate = new(1);
        private bool _disposed;
        private OnnxRuntimeLibrary? _runtime;
        private OnnxTableStructureRecognizer? _structure;
        private OnnxCellTextRecognizer? _text;

        public async ValueTask DisposeAsync()
        {
            await _gate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_disposed) return;
                _disposed = true;
                _text?.Dispose();
                _structure?.Dispose();
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
            return Task.FromResult(GetModelCapabilities());
        }

        public async Task<TableRecognitionResult> RecognizeAsync(ReadOnlyMemory<byte> encoded,
            CancellationToken token = default)
        {
            await _gate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                Logger.ZLogDebug($"Table recognition started. InputBytes={encoded.Length}");
                var result = await Task.Run(() => Recognize(encoded, token), token).ConfigureAwait(false);
                Logger.ZLogInformation(
                    $"Table recognition completed. Width={result.Width} Height={result.Height} Rows={result.Rows} Columns={result.Columns} Cells={result.Cells.Count}");
                return result;
            }
            finally
            {
                _gate.Release();
            }
        }

        private RecognitionProviderCapabilities GetModelCapabilities()
        {
            return paths.Capabilities("slanet-plus.onnx", "PP-OCRv6_rec_small.onnx");
        }

        private TableRecognitionResult Recognize(ReadOnlyMemory<byte> encoded, CancellationToken token)
        {
            var capabilities = GetModelCapabilities();
            if (!capabilities.Available) throw new NotSupportedException(capabilities.UnavailableReason);
            using var image = RecognitionImageInput.Decode(encoded);
            token.ThrowIfCancellationRequested();
            EnsureEngine(token);
            var gray = GrayImage.FromImage(image);
            var cells = _structure!.Recognize(image, gray, token);
            _text!.Recognize(gray, cells, token);
            token.ThrowIfCancellationRequested();
            return new(cells.Max(cell => cell.Row + cell.RowSpan), cells.Max(cell => cell.Column + cell.ColumnSpan),
                gray.Width, gray.Height, [
                    .. cells.Select(cell => new TableCell(cell.Row, cell.Column, cell.RowSpan,
                        cell.ColumnSpan, cell.Text,
                        new((float)cell.X, (float)cell.Y, (float)cell.Width, (float)cell.Height))),
                ]);
        }

        private void EnsureEngine(CancellationToken token)
        {
            if (_runtime is not null) return;
            var runtime = new OnnxRuntimeLibrary(paths.RuntimeLibrary);
            OnnxTableStructureRecognizer? structure = null;
            try
            {
                structure = new(runtime, paths.Directory);
                token.ThrowIfCancellationRequested();
                var text = new OnnxCellTextRecognizer(runtime, paths.Directory);
                _runtime = runtime;
                _structure = structure;
                _text = text;
            }
            catch
            {
                structure?.Dispose();
                runtime.Dispose();
                throw;
            }
        }
    }
}
