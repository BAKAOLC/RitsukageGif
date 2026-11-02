using Avalonia;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace RitsukageGif.Desktop
{
    internal sealed partial class ImageEditorWindow
    {
        private Rect _hdrComposedBounds;
        private CapturedFrame? _hdrComposedFrame;
        private PixelSize _hdrComposedSize;
        private long _hdrComposedVersion;
        private bool _hdrPencilVisible;
        private Point? _hdrPointer;
        private HdrPreviewHost? _hdrPreview;
        private bool _hdrPreviewPending;
        private long _hdrPreviewVersion;
        private CancellationTokenSource? _hdrPreviewWork;

        partial void InitializeHdrPreview()
        {
            if (_hdrSource is null || !HdrPreviewHost.IsSupported) return;
            _hdrPreview = new()
                { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
            _hdrPreview.PresentationChanged += HdrPresentationChanged;
            _hdrPreview.DisplayColorChanged += QueueHdrPreview;
            _imageLayer.Children.Insert(_imageLayer.Children.IndexOf(_surface), _hdrPreview);
            _surface.AddHandler(PointerMovedEvent, HdrPointerMoved, RoutingStrategies.Bubble, true);
            _surface.PointerExited += HdrPointerExited;
            HdrPresentationChanged(false);
            QueueHdrPreview();
        }

        private void HdrPointerMoved(object? sender, PointerEventArgs args)
        {
            var previous = _hdrPointer;
            _hdrPointer = args.GetPosition(_imageLayer);
            if (_hdrPreview?.IsVisible != true) return;
            var pencil = Session.PointerCursor == StandardCursorType.None;
            if ((pencil && previous != _hdrPointer) || (_hdrPencilVisible && !pencil)) QueueHdrPreview();
        }

        private void HdrPointerExited(object? sender, PointerEventArgs args)
        {
            var clear = _hdrPencilVisible ||
                        (_hdrPointer is not null && Session.PointerCursor == StandardCursorType.None);
            _hdrPointer = null;
            if (_hdrPreview?.IsVisible == true && clear) QueueHdrPreview();
        }

        private void HdrPresentationChanged(bool visible)
        {
            SetNativePreviewVisible(visible);
            if (visible) QueueHdrPreview();
            else _hdrPencilVisible = false;
            SetEditorStatus(UiText.Get(visible ? "PinnedImageHdrPreview" : "PinnedImageSdrPreview"));
        }

        partial void QueueHdrPreview()
        {
            if (_closed || _outputBusy || _hdrPreview is null) return;
            _hdrPreviewWork?.Cancel();
            ++_hdrPreviewVersion;
            if (RequiresSdrEditorPreview)
            {
                _hdrPreview.SetFrame(null);
                HdrPresentationChanged(false);
                return;
            }

            var visible = _viewport.ExactVisibleContentRect;
            _hdrPreview.Margin = new(visible.X, visible.Y, 0, 0);
            _hdrPreview.Width = visible.Width;
            _hdrPreview.Height = visible.Height;
            _hdrPreview.TryUpdateNativeControlPosition();
            if (_hdrPreviewPending) return;
            _hdrPreviewPending = true;
            Dispatcher.UIThread.Post(() =>
            {
                _hdrPreviewPending = false;
                if (!_closed && !RequiresSdrEditorPreview) _ = UpdateHdrPreviewAsync(_hdrPreviewVersion);
            }, DispatcherPriority.Render);
        }

        private async Task UpdateHdrPreviewAsync(long version)
        {
            if (_hdrSource is not { } original || _hdrPreview is null) return;
            var visible = _viewport.ExactVisibleContentRect;
            if (visible.Width <= 0 || visible.Height <= 0)
            {
                _hdrPreview.SetFrame(null);
                return;
            }

            var size = PreviewPixelSize(visible);
            using var work = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            _hdrPreviewWork = work;
            var entered = false;
            try
            {
                await _previewGate.WaitAsync(work.Token);
                entered = true;
                if (_closed || version != _hdrPreviewVersion || RequiresSdrEditorPreview) return;
                var contentVersion = _previewVersion;
                if (_hdrComposedFrame is null || _hdrComposedVersion != contentVersion ||
                    _hdrComposedBounds != visible || _hdrComposedSize != size)
                {
                    var composed = await Session.ComposeImageHdrPreviewAsync(original, visible, size, work.Token);
                    work.Token.ThrowIfCancellationRequested();
                    _hdrComposedFrame = composed;
                    _hdrComposedVersion = contentVersion;
                    _hdrComposedBounds = visible;
                    _hdrComposedSize = size;
                }

                var frame = _hdrComposedFrame!;
                work.Token.ThrowIfCancellationRequested();
                using var transient = new RenderTargetBitmap(size, new(96, 96));
                using (var context = transient.CreateDrawingContext())
                {
                    using var transform = context.PushTransform(new(size.Width / visible.Width, 0,
                        0, size.Height / visible.Height, -visible.X * size.Width / visible.Width,
                        -visible.Y * size.Height / visible.Height));
                    Session.RenderSelection(context, RenderScaling / _viewport.Zoom);
                    if (_hdrPointer is { } pointer)
                        Session.RenderPencilCursor(context, pointer, RenderScaling / _viewport.Zoom);
                }

                using var overlay = new ImagePreviewFramebuffer(size, true);
                transient.CopyPixels(overlay);
                var white = HdrPreviewHost.DisplayWhiteLevel(TryGetPlatformHandle()!.Handle,
                    frame.ColorInfo.SdrWhiteNits / FrameColorInfo.ScRgbReferenceWhiteNits);
                using var preview = await Task.Run(() => HdrImagePreviewBuilder.Build(frame, visible, overlay.Pixels,
                    white, null, work.Token), work.Token);
                if (_closed || version != _hdrPreviewVersion || RequiresSdrEditorPreview ||
                    work.IsCancellationRequested) return;
                _hdrPreview.SetFrame(preview);
                _hdrPencilVisible = _hdrPreview.IsVisible && _hdrPointer is not null &&
                                    Session.PointerCursor == StandardCursorType.None;
            }
            catch (OperationCanceledException) when (work.IsCancellationRequested)
            {
            }
            catch (Exception error)
            {
                if (!_closed && version == _hdrPreviewVersion)
                {
                    _hdrPreview.SetFrame(null);
                    HdrPresentationChanged(false);
                    SetEditorError(error);
                }
            }
            finally
            {
                if (entered) _previewGate.Release();
                if (ReferenceEquals(_hdrPreviewWork, work)) _hdrPreviewWork = null;
            }
        }

        partial void StopHdrPreview()
        {
            ++_hdrPreviewVersion;
            _hdrComposedFrame = null;
            _hdrPointer = null;
            _hdrPencilVisible = false;
            _hdrPreviewWork?.Cancel();
            _surface.RemoveHandler(PointerMovedEvent, HdrPointerMoved);
            _surface.PointerExited -= HdrPointerExited;
            if (_hdrPreview is not null)
            {
                _hdrPreview.PresentationChanged -= HdrPresentationChanged;
                _hdrPreview.DisplayColorChanged -= QueueHdrPreview;
                _hdrPreview.Dispose();
                _hdrPreview = null;
            }
        }
    }
}
