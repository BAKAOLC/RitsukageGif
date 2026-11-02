using Avalonia;

namespace RitsukageGif.Desktop
{
    internal sealed partial class ImageEditorWindow
    {
        private async Task<ImageEditorDocumentView> PrepareDocumentViewAsync(PinnedImageContent image,
            CapturedFrame? hdr,
            ScreenshotAnnotationSession session, CancellationToken token)
        {
            var bounds = new PixelRect(0, 0, image.Width, image.Height);
            session.CanOutputHdr = hdr is not null;
            session.RegisterFrame(bounds, image.Pixels);
            session.NotifySelection(bounds);
            var canvas = new ImageEditorCanvas(image.Width, image.Height, image.Pixels);
            var surface = new ScreenshotAnnotationSurface(session, bounds, 1, args =>
            {
                var point = args.GetPosition(_imageLayer);
                return new((int)Math.Floor(point.X), (int)Math.Floor(point.Y));
            });
            try
            {
                if (session.HasMosaicAnnotations)
                {
                    var full = new Rect(0, 0, image.Width, image.Height);
                    var scale = Math.Min(1, Math.Min(Math.Max(1, _viewport.Bounds.Width * RenderScaling) / image.Width,
                        Math.Max(1, _viewport.Bounds.Height * RenderScaling) / image.Height));
                    var target = PreviewPixelSize(full, scale);
                    var png = image.Hdr is null ? image.Png : image.SdrPng ?? image.Png;
                    var preview = await session.ComposeImageSdrPreviewAsync(png, image.Pixels, full, target, token);
                    canvas.SetPreview(full, target, preview);
                    surface.RenderContent = false;
                }

                var toolbar = new ScreenshotToolbar(session, bounds, 1, SelectionCommands.SnapshotBindings(),
                        ExecuteCommand, true)
                    { IsDocked = true, Margin = new(6) };
                return new(image, hdr, session, canvas, surface, toolbar);
            }
            catch
            {
                surface.Dispose();
                canvas.Dispose();
                throw;
            }
        }

        private void DetachDocumentView()
        {
            Session.Changed -= SessionChanged;
            Session.TextEditStateChanged -= QueuePreview;
            StopHdrPreview();
        }

        private void InstallDocumentView(ImageEditorDocumentView view)
        {
            var previousCanvas = _canvas;
            var previousSurface = _surface;
            var previousToolbar = _toolbar;
            _sourceBounds = new(0, 0, view.Image.Width, view.Image.Height);
            _sourcePng = view.Image.Hdr is null ? view.Image.Png : view.Image.SdrPng ?? view.Image.Png;
            _sourcePixels = view.Image.Pixels;
            _hdrSource = view.Hdr;
            Session = view.Session;
            _canvas = view.Canvas;
            _surface = view.Surface;
            _toolbar = view.Toolbar;
            _retainDocumentPreview = _canvas.HasPreview;
            _imageLayer.Children.Clear();
            _imageLayer.Width = _sourceBounds.Width;
            _imageLayer.Height = _sourceBounds.Height;
            _imageLayer.Children.Add(_canvas);
            _imageLayer.Children.Add(_surface);
            AttachStripSurface();
            var index = _editorRoot.Children.IndexOf(previousToolbar);
            _editorRoot.Children.RemoveAt(index);
            _editorRoot.Children.Insert(index, _toolbar);
            previousToolbar.Dispose();
            previousSurface.Dispose();
            previousCanvas.Dispose();
            AttachDocumentSession();
            if (_servicesConnected) ConnectServices();
            AddDocumentButtons();
            _viewport.PixelSize = new(_sourceBounds.Width, _sourceBounds.Height);
            SetStripMode(null);
            InitializeHdrPreview();
            ViewChanged();
        }
    }
}
