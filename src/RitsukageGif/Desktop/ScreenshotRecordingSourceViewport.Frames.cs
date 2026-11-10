using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Platform;
using Avalonia.Threading;

namespace RitsukageGif.Desktop
{
    internal sealed partial class ScreenshotRecordingSourceViewport
    {
        private void QueueFrame()
        {
            lock (_refreshGate)
            {
                if (_disposed || !_ready || !_refresh.IsCompleted ||
                    Stopwatch.GetElapsedTime(_refreshedAt).TotalMilliseconds < 100) return;
                _refreshedAt = Stopwatch.GetTimestamp();
                _refresh = RefreshFrameAsync(_region, _camera, _lifetime.Token);
            }
        }

        private async Task RefreshFrameAsync(PixelRect region, RecordingCameraSession? camera, CancellationToken token)
        {
            try
            {
                var frame = await Task.Run(() =>
                {
                    CapturedFrameSet? preview = null;
                    var density = Math.Min(1, Math.Min(1280d / region.Width, 720d / region.Height));
                    _demand.Use(snapshot =>
                    {
                        var frames = NativeFrameComposition.Compose(
                            [(snapshot.Geometry.Bounds, snapshot.Frames with { Hdr = null })], region, density, token);
                        preview = frames with { Sdr = frames.Sdr with { Pixels = frames.Sdr.Pixels.ToArray() } };
                    }, region, density, token: token, fps: 10);
                    return camera?.Composite(preview!, region, token).Sdr ?? preview!.Sdr;
                }, token).ConfigureAwait(false);
                var pixels = CapturedFramePixels.ToStraightBgra(frame, token);
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (_disposed || region != _region) return;
                    if (_bitmap?.PixelSize != new PixelSize(frame.Width, frame.Height))
                    {
                        var old = _bitmap;
                        _bitmap = new(new(frame.Width, frame.Height), new(96, 96), PixelFormat.Bgra8888,
                            AlphaFormat.Unpremul);
                        _image.Source = _bitmap;
                        old?.Dispose();
                    }

                    using var buffer = _bitmap.Lock();
                    for (var row = 0; row < frame.Height; row++)
                        Marshal.Copy(pixels, row * frame.Width * 4, buffer.Address + row * buffer.RowBytes,
                            frame.Width * 4);
                    _image.InvalidateVisual();
                    NotifyCameraOptions();
                });
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
            }
            catch (Exception error)
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (!_disposed) Failed?.Invoke(error);
                });
            }
        }
    }
}
