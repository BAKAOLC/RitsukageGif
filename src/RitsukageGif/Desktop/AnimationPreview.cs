using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Image = Avalonia.Controls.Image;

namespace RitsukageGif.Desktop
{
    internal sealed partial class AnimationPreview : IDisposable
    {
        private readonly Stopwatch _clock = new();
        private readonly Action<string> _error;
        private readonly HdrPreviewHost _hdr;
        private readonly DispatcherTimer _timer = new();
        private readonly Image _view;
        private WriteableBitmap? _bitmap;
        private bool _disposed;
        private long _nextDue;
        private string? _path;
        private bool _paused;
        private Playback? _playback;

        public AnimationPreview(Image view, HdrPreviewHost hdr, Action<string>? error = null)
        {
            _view = view;
            _hdr = hdr;
            _view.SizeChanged += (_, _) => UpdateHdrBounds();
            _error = error ?? (_ => { });
            _timer.Tick += (_, _) => Tick();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Clear();
            _hdr.Dispose();
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
                _bitmap = new(new(frame.Width, frame.Height), new(96, 96),
                    PixelFormat.Bgra8888, AlphaFormat.Premul);
                _view.Source = _bitmap;
            }

            using (var buffer = _bitmap.Lock())
                if (buffer.RowBytes == frame.Width * 4)
                    Marshal.Copy(frame.Pixels, 0, buffer.Address, checked(frame.Width * frame.Height * 4));
                else
                    for (var y = 0; y < frame.Height; y++)
                        Marshal.Copy(frame.Pixels, y * frame.Width * 4, buffer.Address + y * buffer.RowBytes,
                            frame.Width * 4);

            _view.InvalidateVisual();
            UpdateHdrBounds();
            _hdr.SetFrame(frame);
        }

        public void Pause()
        {
            _timer.Stop();
            _clock.Stop();
            _playback?.Dispose();
            _playback = null;
            _paused = true;
        }

        public Task ResumeAsync()
        {
            return !_disposed && _paused && _path is { } path
                ? LoadAsync(path)
                : Task.CompletedTask;
        }

        private void UpdateHdrBounds()
        {
            if (_bitmap is null) return;
            var scale = Math.Min(_view.Bounds.Width / _bitmap.PixelSize.Width,
                _view.Bounds.Height / _bitmap.PixelSize.Height);
            _hdr.Width = Math.Max(0, _bitmap.PixelSize.Width * scale);
            _hdr.Height = Math.Max(0, _bitmap.PixelSize.Height * scale);
        }

        private void Clear()
        {
            _timer.Stop();
            _clock.Stop();
            _playback?.Dispose();
            _playback = null;
            _view.Source = null;
            _hdr.SetFrame(null);
            _bitmap?.Dispose();
            _bitmap = null;
        }
    }
}
