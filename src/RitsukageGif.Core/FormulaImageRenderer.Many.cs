using System.Text;
using CSharpMath.SkiaSharp;
using SkiaSharp;

namespace RitsukageGif.Core
{
    public static partial class FormulaImageRenderer
    {
        public static bool TryRenderManyPng(IReadOnlyList<string> sources, out byte[]? png, out string? error,
            float fontSize = 28, uint textColor = 0xff000000)
        {
            png = null;
            if (sources.Count == 1) return TryRenderPng(sources[0], out png, out error, fontSize, textColor);
            var painters = CreatePainters(sources, fontSize, textColor, out var width, out var height, out error);
            if (painters is null) return false;
            using var surface = SKSurface.Create(new SKImageInfo(width, height))
                                ?? throw new InvalidOperationException("Cannot create the formula rendering surface.");
            surface.Canvas.Clear(SKColors.Transparent);
            DrawPainters(surface.Canvas, painters, width);
            using var image = surface.Snapshot();
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            png = data.ToArray();
            return true;
        }

        public static bool TryRenderManySvg(IReadOnlyList<string> sources, out string? svg, out string? error,
            float fontSize = 28, uint textColor = 0xff000000)
        {
            svg = null;
            if (sources.Count == 1) return TryRenderSvg(sources[0], out svg, out error, fontSize, textColor);
            var painters = CreatePainters(sources, fontSize, textColor, out var width, out var height, out error);
            if (painters is null) return false;
            using var output = new MemoryStream();
            using (var stream = new SKManagedWStream(output))
            using (var canvas = SKSvgCanvas.Create(SKRect.Create(width, height), stream))
                DrawPainters(canvas, painters, width);
            svg = Encoding.UTF8.GetString(output.ToArray());
            return true;
        }

        private static List<MathPainter>? CreatePainters(IReadOnlyList<string> sources, float fontSize,
            uint textColor, out int width, out int height, out string? error)
        {
            width = height = 0;
            error = null;
            if (sources.Count == 0)
            {
                error = "There are no formulas to render.";
                return null;
            }

            var painters = new List<MathPainter>(sources.Count);
            double totalHeight = 24, maximumWidth = 0;
            foreach (var source in sources)
            {
                var painter = CreatePainter(source, fontSize, textColor, out error);
                if (painter is null) return null;
                var bounds = painter.Measure();
                totalHeight += Math.Ceiling(bounds.Height) + (painters.Count == 0 ? 0 : 16);
                maximumWidth = Math.Max(maximumWidth, Math.Ceiling(bounds.Width));
                if (totalHeight > 65535 || (maximumWidth + 24) * totalHeight > 32_000_000)
                {
                    error = "The rendered formula dimensions are not supported.";
                    return null;
                }

                painters.Add(painter);
            }

            width = checked((int)maximumWidth + 24);
            height = checked((int)totalHeight);
            return painters;
        }

        private static void DrawPainters(SKCanvas canvas, IReadOnlyList<MathPainter> painters, int width)
        {
            float y = 12;
            foreach (var painter in painters)
            {
                var bounds = painter.Measure();
                painter.Draw(canvas, (width - bounds.Width) / 2, y - bounds.Y);
                y += (float)Math.Ceiling(bounds.Height) + 16;
            }
        }
    }
}
