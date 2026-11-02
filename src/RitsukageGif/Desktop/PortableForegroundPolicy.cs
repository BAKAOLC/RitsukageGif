using Avalonia.Threading;

namespace RitsukageGif.Desktop
{
    internal sealed partial class PortableForegroundPolicy : IDisposable
    {
        private static readonly ILogger Logger = LogHub.GetLogger("Desktop.Input.Foreground");
        private static readonly ForegroundHotkeyContext UnavailableContext = new(null, false, false, false);
        private static PortableForegroundPolicy? _active;
        private ForegroundHotkeyContext _current = UnavailableContext;
        private readonly CancellationTokenSource _stop = new();
        private readonly Action _changed;
        private readonly Action<ForegroundHotkeyContext> _status;
        private int _disposed;
        private int _queued;
        private int _published;

        internal PortableForegroundPolicy(Action changed, Action<ForegroundHotkeyContext> status)
        {
            _changed = changed;
            _status = status;
            Volatile.Write(ref _active, this);
            Completion = Task.Run(RunAsync);
        }

        internal static bool Supported => OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ||
                                          (OperatingSystem.IsLinux() && !DesktopCapture.UsesSourceSelection);

        internal Task Completion { get; }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            Interlocked.CompareExchange(ref _active, null, this);
            _stop.Cancel();
            _ = Completion.ContinueWith(_ => _stop.Dispose(), CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }

        internal static ForegroundHotkeyContext Read()
        {
            return Volatile.Read(ref _active) is { } observer
                ? Volatile.Read(ref observer._current)
                : UnavailableContext;
        }

        private async Task RunAsync()
        {
            if (!OperatingSystem.IsMacOS() && !(OperatingSystem.IsLinux() && !DesktopCapture.UsesSourceSelection))
            {
                Publish(new(null, false, false, false));
                return;
            }

            var failures = 0;
            while (!_stop.IsCancellationRequested)
            {
                try
                {
                    await RunHelperAsync(_stop.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (_stop.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception error)
                {
                    if (failures == 0) Logger.ZLogError(error, $"Foreground policy observer failed; retrying.");
                    failures = Math.Min(5, failures + 1);
                    Publish(new(null, false, false, false));
                }

                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(1 << failures), _stop.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (_stop.IsCancellationRequested)
                {
                    return;
                }
            }
        }

        private void Publish(ForegroundHotkeyContext context)
        {
            if (Volatile.Read(ref _disposed) != 0) return;
            var previous = Interlocked.Exchange(ref _current, context);
            var first = Interlocked.Exchange(ref _published, 1) == 0;
            if ((!first && previous == context) || Interlocked.Exchange(ref _queued, 1) != 0) return;
            Dispatcher.UIThread.Post(() =>
            {
                Interlocked.Exchange(ref _queued, 0);
                if (Volatile.Read(ref _disposed) != 0) return;
                var latest = Volatile.Read(ref _current);
                _status(latest);
                _changed();
            });
        }
    }
}
