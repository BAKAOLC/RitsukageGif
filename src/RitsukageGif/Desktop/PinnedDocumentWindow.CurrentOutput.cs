using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;

namespace RitsukageGif.Desktop
{
    internal sealed partial class PinnedDocumentWindow
    {
        private async Task<ScreenshotResult> CaptureCurrentDocumentViewAsync(CancellationToken token)
        {
            _annotationOutputs++;
            ScreenshotAnnotationSession? session = null;
            var wasBusy = false;
            try
            {
                if (_annotationRefreshing) await _annotationRefresh.WaitAsync(token);
                _lifetime.Token.ThrowIfCancellationRequested();
                if (Document is PinnedFormulaDocument)
                {
                    if (_formulaPngTask is { } pending) await pending.WaitAsync(token);
                    token.ThrowIfCancellationRequested();
                    if (_documentBitmap is null) throw new InvalidOperationException("The formula cannot be rendered.");
                }

                session = _annotationScene;
                wasBusy = session?.IsBusy ?? false;
                if (session is { HasMosaicAnnotations: true }) await EnsureDocumentAnnotationSourceAsync(token);
                token.ThrowIfCancellationRequested();
                EnsureDocumentSceneLayout();
                UpdateLayout();
                var scaling = RenderScaling;
                var viewScale = DocumentSceneViewScale;
                var size = _isThumbnail ? _thumbnailRegion.Size * viewScale : _viewport.Bounds.Size;
                var origin = _isThumbnail
                    ? _thumbnailRegion.TopLeft
                    : DocumentScenePoint(_viewport.TranslatePoint(default, _content)!.Value);
                var opacity = Opacity;
                var smooth = PreferencesProvider().SmoothScaling;
                var sourceViewport = new Rect(origin, new Size(size.Width / viewScale, size.Height / viewScale));
                session?.SetExportState(true);
                using var bitmap = Document is PinnedFilesDocument
                    ? await RenderFileViewportAsync(size, origin, viewScale, scaling, token)
                    : RenderTextualViewport(size, sourceViewport, viewScale, scaling, token);
                await DrawDocumentAnnotationsAsync(bitmap, origin, viewScale, token);
                var result = await EncodeDocumentBitmapAsync(bitmap, token);
                return await PinnedViewportOutput.ProjectAsync(Task.FromResult(result), new(Matrix.Identity,
                    bitmap.PixelSize, false, opacity, smooth, scaling), token);
            }
            finally
            {
                if (!_closed && ReferenceEquals(session, _annotationScene)) session?.SetExportState(wasBusy);
                _annotationOutputs--;
                TryReleaseDocumentSources();
            }
        }

        private RenderTargetBitmap RenderTextualViewport(Size size, Rect sourceViewport, double viewScale,
            double scaling, CancellationToken token)
        {
            var bitmap = CreateDocumentSceneBitmap(size, scaling);
            try
            {
                using var drawing = bitmap.CreateDrawingContext();
                using var transform = drawing.PushTransform(new(viewScale, 0, 0, viewScale,
                    -sourceViewport.X * viewScale, -sourceViewport.Y * viewScale));
                using var clip = drawing.PushClip(sourceViewport);
                switch (Document)
                {
                    case PinnedFormulaDocument when _documentBitmap is { } formula:
                        drawing.FillRectangle(Brushes.White, sourceViewport.Intersect(new(formula.Size)));
                        drawing.DrawImage(formula, new(formula.Size));
                        break;
                    case PinnedColorDocument:
                        DrawColorViewport(drawing, sourceViewport, token);
                        break;
                    default:
                        switch (_textView)
                        {
                            case PinnedRichView rich:
                                drawing.FillRectangle(AppTheme.Surface, sourceViewport.Intersect(new(rich.SceneSize)));
                                rich.DrawSceneViewport(drawing, sourceViewport, token);
                                break;
                            case PinnedTextView text:
                                drawing.FillRectangle(AppTheme.Surface, sourceViewport.Intersect(new(text.SceneSize)));
                                text.DrawSceneViewport(drawing, sourceViewport, token);
                                break;
                        }

                        break;
                }

                return bitmap;
            }
            catch
            {
                bitmap.Dispose();
                throw;
            }
        }

        private void DrawColorViewport(DrawingContext drawing, Rect sourceViewport, CancellationToken token)
        {
            var content = (Grid)_surface.Child!;
            var swatch = (Border)content.Children[0];
            drawing.FillRectangle(swatch.Background!, swatch.Bounds.Intersect(sourceViewport));
            var text = (PinnedTextView)_textView!;
            var position = text.TextArea.TextView.TranslatePoint(default, content)!.Value;
            var offset = new Vector(position.X - text.ScrollOffset.X, position.Y - text.ScrollOffset.Y);
            using var transform = drawing.PushTransform(Matrix.CreateTranslation(offset.X, offset.Y));
            text.DrawSceneViewport(drawing, sourceViewport.Translate(-offset), token);
        }

        private async Task<RenderTargetBitmap> RenderFileViewportAsync(Size size, Point origin, double viewScale,
            double scaling, CancellationToken token)
        {
            var grid = CreateFileGrid(false, viewScale);
            var transformed = new LayoutTransformControl
            {
                Child = grid, LayoutTransform = new ScaleTransform(viewScale, viewScale),
            };
            Canvas.SetLeft(transformed, -origin.X * viewScale);
            Canvas.SetTop(transformed, -origin.Y * viewScale);
            var frame = new Canvas { Width = size.Width, Height = size.Height, ClipToBounds = true };
            frame.Children.Add(transformed);
            var host = new Canvas { Opacity = 0, IsHitTestVisible = false };
            host.Children.Add(frame);
            _content.Children.Add(host);
            using var scene = new PinnedFileSceneLease(_content, host, grid);
            frame.ApplyStyling();
            frame.ApplyTemplate();
            frame.Measure(size);
            frame.Arrange(new(default, size));
            frame.UpdateLayout();
            foreach (var row in grid.GetVisualDescendants().OfType<PinnedFileRowButton>())
                await row.EnsureIconAsync(token);
            token.ThrowIfCancellationRequested();
            var bitmap = CreateDocumentSceneBitmap(size, scaling);
            try
            {
                bitmap.Render(frame);
                return bitmap;
            }
            catch
            {
                bitmap.Dispose();
                throw;
            }
        }
    }
}
