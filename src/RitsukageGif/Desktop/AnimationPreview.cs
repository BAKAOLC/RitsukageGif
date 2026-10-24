using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Image = Avalonia.Controls.Image;

namespace RitsukageGif.Desktop
{
    internal sealed class AnimationPreview : IDisposable
    {
        private readonly Stopwatch _clock = new();
        private readonly Action<string> _error;
        private readonly DispatcherTimer _timer = new();
        private readonly Image _view;
        private WriteableBitmap? _bitmap;
        private long _nextDue;
        private Playback? _playback;
        private bool _disposed;
        private string? _path;
        private bool _paused;

        public AnimationPreview(Image view, Action<string>? error = null)
        {
            _view = view;
            _error = error ?? (_ => { });
            _timer.Tick += (_, _) => Tick();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Clear();
        }

        public async Task LoadAsync(string path)
        {
            if (_disposed) return;
            Clear();
            _path = path;
            _paused = false;
            var playback = new Playback(path);
            _playback = playback;
            try
            {
                using var first = await playback.Frames.Reader.ReadAsync(playback.Token);
                if (_playback != playback || _disposed) return;
                Present(first);
                _nextDue = first.Delay;
                _clock.Restart();
                Schedule();
            }
            catch (Exception) when (_playback != playback || _disposed)
            {
            }
            catch
            {
                Clear();
                throw;
            }
        }

        private void Tick()
        {
            _timer.Stop();
            if (_playback is not { } playback) return;
            PreviewFrame? latest = null;
            try
            {
                while (_clock.ElapsedMilliseconds >= _nextDue && playback.Frames.Reader.TryRead(out var frame))
                {
                    latest?.Dispose();
                    latest = frame;
                    _nextDue += frame.Delay;
                }

                if (latest is not null) Present(latest);
                if (playback.Frames.Reader.Completion.IsFaulted)
                    throw playback.Frames.Reader.Completion.Exception!.GetBaseException();
                if (playback.Frames.Reader.Completion.IsCompleted)
                {
                    _clock.Stop();
                    return;
                }

                Schedule();
            }
            catch (Exception exception)
            {
                Clear();
                _error(exception.Message);
            }
            finally
            {
                latest?.Dispose();
            }
        }

        private void Schedule()
        {
            _timer.Interval = TimeSpan.FromMilliseconds(Math.Max(8, _nextDue - _clock.ElapsedMilliseconds));
            _timer.Start();
        }

        private void Present(PreviewFrame frame)
        {
            if (_bitmap is null || _bitmap.PixelSize != new PixelSize(frame.Width, frame.Height))
            {
                _view.Source = null;
                _bitmap?.Dispose();
                _bitmap = new WriteableBitmap(new PixelSize(frame.Width, frame.Height), new Vector(96, 96),
                    PixelFormat.Bgra8888, AlphaFormat.Premul);
                _view.Source = _bitmap;
            }

            using (var buffer = _bitmap.Lock())
            {
                for (var y = 0; y < frame.Height; y++)
                    Marshal.Copy(frame.Pixels, y * frame.Width * 4, buffer.Address + y * buffer.RowBytes,
                        frame.Width * 4);
            }

            _view.InvalidateVisual();
        }

        public void Pause()
        {
            _timer.Stop();
            _clock.Stop();
            _playback?.Dispose();
            _playback = null;
            _paused = true;
        }

        public Task ResumeAsync() => !_disposed && _paused && _path is { } path
            ? LoadAsync(path)
            : Task.CompletedTask;

        private void Clear()
        {
            _timer.Stop();
            _clock.Stop();
            _playback?.Dispose();
            _playback = null;
            _view.Source = null;
            _bitmap?.Dispose();
            _bitmap = null;
        }

        private sealed class Playback : IDisposable
        {
            private readonly CancellationTokenSource _cancel = new();
            private readonly Task _worker;
            private readonly CancellationToken _token;
            private int _disposed;

            public Playback(string path)
            {
                var token = _token = _cancel.Token;
                _worker = Task.Run(async () =>
                {
                    try
                    {
                        while (!token.IsCancellationRequested)
                        {
                            var count = 0;
                            foreach (var frame in PreviewDecoder.Read(path, token))
                            {
                                try
                                {
                                    await Frames.Writer.WriteAsync(frame, token);
                                }
                                catch
                                {
                                    frame.Dispose();
                                    throw;
                                }

                                count++;
                            }

                            if (count == 0) throw new InvalidDataException(UiText.Get("NoFrames"));
                            if (count == 1) break;
                        }

                        Frames.Writer.TryComplete();
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested)
                    {
                        Frames.Writer.TryComplete();
                    }
                    catch (Exception ex)
                    {
                        Frames.Writer.TryComplete(ex);
                    }
                }, CancellationToken.None);
            }

            public CancellationToken Token => _token;

            public Channel<PreviewFrame> Frames { get; } = Channel.CreateBounded<PreviewFrame>(
                new BoundedChannelOptions(2)
                    { SingleReader = false, SingleWriter = true, FullMode = BoundedChannelFullMode.Wait });

            public void Dispose()
            {
                if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
                _cancel.Cancel();
                _ = _worker.ContinueWith(_ =>
                {
                    while (Frames.Reader.TryRead(out var frame)) frame.Dispose();
                    _cancel.Dispose();
                }, TaskScheduler.Default);
            }
        }
    }
}
