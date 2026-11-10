using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    internal sealed partial class ScreenshotAnnotationSession
    {
        public ScreenshotTool? SelectedTool => _selected is { } annotation ? AnnotationTool(annotation) : null;

        private static ScreenshotTool AnnotationTool(ScreenshotAnnotation annotation)
        {
            return annotation.AutoMosaicId != Guid.Empty ? ScreenshotTool.AutoMosaic : annotation.Tool;
        }

        public Task UpdateSelectedStyleAsync(Color? color = null, double? thickness = null, double? fontSize = null,
            int? mosaicSize = null)
        {
            if (_disposed || !CanAnnotate || IsBusy || (IsDrawing && !IsDrawingPolyline)) return Task.CompletedTask;
            var tool = SelectedTool ?? Tool;
            var brush = tool == ScreenshotTool.Brush || (IsPixelTool(tool) &&
                                                         (_selected?.Properties ?? Properties).PixelPath ==
                                                         AnnotationPixelPath.Brush);
            if (thickness is { } width && (!double.IsFinite(width) || width < 1 || width >
                    (brush ? MaximumBrushThickness : 1024)))
                throw new ArgumentOutOfRangeException(nameof(thickness));
            if (fontSize is { } size && (!double.IsFinite(size) || size is < 1 or > 1024))
                throw new ArgumentOutOfRangeException(nameof(fontSize));
            if (mosaicSize is < 2 or > 28) throw new ArgumentOutOfRangeException(nameof(mosaicSize));

            if (color is { } newColor)
            {
                Color = newColor;
                if (_sharedColor && tool != ScreenshotTool.Highlight) _masterColor = newColor;
            }

            if (thickness is { } newThickness) Thickness = newThickness;
            if (fontSize is { } newFontSize) FontSize = newFontSize;
            if (mosaicSize is { } newMosaicSize) MosaicSize = newMosaicSize;
            if (_gesture is { Tool: ScreenshotTool.Polyline } draft)
            {
                _gesture = CloneAnnotation(draft, color: Color, thickness: Thickness);
                draft.Dispose();
                StoreToolStyle(ScreenshotTool.Polyline);
                Changed?.Invoke();
                return Task.CompletedTask;
            }

            if (IsEditingText)
            {
                NotifyTextEditState();
                return Task.CompletedTask;
            }

            if (ActiveStyleAnnotation is not { } annotation)
            {
                Changed?.Invoke();
                return Task.CompletedTask;
            }

            var updatedColor = annotation.Tool == ScreenshotTool.Mosaic
                ? annotation.Color
                : color ?? annotation.Color;
            var updatedThickness = annotation.Tool == ScreenshotTool.Text
                ? annotation.Thickness
                : thickness ?? annotation.Thickness;
            var updatedFontSize =
                annotation.Tool is ScreenshotTool.Text or ScreenshotTool.Number or ScreenshotTool.Watermark
                    ? fontSize ?? annotation.FontSize
                    : annotation.FontSize;
            var updatedMosaicSize = annotation.Tool == ScreenshotTool.Mosaic
                ? mosaicSize ?? annotation.MosaicSize
                : annotation.MosaicSize;
            var index = _annotations.IndexOf(annotation);
            if (index < 0 || (updatedColor == annotation.Color && updatedThickness.Equals(annotation.Thickness) &&
                              updatedFontSize.Equals(annotation.FontSize) &&
                              updatedMosaicSize == annotation.MosaicSize))
            {
                Changed?.Invoke();
                return Task.CompletedTask;
            }

            return ReplaceAnnotationAsync(index, CloneAnnotation(annotation, color: updatedColor,
                fontSize: updatedFontSize, thickness: updatedThickness, mosaicSize: updatedMosaicSize));
        }
    }
}
