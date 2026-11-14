using Avalonia;
using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    internal sealed partial class OverlayCanvas
    {
        private Action<DrawingContext, Rect>? _magnifierScenePainter;

        private bool MagnifierSceneMayCover(Rect region)
        {
            if (MagnifierAnnotations is { } annotations)
            {
                var preview = annotations.GetPreviewBounds();
                var previewBounds = new Rect(preview.X, preview.Y, preview.Width, preview.Height);
                if (CapturePreferences.MagnifierAnnotations && annotations.AnnotationsVisible &&
                    (annotations.Underlay is not null || (!preview.IsEmpty && previewBounds.Intersects(region))))
                    return true;
                if (CapturePreferences.MagnifierAnnotationControls && annotations is { IsEditingText: false } and
                        ({ IsPrivacyDetectionActive: true } or
                        { AnnotationsVisible: true, IsBusy: false, HasSelectedAnnotation: true }))
                    return true;
            }

            if (Selected.IsEmpty) return false;
            if (MagnifierSelectionHandlesVisible && MagnifierHandlesMayCover(region)) return true;
            if (!CapturePreferences.MagnifierSelectionBorder) return false;
            var (geometry, pen) = SelectionBorder();
            if (pen is null) return false;
            var pad = SelectionScenePadding(pen, false);
            var bounds = new Rect(Selected.X, Selected.Y, Selected.Width, Selected.Height);
            if (!bounds.Inflate(pad).Intersects(region)) return false;
            if (geometry is null) return !bounds.Deflate(pad).Contains(region);
            return !IsScreenshotRectangle || !bounds.Deflate(pad + FrameRadiusPixels).Contains(region);
        }

        private void PaintMagnifierScene(DrawingContext context, Rect source)
        {
            var target = new Rect(0, 0, source.Width, source.Height);
            context.FillRectangle(Brushes.Black, target);
            DrawMagnifierBackground(context, source, target, 1);
            DrawMagnifierAnnotations(context, target.Center, 1, Mouse);
            DrawMagnifiedSelection(context, target, 1, Mouse);
        }

        private void DrawMagnifierBackground(DrawingContext context, Rect source, Rect lens, double factor)
        {
            var available = source.Intersect(new(_monitor.X, _monitor.Y, _monitor.Width, _monitor.Height));
            if (available is { Width: > 0, Height: > 0 })
                context.DrawImage(_bitmap,
                    new(available.X - _monitor.X, available.Y - _monitor.Y, available.Width, available.Height),
                    new(lens.X + (available.X - source.X) * factor, lens.Y + (available.Y - source.Y) * factor,
                        available.Width * factor, available.Height * factor));
            if (WindowFrame is not { } frame) return;
            var visible =
                source.Intersect(new(frame.Region.X, frame.Region.Y, frame.Region.Width, frame.Region.Height));
            if (visible is { Width: > 0, Height: > 0 })
                context.DrawImage(frame.Bitmap,
                    new(visible.X - frame.Region.X, visible.Y - frame.Region.Y, visible.Width, visible.Height),
                    new(lens.X + (visible.X - source.X) * factor, lens.Y + (visible.Y - source.Y) * factor,
                        visible.Width * factor, visible.Height * factor));
        }
    }
}
