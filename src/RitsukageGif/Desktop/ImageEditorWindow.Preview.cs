using Avalonia;
using Avalonia.Threading;

namespace RitsukageGif.Desktop
{
    internal sealed partial class ImageEditorWindow
    {
        private void QueuePreview()
        {
            if (_closed || _documentChanging) return;
            _previewWork?.Cancel();
            var version = ++_previewVersion;
            if (!_retainDocumentPreview && !Session.HasMosaicAnnotations) _canvas.ClearPreview();
            RefreshSurfaceRendering();
            QueueHdrPreview();
            if (Session.IsBusy) return;
            Dispatcher.UIThread.Post(() =>
            {
                if (!_closed && version == _previewVersion) _ = UpdateSdrPreviewAsync(version);
            }, DispatcherPriority.Background);
        }

        private async Task UpdateSdrPreviewAsync(long version)
        {
            var visible = _viewport.ExactVisibleContentRect;
            if (visible.Width <= 0 || visible.Height <= 0) return;
            var size = PreviewPixelSize(visible);
            using var work = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            _previewWork = work;
            try
            {
                byte[] pixels;
                await _previewGate.WaitAsync(work.Token);
                try
                {
                    if (_closed || version != _previewVersion) return;
                    pixels = await Session.ComposeImageSdrPreviewAsync(_sourcePng, _sourcePixels,
                        visible, size, work.Token);
                }
                finally
                {
                    _previewGate.Release();
                }

                if (_closed || version != _previewVersion || work.IsCancellationRequested) return;
                _canvas.SetPreview(visible, size, pixels);
                _retainDocumentPreview = false;
                RefreshSurfaceRendering();
            }
            catch (OperationCanceledException) when (work.IsCancellationRequested)
            {
            }
            catch (Exception error)
            {
                if (!_closed && version == _previewVersion) SetEditorError(error);
            }
            finally
            {
                if (ReferenceEquals(_previewWork, work)) _previewWork = null;
            }
        }

        private PixelSize PreviewPixelSize(Rect visible, double? zoom = null)
        {
            var width = Math.Max(1, Math.Ceiling(visible.Width * (zoom ?? _viewport.Zoom)));
            var height = Math.Max(1, Math.Ceiling(visible.Height * (zoom ?? _viewport.Zoom)));
            const double pixelBudget = ScreenshotAnnotationSession.MaximumImagePreviewPixels;
            if (width * height > pixelBudget)
            {
                var scale = Math.Sqrt(pixelBudget / (width * height));
                width = Math.Max(1, Math.Floor(width * scale));
                height = Math.Max(1, Math.Floor(height * scale));
            }

            return new((int)width, (int)height);
        }
    }
}
