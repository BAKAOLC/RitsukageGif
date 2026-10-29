using Avalonia.Platform;
using Avalonia.Threading;

namespace RitsukageGif.Desktop
{
    // ReSharper disable once ClassCannotBeInstantiated
    internal sealed partial class PinnedImageWindow
    {
        private PixelRect _annotationPreviewBounds;
        private ScreenshotTiledImage? _annotationPreviewImage;
        private bool _annotationPreviewRunning, _annotationPreviewDrawing, _annotationPreviewBusy;
        private ScreenshotAnnotationSession? _annotationPreviewSession;
        private DispatcherTimer? _annotationPreviewTimer;
        private ScreenshotTool _annotationPreviewTool;
        private long _annotationPreviewVersion, _annotationPreviewEpoch, _annotationPreviewDisplayedVersion;
        private CancellationTokenSource? _annotationPreviewWork;

        private void InitializeAnnotationPreview(ScreenshotAnnotationSession session)
        {
            StopAnnotationPreview();
            _annotationPreviewSession = session;
            _annotationPreviewTimer = new() { Interval = TimeSpan.FromMilliseconds(16) };
            _annotationPreviewTimer.Tick += AnnotationPreviewTick;
            _annotationPreviewDrawing = session.IsDrawing;
            _annotationPreviewBusy = session.IsBusy;
            _annotationPreviewTool = session.Tool;
            _annotationPreviewDisplayedVersion = -1;
            session.Changed += AnnotationPreviewChanged;
            AnnotationPreviewChanged();
        }

        private void StopAnnotationPreview()
        {
            if (_annotationPreviewSession is { } session) session.Changed -= AnnotationPreviewChanged;
            _annotationPreviewSession = null;
            _annotationPreviewVersion++;
            _annotationPreviewEpoch++;
            _annotationPreviewWork?.Cancel();
            if (_annotationPreviewTimer is { } timer)
            {
                timer.Stop();
                timer.Tick -= AnnotationPreviewTick;
                _annotationPreviewTimer = null;
            }

            RestoreAnnotationPreviewSource();
        }

        private void RestoreAnnotationPreviewSource()
        {
            _annotationPreviewBounds = default;
            if (_annotationSurface is { } surface)
            {
                surface.RenderContent = true;
                surface.InvalidateVisual();
            }

            if (_annotationPreviewImage is not { } image) return;
            _annotationPreviewImage = null;
            _image.Source = _bitmap;
            image.Dispose();
            if (!_closed) UpdateVisibleRegion();
        }

        private void AnnotationPreviewChanged()
        {
            if (_closed || _annotationPreviewSession is not { } session ||
                !ReferenceEquals(session, _annotation)) return;
            _annotationPreviewVersion++;
            if (_annotationPreviewDrawing != session.IsDrawing || _annotationPreviewBusy != session.IsBusy ||
                _annotationPreviewTool != session.Tool)
            {
                _annotationPreviewEpoch++;
                _annotationPreviewDrawing = session.IsDrawing;
                _annotationPreviewBusy = session.IsBusy;
                _annotationPreviewTool = session.Tool;
                _annotationPreviewWork?.Cancel();
            }

            if (!NeedsAnnotationPreview(session))
            {
                _annotationPreviewTimer?.Stop();
                _annotationPreviewWork?.Cancel();
                RestoreAnnotationPreviewSource();
                return;
            }

            if (_annotationSurface is { } surface)
            {
                surface.RenderContent = false;
                surface.InvalidateVisual();
            }

            if (!_annotationPreviewRunning) _annotationPreviewTimer?.Start();
        }

        private void AnnotationPreviewTick(object? sender, EventArgs args)
        {
            _annotationPreviewTimer?.Stop();
            if (_annotationPreviewRunning || _closed || _annotationPreviewSession is not { } session ||
                !ReferenceEquals(session, _annotation) || session.IsBusy || !NeedsAnnotationPreview(session)) return;
            var work = CancellationTokenSource.CreateLinkedTokenSource(_annotationCancellation!.Token);
            _annotationPreviewWork = work;
            _annotationPreviewRunning = true;
            var region = new PixelRect(0, 0, (int)_bitmap.Size.Width, (int)_bitmap.Size.Height);
            var bounds = session.GetPreviewBounds().Intersect(region);
            var dirty = UnionPreviewBounds(bounds, _annotationPreviewBounds).Intersect(region);
            _ = ComposeAnnotationPreviewAsync(session, _pixels, region, dirty, bounds,
                _annotationPreviewVersion, _annotationPreviewEpoch, session.IsDrawing, work);
        }

        private async Task ComposeAnnotationPreviewAsync(ScreenshotAnnotationSession session, byte[] source,
            PixelRect region, PixelRect dirty, PixelRect bounds, long version, long epoch, bool drawing,
            CancellationTokenSource work)
        {
            var token = work.Token;
            try
            {
                if (dirty.IsEmpty)
                {
                    _annotationPreviewDisplayedVersion = version;
                    return;
                }

                var precision = await GetAnnotationPrecisionSourceAsync(session, token);
                token.ThrowIfCancellationRequested();
                if (_closed || !ReferenceEquals(session, _annotation) || !ReferenceEquals(source, _pixels) ||
                    epoch != _annotationPreviewEpoch || version != _annotationPreviewVersion) return;
                var background = _annotationPreviewImage is null
                    ? precision is null
                        ? Task.Run(source.ToArray, token)
                        : ScreenshotAnnotationSession.EncodeHighPrecisionPreviewAsync(precision, token)
                    : null;
                var crop = source;
                if (precision is null && dirty != region)
                {
                    crop = new byte[checked(dirty.Width * dirty.Height * 4)];
                    for (var row = 0; row < dirty.Height; row++)
                    {
                        token.ThrowIfCancellationRequested();
                        Buffer.BlockCopy(source, ((dirty.Y + row) * region.Width + dirty.X) * 4,
                            crop, row * dirty.Width * 4, dirty.Width * 4);
                    }
                }

                var pixels = precision is null
                    ? await session.ComposePreviewAsync(dirty, crop, token)
                    : await session.ComposeHighPrecisionPreviewAsync(region, precision, dirty, token);
                var initialPixels = background is null ? null : await background;
                token.ThrowIfCancellationRequested();
                if (_closed || !ReferenceEquals(session, _annotation) ||
                    !ReferenceEquals(session, _annotationPreviewSession) ||
                    !ReferenceEquals(source, _pixels) || epoch != _annotationPreviewEpoch ||
                    (version != _annotationPreviewVersion && !(drawing && session.IsDrawing))) return;
                var image = _annotationPreviewImage;
                if (image is null)
                {
                    image = new(region.Width, region.Height, initialPixels!,
                        AlphaFormat.Unpremul);
                    _annotationPreviewImage = image;
                }

                image.ApplyPatch(dirty, pixels);
                _annotationPreviewBounds = bounds;
                if (_annotationSurface is { } surface) surface.RenderContent = false;
                UpdateVisibleRegion();
                _annotationPreviewDisplayedVersion = version;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
            }
            catch (ObjectDisposedException) when (_closed || !ReferenceEquals(session, _annotation))
            {
            }
            catch (Exception error)
            {
                if (!_closed && ReferenceEquals(session, _annotation) && version == _annotationPreviewVersion)
                {
                    _status.Text = error.Message;
                    _status.IsVisible = true;
                    _annotationPreviewDisplayedVersion = version;
                }
            }
            finally
            {
                work.Dispose();
                if (ReferenceEquals(_annotationPreviewWork, work)) _annotationPreviewWork = null;
                _annotationPreviewRunning = false;
                if (!_closed && _annotationPreviewSession is { } current && ReferenceEquals(current, _annotation) &&
                    NeedsAnnotationPreview(current) && !current.IsBusy &&
                    _annotationPreviewDisplayedVersion != _annotationPreviewVersion)
                    _annotationPreviewTimer?.Start();
            }
        }

        private static PixelRect UnionPreviewBounds(PixelRect left, PixelRect right)
        {
            if (left.IsEmpty) return right;
            if (right.IsEmpty) return left;
            var x = Math.Min(left.X, right.X);
            var y = Math.Min(left.Y, right.Y);
            return new(x, y, Math.Max(left.Right, right.Right) - x, Math.Max(left.Bottom, right.Bottom) - y);
        }

        private bool NeedsAnnotationPreview(ScreenshotAnnotationSession session)
        {
            return (HasAnnotationPrecisionSource &&
                    (session is { HasAnnotations: true, AnnotationsVisible: true } || session.IsDrawing)) ||
                   session is { HasMosaicAnnotations: true } or
                   {
                       IsDrawing: true, Tool: ScreenshotTool.Mosaic or ScreenshotTool.Blur
                       or ScreenshotTool.Eraser or ScreenshotTool.Magnifier,
                   };
        }
    }
}
