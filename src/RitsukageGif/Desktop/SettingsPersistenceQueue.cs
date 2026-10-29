using System.Diagnostics;
using System.Runtime.ExceptionServices;

namespace RitsukageGif.Desktop
{
    internal sealed class SettingsPersistenceQueue<T> : IAsyncDisposable where T : class
    {
        // ReSharper disable once StaticMemberInGenericType
        private static readonly ILogger Logger = LogHub.GetLogger("Desktop.Settings.Persistence");
        private readonly TimeSpan _debounce;
        private readonly List<(long Revision, TaskCompletionSource Completion)> _flushWaiters = [];
        private readonly Lock _gate = new();
        private readonly Action<Exception> _reportError;
        private readonly TimeSpan _retryDelay;
        private readonly SemaphoreSlim _wake = new(0, 1);
        private readonly Task _worker;
        private readonly Func<T, CancellationToken, Task> _writer;
        private long _completedRevision;
        private bool _disposeRequested;
        private long _flushRevision;
        private long _lastRequest;
        private T? _pending;
        private long _requestedRevision;
        private bool _retryPending;

        internal SettingsPersistenceQueue(Func<T, CancellationToken, Task> writer, Action<Exception> reportError,
            TimeSpan? debounce = null)
        {
            ArgumentNullException.ThrowIfNull(writer);
            ArgumentNullException.ThrowIfNull(reportError);
            _debounce = debounce ?? TimeSpan.FromSeconds(3);
            if (_debounce < TimeSpan.Zero || _debounce.TotalMilliseconds > int.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(debounce));
            _retryDelay = _debounce > TimeSpan.Zero ? _debounce : TimeSpan.FromSeconds(3);
            _writer = writer;
            _reportError = reportError;
            _worker = Task.Run(RunAsync);
        }

        public ValueTask DisposeAsync()
        {
            lock (_gate)
                if (!_disposeRequested)
                {
                    _disposeRequested = true;
                    Wake();
                }

            return new(_worker);
        }

        internal void RequestSave(T snapshot)
        {
            ArgumentNullException.ThrowIfNull(snapshot);
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposeRequested, this);
                _requestedRevision = checked(_requestedRevision + 1);
                _pending = snapshot;
                _lastRequest = Stopwatch.GetTimestamp();
                _retryPending = false;
                Logger.ZLogDebug($"Settings save queued. Revision={_requestedRevision}");
                Wake();
            }
        }

        internal Task FlushAsync(CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            Task completion;
            lock (_gate)
                if (_disposeRequested)
                {
                    completion = _worker;
                }
                else if (_completedRevision == _requestedRevision)
                {
                    return Task.CompletedTask;
                }
                else
                {
                    var waiter = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    _flushWaiters.Add((_requestedRevision, waiter));
                    _flushRevision = _requestedRevision;
                    Logger.ZLogDebug($"Settings flush requested. Revision={_flushRevision}");
                    Wake();
                    completion = waiter.Task;
                }

            return token.CanBeCanceled ? completion.WaitAsync(token) : completion;
        }

        private void Wake()
        {
            if (_wake.CurrentCount == 0) _wake.Release();
        }

        private async Task RunAsync()
        {
            try
            {
                while (true)
                {
                    TimeSpan wait;
                    T? snapshot;
                    long revision;
                    lock (_gate)
                        if (_completedRevision == _requestedRevision)
                        {
                            if (_disposeRequested) return;
                            wait = Timeout.InfiniteTimeSpan;
                            snapshot = null;
                            revision = 0;
                        }
                        else
                        {
                            wait = _disposeRequested || _flushRevision > _completedRevision
                                ? TimeSpan.Zero
                                : (_retryPending ? _retryDelay : _debounce) -
                                  Stopwatch.GetElapsedTime(_lastRequest);
                            snapshot = _pending;
                            revision = _requestedRevision;
                        }

                    if (snapshot is null || wait > TimeSpan.Zero)
                    {
                        await _wake.WaitAsync(wait).ConfigureAwait(false);
                        continue;
                    }

                    Exception? failure = null;
                    try
                    {
                        Logger.ZLogDebug($"Settings save started. Revision={revision}");
                        await _writer(snapshot, CancellationToken.None).ConfigureAwait(false);
                        Logger.ZLogInformation($"Settings save completed. Revision={revision}");
                    }
                    catch (Exception error)
                    {
                        Logger.ZLogError(error, $"Settings save failed. Revision={revision}");
                        failure = ReportFailure(error);
                    }

                    bool stop;
                    lock (_gate)
                    {
                        if (failure is null)
                        {
                            _completedRevision = revision;
                            if (_requestedRevision == revision) _pending = null;
                        }
                        else
                        {
                            if (_requestedRevision == revision)
                            {
                                _lastRequest = Stopwatch.GetTimestamp();
                                _retryPending = true;
                            }

                            if (_flushRevision <= revision) _flushRevision = 0;
                        }

                        CompleteWaiters(revision, failure);
                        stop = failure is not null && _disposeRequested && _requestedRevision == revision;
                    }

                    if (stop) ExceptionDispatchInfo.Capture(failure!).Throw();
                }
            }
            finally
            {
                lock (_gate) _pending = null;
                _wake.Dispose();
            }
        }

        private Exception ReportFailure(Exception error)
        {
            try
            {
                _reportError(error);
                return error;
            }
            catch (Exception notificationError)
            {
                Logger.ZLogError(notificationError, $"Settings save failure notification failed.");
                return new AggregateException(error, notificationError);
            }
        }

        private void CompleteWaiters(long revision, Exception? failure)
        {
            for (var index = _flushWaiters.Count - 1; index >= 0; index--)
            {
                var waiter = _flushWaiters[index];
                if (waiter.Revision > revision) continue;
                _flushWaiters.RemoveAt(index);
                if (failure is null)
                {
                    Logger.ZLogDebug($"Settings flush completed. Revision={waiter.Revision}");
                    waiter.Completion.TrySetResult();
                }
                else
                {
                    waiter.Completion.TrySetException(failure);
                }
            }
        }
    }
}
