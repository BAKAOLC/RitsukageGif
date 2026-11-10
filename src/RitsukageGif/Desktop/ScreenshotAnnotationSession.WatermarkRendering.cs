using System.Globalization;
using Avalonia;
using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    internal sealed partial class ScreenshotAnnotationSession
    {
        private static FormattedText FormatWatermark(ScreenshotAnnotation annotation)
        {
            return new(annotation.DisplayText, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                CreateTypeface(annotation.Properties), annotation.FontSize * 4 / 3,
                new SolidColorBrush(annotation.Color));
        }

        private void DrawTextWatermark(DrawingContext context, ScreenshotAnnotation annotation)
        {
            if (string.IsNullOrEmpty(annotation.DisplayText)) return;
            var text = FormatWatermark(annotation);
            var properties = annotation.Properties;
            var lineHeight = annotation.FontSize * 4 / 3;
            var padding = properties.Tiled ? lineHeight * properties.WatermarkSpacing / 50 : lineHeight * .1;
            var size = new Size(Math.Max(1, text.Width + padding * 2), Math.Max(1, text.Height + padding * 2));
            var bounds = new Rect(AnnotationBounds.X, AnnotationBounds.Y, AnnotationBounds.Width,
                AnnotationBounds.Height);
            using var clip = context.PushClip(bounds);
            using var opacity = context.PushOpacity(properties.Opacity);
            if (!properties.Tiled)
            {
                var origin = properties.NineGrid ? WatermarkOrigin(size, properties.Anchor) : annotation.Points[0];
                context.DrawText(text, origin + new Vector(padding, padding));
                return;
            }

            var rotation = Matrix.CreateRotation(properties.WatermarkAngle * Math.PI / 180);
            var local = new Rect(bounds.Size).TransformToAABB(rotation.Invert());
            using var transform = context.PushTransform(rotation * Matrix.CreateTranslation(bounds.X, bounds.Y));
            var top = Math.Floor(local.Top / size.Height) * size.Height;
            var left = Math.Floor(local.Left / size.Width) * size.Width;
            for (var y = top; y < local.Bottom; y += size.Height)
            for (var x = left; x < local.Right; x += size.Width)
                context.DrawText(text, new(x + padding, y + padding));
        }
    }
}
