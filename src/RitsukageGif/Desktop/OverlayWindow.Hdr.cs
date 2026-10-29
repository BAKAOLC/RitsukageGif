using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    internal sealed partial class OverlayWindow
    {
        private HdrOverlayWindow? _hdrBackground;
        private bool _hdrFailed;
        private int _hdrGeneration;
        private CapturedFrame? _hdrRequestedScreen, _hdrRequestedWindow;
        private CancellationTokenSource? _hdrUpload;

        private void InitializeHdrBackground()
        {
            if (_sourceLocal || !OperatingSystem.IsWindows()) return;
            if (_coordinator.HdrScreenshotFor(Region) is not null)
                TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
            Opened += (_, _) => RefreshHdrBackground();
            Closed += (_, _) => DisposeHdrBackground();
            PropertyChanged += (_, args) =>
            {
                if (args.Property == IsVisibleProperty)
                {
                    if (IsVisible) RefreshHdrBackground();
                    else SuspendHdrBackground();
                }
                else if (args.Property == TopmostProperty)
                {
                    if (Topmost) RefreshHdrBackground();
                    else SuspendHdrBackground();
                }
                else if (args.Property == ActualTransparencyLevelProperty)
                {
                    RefreshHdrBackground();
                }
            };
        }

        private void RefreshHdrBackground()
        {
            if (_sourceLocal || _imageDisposed || !OperatingSystem.IsWindows() || !IsVisible || !Topmost ||
                TryGetPlatformHandle() is not { } handle) return;
            var screen = _coordinator.HdrScreenshotFor(Region);
            if (screen is null)
            {
                SuspendHdrBackground();
                return;
            }

            if (!TransparencyLevelHint.Contains(WindowTransparencyLevel.Transparent))
                TransparencyLevelHint = [WindowTransparencyLevel.Transparent];

            try
            {
                if (_hdrBackground is null)
                {
                    _hdrBackground = new(handle.Handle, Region);
                    _hdrBackground.DisplayChanged += () =>
                    {
                        _hdrBackground?.InvalidateDevice();
                        _hdrFailed = false;
                        _hdrRequestedScreen = null;
                        RefreshHdrBackground();
                    };
                }

                if (ActualTransparencyLevel != WindowTransparencyLevel.Transparent ||
                    !HdrOverlayWindow.SupportsTransparentInput(handle.Handle) ||
                    !WindowsDisplayColor.Read(WindowsDisplayColor.ForRegion(Region)).Hdr ||
                    (_coordinator.Snapshot.Cursor && _cursorOverlay is { CanRenderHdr: false }))
                {
                    SuspendHdrBackground();
                    return;
                }

                var window = _coordinator.FrozenWindow;
                if (_coordinator is { ScreenshotMode: true, WindowSource: true } && window?.Hdr is null)
                {
                    SuspendHdrBackground();
                    return;
                }

                if (ReferenceEquals(screen, _hdrRequestedScreen) && ReferenceEquals(window?.Hdr, _hdrRequestedWindow))
                {
                    if (_canvas.NativeHdrBackground) _hdrBackground.SetSelection(_canvas.Selected);
                    return;
                }

                if (_hdrFailed) return;
                _hdrRequestedScreen = screen;
                _hdrRequestedWindow = window?.Hdr;
                _hdrUpload?.Cancel();
                var cancellation = new CancellationTokenSource();
                _hdrUpload = cancellation;
                var generation = ++_hdrGeneration;
                SetHdrBackgroundVisible(false);
                _ = UploadHdrBackgroundAsync(screen, window?.Hdr, window?.Region ?? default, generation, cancellation);
            }
            catch (Exception error) when (error is ExternalException or NotSupportedException)
            {
                _hdrFailed = true;
                SuspendHdrBackground();
                Trace.TraceWarning("HDR selection preview unavailable: {0}", error.Message);
            }
        }

        private async Task UploadHdrBackgroundAsync(CapturedFrame screen, CapturedFrame? window,
            PixelRect bounds, int generation, CancellationTokenSource cancellation)
        {
            try
            {
                await _hdrBackground!.PresentAsync(screen, window, bounds, cancellation.Token);
                if (generation != _hdrGeneration || _imageDisposed || !IsVisible || !Topmost ||
                    cancellation.IsCancellationRequested) return;
                _hdrBackground.SetSelection(_canvas.Selected);
                _hdrBackground.Show();
                SetHdrBackgroundVisible(true);
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
            }
            catch (Exception error) when
                (error is ExternalException or NotSupportedException or ObjectDisposedException)
            {
                if (generation == _hdrGeneration && !_imageDisposed)
                {
                    _hdrFailed = true;
                    SuspendHdrBackground();
                    Trace.TraceWarning("HDR selection preview unavailable: {0}", error.Message);
                }
            }
            finally
            {
                if (ReferenceEquals(_hdrUpload, cancellation)) _hdrUpload = null;
                cancellation.Dispose();
            }
        }

        private void SetHdrBackgroundVisible(bool visible)
        {
            _canvas.NativeHdrBackground = visible;
            if (_cursorOverlay is not null)
            {
                _cursorOverlay.NativeHdrBackground = visible;
                _cursorOverlay.InvalidateVisual();
            }

            Background = visible ? Brushes.Transparent : Brushes.Black;
            _canvas.InvalidateVisual();
            if (!visible) _hdrBackground?.Hide();
        }

        private void SuspendHdrBackground()
        {
            ++_hdrGeneration;
            _hdrUpload?.Cancel();
            _hdrRequestedScreen = _hdrRequestedWindow = null;
            SetHdrBackgroundVisible(false);
        }

        private void DisposeHdrBackground()
        {
            SuspendHdrBackground();
            _hdrBackground?.Dispose();
            _hdrBackground = null;
        }
    }
}
