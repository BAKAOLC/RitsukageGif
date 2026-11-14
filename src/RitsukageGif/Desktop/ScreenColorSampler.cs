using System.Threading.Channels;
using Avalonia.Media;
using Avalonia.Threading;

namespace RitsukageGif.Desktop
{
    internal sealed class ScreenColorSampler
    {

        private readonly CancellationTokenSource _stop = new();
        private readonly Channel<(Avalonia.PixelPoint Point, long Revision)> _requests =
            Channel.CreateBounded<(Avalonia.PixelPoint, long)>(new BoundedChannelOptions(1)
            {
                SingleReader = true, SingleWriter = true, FullMode = BoundedChannelFullMode.DropOldest,
            });
        private readonly Task _worker;

        public ScreenColorSampler(PixelRect[] monitors, Task prior,
            Action<ScreenColorSampler, long, Color?> publish, Action<ScreenColorSampler, Exception> failure)
        {
            _worker = StartAsync();
            return;

            async Task StartAsync()
            {
                await prior.ConfigureAwait(false);
                if (_stop.IsCancellationRequested) return;
                await Task.Factory.StartNew(() => Run(_stop.Token), CancellationToken.None,
                    TaskCreationOptions.LongRunning, TaskScheduler.Default).ConfigureAwait(false);
            }

            void Run(CancellationToken token)
            {
                DesktopCapture? capture = null;
                try
                {
                    try
                    {
                        while (_requests.Reader.WaitToReadAsync(token).AsTask().GetAwaiter().GetResult())
                        {
                            if (!_requests.Reader.TryRead(out var request)) continue;
                            token.ThrowIfCancellationRequested();
                            var region = new PixelRect(request.Point.X, request.Point.Y, 1, 1);
                            if (capture is null) capture = new(new(region, Divisor: 1, Cursor: false), monitors);
                            else capture.Move(region);
                            var pixels = capture.Capture(token);
                            var color = pixels is [var blue, var green, var red, > 0]
                                ? Color.FromRgb(red, green, blue)
                                : (Color?)null;
                            Dispatcher.UIThread.InvokeAsync(() => publish(this, request.Revision, color),
                                DispatcherPriority.Background, token).GetTask().GetAwaiter().GetResult();
                        }
                    }
                    finally
                    {
                        capture?.Dispose();
                    }
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                }
                catch (Exception error)
                {
                    Dispatcher.UIThread.Post(() => failure(this, error));
                }
            }
        }

        public void Request(Avalonia.PixelPoint point, long revision)
        {
            _requests.Writer.TryWrite((point, revision));
        }

        public async Task StopAsync()
        {
            await _stop.CancelAsync().ConfigureAwait(false);
            try
            {
                await _worker.ConfigureAwait(false);
            }
            finally
            {
                _stop.Dispose();
            }
        }
    }
}
