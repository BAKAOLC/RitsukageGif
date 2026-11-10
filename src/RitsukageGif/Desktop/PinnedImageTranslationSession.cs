namespace RitsukageGif.Desktop
{
    internal sealed class PinnedImageTranslationSession<T> : IDisposable where T : class
    {
        private readonly CancellationToken _lifetime;
        private readonly Func<T, string> _targetLanguage;
        private CancellationTokenSource? _work;
        private long _request;
        private bool _disposed;

        internal PinnedImageTranslationSession(Func<T, string> targetLanguage, CancellationToken lifetime)
        {
            _lifetime = lifetime;
            _targetLanguage = targetLanguage;
        }

        internal string TargetLanguage { get; private set; } = "";
        internal bool Enabled { get; private set; }
        internal T? Cached { get; private set; }
        internal PinnedImageTranslationStatus Status { get; private set; }
        internal T? Displayed => Enabled ? Cached : null;

        public void Dispose()
        {
            _disposed = true;
            CancelWork();
        }

        internal event Action? Changed;
        internal event Action<Exception>? Failed;

        internal void Disable()
        {
            Enabled = false;
            Changed?.Invoke();
        }

        internal void Enable(string target, Func<string, CancellationToken, Task<T?>> translate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (target != TargetLanguage)
            {
                CancelWork();
                TargetLanguage = target;
                Cached = null;
                Status = PinnedImageTranslationStatus.None;
            }

            if (Status == PinnedImageTranslationStatus.Empty)
            {
                Enabled = false;
                Changed?.Invoke();
                return;
            }

            Enabled = true;
            if (Status == PinnedImageTranslationStatus.None)
            {
                var cancellation = _work = CancellationTokenSource.CreateLinkedTokenSource(_lifetime);
                var request = ++_request;
                Status = PinnedImageTranslationStatus.Pending;
                Changed?.Invoke();
                _ = TranslateAsync(request, target, translate, cancellation);
            }
            else
            {
                Changed?.Invoke();
            }
        }

        internal void Restore(string target, bool enabled, T? cached, bool empty)
        {
            CancelWork();
            TargetLanguage = target;
            Cached = cached;
            Status = cached is not null ? PinnedImageTranslationStatus.Ready :
                empty ? PinnedImageTranslationStatus.Empty : PinnedImageTranslationStatus.None;
            Enabled = enabled && cached is not null;
            Changed?.Invoke();
        }

        private async Task TranslateAsync(long request, string target,
            Func<string, CancellationToken, Task<T?>> translate, CancellationTokenSource cancellation)
        {
            try
            {
                var result = await translate(target, cancellation.Token);
                if (_disposed || request != _request || cancellation.IsCancellationRequested) return;
                Cached = result;
                if (result is not null) TargetLanguage = _targetLanguage(result);
                Status = result is null ? PinnedImageTranslationStatus.Empty : PinnedImageTranslationStatus.Ready;
                if (result is null) Enabled = false;
                Changed?.Invoke();
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
            }
            catch (Exception error)
            {
                if (_disposed || request != _request) return;
                Cached = null;
                Status = PinnedImageTranslationStatus.None;
                Enabled = false;
                Changed?.Invoke();
                Failed?.Invoke(error);
            }
            finally
            {
                if (ReferenceEquals(_work, cancellation)) _work = null;
                cancellation.Dispose();
            }
        }

        private void CancelWork()
        {
            _request++;
            _work?.Cancel();
            _work = null;
        }
    }
}
