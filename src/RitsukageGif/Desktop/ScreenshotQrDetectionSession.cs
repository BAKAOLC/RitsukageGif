namespace RitsukageGif.Desktop
{
    internal sealed class ScreenshotQrDetectionSession : IDisposable
    {
        private readonly Action<Action> _post;
        private readonly Action<Exception> _reportError;
        private bool _disposed;
        private int _revision;
        private object? _source;
        private CancellationTokenSource? _work;

        internal ScreenshotQrDetectionSession(
            Action<Action> post, Action<Exception> reportError)
        {
            _post = post;
            _reportError = reportError;
        }

        internal QrDetectionSnapshot? Snapshot { get; private set; }
        internal PixelRect RequestedRegion { get; private set; }
        internal Task Pending { get; private set; } = Task.CompletedTask;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Clear();
            Changed = null;
        }

        internal event Action? Changed;

        internal void Update(object source, PixelRect region,
            Func<CancellationToken, Task<IReadOnlyList<BarcodeRecognitionResult>>> scan,
            CancellationToken token)
        {
            if (_disposed || region.IsEmpty) return;
            if (ReferenceEquals(_source, source) && RequestedRegion == region) return;
            Cancel();
            _source = source;
            RequestedRegion = region;
            Snapshot = null;
            var work = CancellationTokenSource.CreateLinkedTokenSource(token);
            _work = work;
            var revision = ++_revision;
            Pending = DecodeAsync(region, scan, work, revision);
        }

        internal void Clear()
        {
            Cancel();
            _source = null;
            RequestedRegion = default;
            Snapshot = null;
        }

        private void Cancel()
        {
            _revision++;
            _work?.Cancel();
            _work = null;
        }

        private async Task DecodeAsync(PixelRect region,
            Func<CancellationToken, Task<IReadOnlyList<BarcodeRecognitionResult>>> scan,
            CancellationTokenSource work, int revision)
        {
            try
            {
                var codes = await scan(work.Token).ConfigureAwait(false);
                work.Token.ThrowIfCancellationRequested();
                _post(() =>
                {
                    if (_disposed || revision != _revision || work.IsCancellationRequested) return;
                    Snapshot = new(region, codes);
                    Changed?.Invoke();
                });
            }
            catch (OperationCanceledException) when (work.IsCancellationRequested)
            {
            }
            catch (Exception error)
            {
                _post(() =>
                {
                    if (!_disposed && revision == _revision) _reportError(error);
                });
            }
            finally
            {
                _post(() =>
                {
                    if (ReferenceEquals(_work, work)) _work = null;
                    work.Dispose();
                });
            }
        }
    }
}
