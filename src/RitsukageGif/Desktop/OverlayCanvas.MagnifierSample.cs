using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.Immutable;

namespace RitsukageGif.Desktop
{
    internal sealed partial class OverlayCanvas
    {
        private RenderTargetBitmap? _magnifierSample;
        private ImagePreviewFramebuffer? _magnifierSamplePixels;
        private ImmutablePen? _magnifierStrokeProbe;

        internal bool TryGetSampleColor(PixelPoint point, out Color color)
        {
            if (!TryGetSourceSampleColor(point, out color)) return false;
            var sampleSelection = MagnifiedSelectionMayCover(point);
            var sampleAnnotations = MagnifierAnnotations is { } annotations &&
                                    ((CapturePreferences.MagnifierAnnotations && annotations.AnnotationsVisible &&
                                      (annotations.Underlay is not null ||
                                       annotations.GetPreviewBounds().Contains(point))) ||
                                     (CapturePreferences.MagnifierAnnotationControls &&
                                      (annotations.HasSelectedAnnotation || annotations.IsPrivacyDetectionActive)));
            if (!sampleSelection && !sampleAnnotations) return true;

            var sample = _magnifierSample ??= new(new(1, 1), new(96, 96));
            var pixels = _magnifierSamplePixels ??= new(new(1, 1));
            var area = new Rect(0, 0, 1, 1);
            using (var context = sample.CreateDrawingContext())
            {
                context.FillRectangle(new SolidColorBrush(color), area);
                if (sampleAnnotations) DrawMagnifierAnnotations(context, area.Center, 1, point);
                if (sampleSelection) DrawMagnifiedSelection(context, area, 1, point);
            }

            sample.CopyPixels(pixels);
            color = Color.FromRgb(pixels.Pixels[2], pixels.Pixels[1], pixels.Pixels[0]);
            return true;
        }

        private bool MagnifiedSelectionMayCover(PixelPoint point)
        {
            if (Selected.IsEmpty) return false;
            if (MagnifierSelectionHandlesVisible && MagnifierHandlesMayCover(new(point.X, point.Y, 1, 1)))
                return true;
            if (!CapturePreferences.MagnifierSelectionBorder) return false;
            var (geometry, pen) = SelectionBorder();
            if (pen is null) return false;
            var x = point.X + .5;
            var y = point.Y + .5;
            if (geometry is not null)
            {
                var probeWidth = pen.Thickness + 2 / Scale;
                if (_magnifierStrokeProbe is not { } probe || !probe.Thickness.Equals(probeWidth))
                    _magnifierStrokeProbe = probe = new(0xff000000, probeWidth);
                return geometry.StrokeContains(probe, new((x - _monitor.X) / Scale, (y - _monitor.Y) / Scale));
            }

            var pad = SelectionScenePadding(pen, false);
            if (x < Selected.X - pad || x > Selected.Right + pad || y < Selected.Y - pad || y > Selected.Bottom + pad)
                return false;
            return Math.Abs(x - Selected.X) <= pad || Math.Abs(x - Selected.Right) <= pad ||
                   Math.Abs(y - Selected.Y) <= pad || Math.Abs(y - Selected.Bottom) <= pad;
        }
    }
}
