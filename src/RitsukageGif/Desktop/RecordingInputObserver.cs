using System.Diagnostics;
using System.Threading.Channels;

namespace RitsukageGif.Desktop
{
    internal sealed partial class RecordingInputObserver : IAsyncDisposable
    {
        private readonly Action<RecordingInputEvent> _consume;

        private readonly Func<bool>? _isActive;

        private readonly Channel<RecordingInputEvent> _events = Channel.CreateBounded<RecordingInputEvent>(
            new BoundedChannelOptions(4096) { SingleReader = true, SingleWriter = true });
        private readonly bool _recordKeyboard;
        private readonly bool _recordMouse;
        private readonly CancellationTokenSource _stop = new();
        private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

        private readonly TaskCompletionSource _disposeCompleted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly Lock _lifetime = new();
        private CancellationTokenRegistration _registration;
        private Exception? _failure;
        private int _started;
        private int _disposeStarted;
        private bool _disposed;

        internal RecordingInputObserver(bool recordKeyboard, bool recordMouse, Action<RecordingInputEvent> consume,
            Func<bool>? isActive = null)
        {
            _recordKeyboard = recordKeyboard;
            _recordMouse = recordMouse;
            _consume = consume;
            _isActive = isActive;
        }

        internal static string? UnavailableReason => OperatingSystem.IsLinux() && DesktopCapture.UsesSourceSelection
            ? "This Wayland session does not provide passive global keyboard and mouse observation."
            : OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() || OperatingSystem.IsLinux()
                ? null
                : "Passive recording input observation is unavailable on this platform.";

        internal Task Completion => _completion.Task;

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposeStarted, 1) == 0) _ = DisposeCoreAsync();
            return new(_disposeCompleted.Task);
        }

        internal async Task StartAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if ((_recordKeyboard || _recordMouse) && UnavailableReason is { } reason)
                throw new PlatformNotSupportedException(reason);
            lock (_lifetime)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (Interlocked.Exchange(ref _started, 1) != 0)
                    throw new InvalidOperationException("The input observer has already started.");
                if (!_recordKeyboard && !_recordMouse)
                {
                    _ready.TrySetResult();
                    _completion.TrySetResult();
                    return;
                }

                _registration = cancellationToken.Register(Stop);
                try
                {
                    new Thread(Run) { IsBackground = true, Name = "Recording input observation" }.Start();
                }
                catch (Exception error)
                {
                    _ready.TrySetException(error);
                    _completion.TrySetResult();
                }
            }

            try
            {
                await _ready.Task.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
            }
            catch
            {
                Stop();
                try
                {
                    await _completion.Task.ConfigureAwait(false);
                }
                catch (Exception cleanupError)
                {
                    LogHub.GetLogger("Desktop.Recording.Input").ZLogDebug(cleanupError,
                        $"Recording input observer cleanup failed after startup failure.");
                }

                throw;
            }
        }

        private async Task DisposeCoreAsync()
        {
            lock (_lifetime)
                if (!_disposed)
                {
                    _disposed = true;
                    _stop.Cancel();
                    if (Volatile.Read(ref _started) == 0)
                    {
                        _ready.TrySetCanceled();
                        _completion.TrySetResult();
                    }
                }

            Exception? failure = null;
            try
            {
                await _completion.Task.ConfigureAwait(false);
            }
            catch (Exception error)
            {
                failure = error;
            }
            finally
            {
                await _registration.DisposeAsync().ConfigureAwait(false);
                _stop.Dispose();
                if (failure is null) _disposeCompleted.TrySetResult();
                else _disposeCompleted.TrySetException(failure);
            }
        }

        private void Stop()
        {
            lock (_lifetime)
                if (!_disposed)
                    _stop.Cancel();
        }

        private void Run()
        {
            var consumer = Task.Run(ConsumeAsync);
            Exception? stopped = null;
            try
            {
                _stop.Token.ThrowIfCancellationRequested();
                if (OperatingSystem.IsWindows()) RunWindows();
                else if (OperatingSystem.IsMacOS()) RunMac();
                else RunX11();
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested)
            {
                _ready.TrySetCanceled(_stop.Token);
            }
            catch (Exception error)
            {
                stopped = error;
                _ready.TrySetException(error);
            }
            finally
            {
                _events.Writer.TryComplete();
                consumer.GetAwaiter().GetResult();
                stopped ??= _failure;
                if (stopped is null)
                {
                    _ready.TrySetCanceled(_stop.Token);
                    _completion.TrySetResult();
                }
                else
                {
                    _ready.TrySetException(stopped);
                    _completion.TrySetException(stopped);
                }
            }
        }

        private void Emit(RecordingInputEvent input)
        {
            if (_stop.IsCancellationRequested || _isActive?.Invoke() == false) return;
            if (!_events.Writer.TryWrite(input))
                Fail(new IOException("The recording input queue could not keep up with its consumer."));
        }

        private async Task ConsumeAsync()
        {
            try
            {
                await foreach (var input in _events.Reader.ReadAllAsync().ConfigureAwait(false)) _consume(input);
            }
            catch (Exception error)
            {
                Fail(error);
            }
        }

        private void Fail(Exception error)
        {
            Interlocked.CompareExchange(ref _failure, error, null);
            Stop();
        }

        private static long NanosecondsToTimestamp(ulong nanoseconds)
        {
            return (long)((UInt128)nanoseconds * (ulong)Stopwatch.Frequency / 1_000_000_000);
        }
    }
}
