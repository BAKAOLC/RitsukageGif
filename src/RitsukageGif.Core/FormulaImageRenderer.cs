using System.Text;
using CSharpMath.Rendering.BackEnd;
using CSharpMath.SkiaSharp;
using SkiaSharp;

namespace RitsukageGif.Core
{
    public static partial class FormulaImageRenderer
    {
        public static bool TryValidate(string latex)
        {
            return CreatePainter(latex, 28, 0xff000000, out _) is not null;
        }

        private static bool TryRenderPng(string latex, out byte[]? png, out string? error,
            float fontSize = 28, uint textColor = 0xff000000)
        {
            png = null;
            var painter = CreatePainter(latex, fontSize, textColor, out error);
            if (painter is null) return false;
            var bounds = painter.Measure();
            var width = checked((int)Math.Ceiling(bounds.Width) + 24);
            var height = checked((int)Math.Ceiling(bounds.Height) + 24);
            using var surface = SKSurface.Create(new SKImageInfo(width, height))
                                ?? throw new InvalidOperationException("Cannot create the formula rendering surface.");
            surface.Canvas.Clear(SKColors.Transparent);
            painter.Draw(surface.Canvas, 12, 12 - bounds.Y);
            using var image = surface.Snapshot();
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            png = data.ToArray();
            return true;
        }

        private static bool TryRenderSvg(string latex, out string? svg, out string? error,
            float fontSize = 28, uint textColor = 0xff000000)
        {
            svg = null;
            var painter = CreatePainter(latex, fontSize, textColor, out error);
            if (painter is null) return false;
            var bounds = painter.Measure();
            using var output = new MemoryStream();
            using (var stream = new SKManagedWStream(output))
            using (var canvas = SKSvgCanvas.Create(SKRect.Create(bounds.Width + 24, bounds.Height + 24), stream))
                painter.Draw(canvas, 12, 12 - bounds.Y);
            svg = Encoding.UTF8.GetString(output.ToArray());
            return true;
        }

        private static MathPainter? CreatePainter(string latex, float fontSize, uint textColor, out string? error)
        {
            error = null;
            if (!float.IsFinite(fontSize) || fontSize is < 1 or > 512)
                throw new ArgumentOutOfRangeException(nameof(fontSize));
            if (string.IsNullOrWhiteSpace(latex))
            {
                error = "The formula is empty.";
                return null;
            }

            latex = FormulaLatexFormatter.Unwrap(latex);
            if (latex.Length > 65536)
            {
                error = "The formula is too long to render.";
                return null;
            }

            var depth = 0;
            for (var index = 0; index < latex.Length; index++)
                switch (latex[index])
                {
                    case '\\':
                        index++;
                        break;
                    case '{' when ++depth > 128:
                        error = "The formula nesting is too deep to render.";
                        return null;
                    case '}':
                        depth = Math.Max(0, depth - 1);
                        break;
                }

            var painter = new MathPainter
            {
                LaTeX = latex, FontSize = fontSize, TextColor = new(textColor), DisplayErrorInline = false,
            };
            if (painter.ErrorMessage is { } syntaxError)
            {
                error = syntaxError;
                return null;
            }

            if (!FormulaFontFallback.TryGet(latex, out var typefaces, out error)) return null;
            if (typefaces.Count > 0)
                painter.LocalTypefaces = Fonts.GlobalTypefaces.Concat(typefaces);
            var bounds = painter.Measure();
            var width = Math.Ceiling(bounds.Width) + 24;
            var height = Math.Ceiling(bounds.Height) + 24;
            if (!double.IsFinite(width) || !double.IsFinite(height) || width <= 24 || height <= 24 ||
                width > 65535 || height > 65535 || width * height > 32_000_000)
            {
                error = "The rendered formula dimensions are not supported.";
                return null;
            }

            return painter;
        }
    }
}
