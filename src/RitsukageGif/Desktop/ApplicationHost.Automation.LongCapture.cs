using Avalonia.Threading;

namespace RitsukageGif.Desktop
{
    public sealed partial class ApplicationHost
    {
        private async Task<ILongCaptureFrameSource> CreateLongCaptureSourceAsync(LongCaptureOptions options,
            CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (options.Source is not (LongCaptureSourceKind.Screen or LongCaptureSourceKind.Window))
                throw new ArgumentException("A live long capture needs a screen or window source.", nameof(options));
            if (options is { Source: LongCaptureSourceKind.Window, WindowId: null })
                throw new ArgumentException("Window long capture requires windowId.", nameof(options));
            var monitors = await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (IsClosing) throw new InvalidOperationException(UiText.Get("CommandNotReady"));
                return Screens.All.Select(screen => new PixelRect(screen.Bounds.X, screen.Bounds.Y,
                    screen.Bounds.Width, screen.Bounds.Height)).ToArray();
            });
            NativeCaptureSessionReference? source = null;
            try
            {
                if (DesktopCapture.UsesSourceSelection)
                {
                    var kind = options.Source == LongCaptureSourceKind.Window
                        ? CaptureSourceKind.Window
                        : CaptureSourceKind.Screen;
                    using var expected = NativeCaptureSession.RetainStaticSource(kind, out var window);
                    if (expected is not null)
                    {
                        source = await NativeCaptureSession.PrepareStaticSourceAsync(expected.Session, window, false,
                            10, options.Stitching.RetainHdr, token).ConfigureAwait(false);
                    }
                    else
                    {
                        using var selection = await NativeCaptureSession.BeginSelectionAsync(window, false,
                            10, options.Stitching.RetainHdr, token).ConfigureAwait(false);
                        source = selection.RetainSource();
                    }

                    var bounds = source.Session.NativeBounds ?? throw new CaptureSourceLostException();
                    if (options.Region.Intersect(bounds) != options.Region) throw new CaptureSourceLostException();
                    monitors = [bounds];
                }

                WindowTarget? target = null;
                if (options.Source == LongCaptureSourceKind.Window)
                {
                    var windows = source?.Session.GetAuthorizedWindows() ?? WindowLocator.List();
                    target = windows.FirstOrDefault(window => window.Handle == options.WindowId)
                             ?? throw new CaptureSourceLostException();
                    if (options.Region.Intersect(target.Client) != options.Region)
                        throw new ArgumentException("The long capture region must fit inside the window client area.",
                            nameof(options));
                }

                token.ThrowIfCancellationRequested();
                var capture = new ScreenshotScrollingSource(options.Region, monitors, target, options.ToneMap,
                    nativeSource: source?.Session);
                var result = new LongCaptureFrameSource(capture, source);
                source = null;
                return result;
            }
            finally
            {
                source?.Dispose();
            }
        }
    }
}
