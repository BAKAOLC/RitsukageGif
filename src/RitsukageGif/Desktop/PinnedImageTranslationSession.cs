namespace RitsukageGif.Desktop
{
    internal sealed class PinnedImageTranslationSession<T> : IDisposable where T : class
    {
        private readonly CancellationToken _lifetime;
        private readonly Func<T, string> _targetLanguage;
        private bool _disposed;
        private TaskCompletionSource<Exception?>? _pending;
        private long _request;
        private CancellationTokenSource? _work;

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
        internal Task<Exception?> Completion => _pending?.Task ?? Task.FromResult<Exception?>(null);

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
            var pending = _pending;
            _pending = null;
            Changed?.Invoke();
            pending?.TrySetResult(null);
        }

        internal Task<Exception?> Enable(string target, Func<string, CancellationToken, Task<T?>> translate)
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
                return Task.FromResult<Exception?>(null);
            }

            Enabled = true;
            if (Status == PinnedImageTranslationStatus.None)
            {
                var cancellation = _work = CancellationTokenSource.CreateLinkedTokenSource(_lifetime);
                var completion = _pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
                var request = ++_request;
                Status = PinnedImageTranslationStatus.Pending;
                Changed?.Invoke();
                _ = TranslateAsync(request, target, translate, cancellation);
                return completion.Task;
            }

            var pending = Status == PinnedImageTranslationStatus.Pending
                ? (_pending ??= new(TaskCreationOptions.RunContinuationsAsynchronously)).Task
                : Task.FromResult<Exception?>(null);
            Changed?.Invoke();
            return pending;
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
                var pending = _pending;
                Changed?.Invoke();
                pending?.TrySetResult(null);
            }
            catch (OperationCanceledException error) when (cancellation.IsCancellationRequested)
            {
                if (ReferenceEquals(_work, cancellation)) _pending?.TrySetResult(error);
            }
            catch (Exception error)
            {
                if (_disposed || request != _request) return;
                Cached = null;
                Status = PinnedImageTranslationStatus.None;
                Enabled = false;
                var pending = _pending;
                Changed?.Invoke();
                pending?.TrySetResult(error);
                Failed?.Invoke(error);
            }
            finally
            {
                if (ReferenceEquals(_work, cancellation))
                {
                    _work = null;
                    _pending = null;
                }

                cancellation.Dispose();
            }
        }

        private void CancelWork()
        {
            _request++;
            if (_work is not { } work) return;
            _work = null;
            var pending = _pending;
            _pending = null;
            var token = work.Token;
            pending?.TrySetResult(new OperationCanceledException(token));
            work.Cancel();
        }
    }
}
