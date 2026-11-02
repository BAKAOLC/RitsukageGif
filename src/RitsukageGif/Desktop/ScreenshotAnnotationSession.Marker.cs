using System.Buffers.Binary;
using Avalonia;
using Avalonia.Media;
using ImageMagick;

namespace RitsukageGif.Desktop
{
    internal sealed partial class ScreenshotAnnotationSession
    {
        private static bool IsMarkerLine(ScreenshotAnnotation annotation)
        {
            return annotation is { Tool: ScreenshotTool.Highlight, Properties.MarkerPath: AnnotationMarkerPath.Line };
        }

        private static double MarkerAlpha(ScreenshotAnnotation annotation)
        {
            return annotation.Color.A / 255d * (annotation.Properties.MarkerBlend == AnnotationMarkerBlend.Translucent
                ? annotation.Properties.Opacity
                : 1);
        }

        private static void DrawMarker(DrawingContext context, ScreenshotAnnotation annotation)
        {
            var alpha = (byte)Math.Round(MarkerAlpha(annotation) * 255);
            var color = Color.FromArgb(alpha, annotation.Color.R, annotation.Color.G, annotation.Color.B);
            context.Custom(new MarkerDrawOperation(MarkerBounds(annotation), annotation.Points[0],
                annotation.Points[^1], color, annotation.Thickness, annotation.Properties.CornerRadius,
                annotation.Properties.MarkerPath, annotation.Properties.MarkerBlend));
        }

        private static Rect MarkerBounds(ScreenshotAnnotation annotation)
        {
            return IsMarkerLine(annotation)
                ? annotation.Bounds.Inflate(annotation.Thickness / 2 + 1)
                : annotation.Bounds;
        }

        private static bool MarkerContains(ScreenshotAnnotation annotation, Point point)
        {
            if (IsMarkerLine(annotation))
            {
                var start = annotation.Points[0];
                var end = annotation.Points[^1];
                var dx = end.X - start.X;
                var dy = end.Y - start.Y;
                var squared = dx * dx + dy * dy;
                var fraction = squared == 0
                    ? 0
                    : Math.Clamp(((point.X - start.X) * dx +
                                  (point.Y - start.Y) * dy) / squared, 0, 1);
                var distanceX = point.X - start.X - dx * fraction;
                var distanceY = point.Y - start.Y - dy * fraction;
                return distanceX * distanceX + distanceY * distanceY <= annotation.Thickness * annotation.Thickness / 4;
            }

            var bounds = annotation.Bounds;
            if (!bounds.Contains(point)) return false;
            var radius = Math.Min(annotation.Properties.CornerRadius, Math.Min(bounds.Width, bounds.Height) / 2);
            if (radius <= 0) return true;
            var x = Math.Clamp(point.X, bounds.Left + radius, bounds.Right - radius);
            var y = Math.Clamp(point.Y, bounds.Top + radius, bounds.Bottom - radius);
            return Distance(point, new(x, y)) <= radius;
        }

        private static double MarkerCoverage(ScreenshotAnnotation annotation, int x, int y)
        {
            if (IsMarkerLine(annotation))
            {
                const double pixelRadius = .7071067811865476;
                var radius = annotation.Thickness / 2;
                var distance = SegmentDistance(new(x + .5, y + .5), annotation.Points[0], annotation.Points[^1]);
                if (radius >= pixelRadius && distance <= radius - pixelRadius) return 1;
                if (distance >= radius + pixelRadius) return 0;
            }
            else if (annotation.Properties.CornerRadius <= 0)
            {
                var bounds = annotation.Bounds;
                if (x >= bounds.Left && x + 1 <= bounds.Right && y >= bounds.Top && y + 1 <= bounds.Bottom) return 1;
                if (x + 1 <= bounds.Left || x >= bounds.Right || y + 1 <= bounds.Top || y >= bounds.Bottom) return 0;
            }

            var count = 0;
            for (var sy = 0; sy < 4; sy++)
            for (var sx = 0; sx < 4; sx++)
                if (MarkerContains(annotation, new(x + (sx + .5) / 4, y + (sy + .5) / 4)))
                    count++;
            return count / 16d;
        }

        private PixelRect MarkerRegion(ScreenshotAnnotation annotation, PixelRect region)
        {
            var bounds = MarkerBounds(annotation);
            return new PixelRect((int)Math.Floor(bounds.Left), (int)Math.Floor(bounds.Top),
                    (int)Math.Ceiling(bounds.Right) - (int)Math.Floor(bounds.Left),
                    (int)Math.Ceiling(bounds.Bottom) - (int)Math.Floor(bounds.Top))
                .Intersect(region).Intersect(AnnotationBounds);
        }

        private static double MarkerChannel(double behind, double color, double alpha, double behindAlpha,
            bool multiply, double resultAlpha)
        {
            var blend = multiply ? behind * color : color;
            var premultiplied = color * alpha * (1 - behindAlpha) +
                                blend * alpha * behindAlpha + behind * behindAlpha * (1 - alpha);
            return resultAlpha > 0 ? premultiplied / resultAlpha : 0;
        }

        private async Task ApplyMarkerAsync(MagickImage image, PixelRect region, ScreenshotAnnotation annotation,
            CancellationToken token)
        {
            using var marker = CloneAnnotation(annotation);
            var bounds = MarkerRegion(marker, region);
            await Task.Run(() =>
            {
                using var pixels = image.GetPixels();
                const int tileSize = 1024;
                for (var top = bounds.Y; top < bounds.Bottom; top += tileSize)
                for (var left = bounds.X; left < bounds.Right; left += tileSize)
                {
                    var area = new PixelRect(left, top, Math.Min(tileSize, bounds.Right - left),
                        Math.Min(tileSize, bounds.Bottom - top));
                    var data =
                        pixels.GetArea(area.X - region.X, area.Y - region.Y, (uint)area.Width, (uint)area.Height)!;
                    var multiply = marker.Properties.MarkerBlend == AnnotationMarkerBlend.Multiply;
                    var color = marker.Color;
                    for (var y = 0; y < area.Height; y++)
                    {
                        token.ThrowIfCancellationRequested();
                        for (var x = 0; x < area.Width; x++)
                        {
                            var alpha = MarkerAlpha(marker) * MarkerCoverage(marker, area.X + x, area.Y + y);
                            if (alpha <= 0) continue;
                            var offset = (y * area.Width + x) * 4;
                            var behindAlpha = data[offset + 3] / 255d;
                            var resultAlpha = alpha + behindAlpha * (1 - alpha);
                            for (var channel = 0; channel < 3; channel++)
                            {
                                var value = channel switch { 0 => color.R, 1 => color.G, _ => color.B };
                                data[offset + channel] = (byte)Math.Clamp(Math.Round(MarkerChannel(
                                    data[offset + channel] / 255d, value / 255d, alpha, behindAlpha, multiply,
                                    resultAlpha) * 255), 0, 255);
                            }

                            data[offset + 3] = (byte)Math.Round(resultAlpha * 255);
                        }
                    }

                    pixels.SetArea(area.X - region.X, area.Y - region.Y, (uint)area.Width, (uint)area.Height, data);
                }
            }, token);
        }

        private Task ApplyHighPrecisionMarkerAsync(byte[] output, PngRaster source, PixelRect region,
            ScreenshotAnnotation annotation, CancellationToken token)
        {
            return Task.Run(() =>
            {
                var area = MarkerRegion(annotation, region);
                var color = annotation.Color;
                var converted = PngColorTransform.OverlayToSource([color.B, color.G, color.R, 255], 1, 1,
                    source.Metadata, token);
                var multiply = annotation.Properties.MarkerBlend == AnnotationMarkerBlend.Multiply;
                for (var y = area.Y; y < area.Bottom; y++)
                {
                    token.ThrowIfCancellationRequested();
                    for (var x = area.X; x < area.Right; x++)
                    {
                        var alpha = MarkerAlpha(annotation) * MarkerCoverage(annotation, x, y);
                        if (alpha <= 0) continue;
                        var pixel = output.AsSpan(((y - region.Y) * source.Width + x - region.X) * 8, 8);
                        var behindAlpha = BinaryPrimitives.ReadUInt16BigEndian(pixel[6..]) / 65535d;
                        var resultAlpha = alpha + behindAlpha * (1 - alpha);
                        for (var channel = 0; channel < 3; channel++)
                        {
                            var behind = BinaryPrimitives.ReadUInt16BigEndian(pixel[(channel * 2)..]) / 65535d;
                            var foreground = converted[channel] / 65535d;
                            var value = MarkerChannel(behind, foreground, alpha, behindAlpha, multiply, resultAlpha);
                            BinaryPrimitives.WriteUInt16BigEndian(pixel[(channel * 2)..],
                                (ushort)Math.Clamp(Math.Round(value * 65535), 0, 65535));
                        }

                        BinaryPrimitives.WriteUInt16BigEndian(pixel[6..], (ushort)Math.Round(resultAlpha * 65535));
                    }
                }
            }, token);
        }

        private Task ApplyHdrMarkerAsync(byte[] output, CapturedFrame source, PixelRect region,
            ScreenshotAnnotation annotation, CancellationToken token)
        {
            return Task.Run(() =>
            {
                var area = MarkerRegion(annotation, region);
                var color = annotation.Color;
                var multiply = annotation.Properties.MarkerBlend == AnnotationMarkerBlend.Multiply;
                var linearRed = MarkerToLinear(color.R);
                var linearGreen = MarkerToLinear(color.G);
                var linearBlue = MarkerToLinear(color.B);
                for (var y = area.Y; y < area.Bottom; y++)
                {
                    token.ThrowIfCancellationRequested();
                    for (var x = area.X; x < area.Right; x++)
                    {
                        var alpha = MarkerAlpha(annotation) * MarkerCoverage(annotation, x, y);
                        if (alpha <= 0) continue;
                        var px = x - region.X;
                        var py = y - region.Y;
                        var pixel = output.AsSpan(py * source.Stride + px * 8, 8);
                        var behindAlpha = source.AlphaMode == CaptureAlphaMode.Opaque
                            ? 1
                            : Math.Clamp(ReadMarkerHalf(pixel[6..]), 0, 1);
                        var resultAlpha = alpha + behindAlpha * (1 - alpha);
                        var whiteNits = source.ColorInfo.SdrWhiteNits;
                        foreach (var colorRegion in source.ColorRegions)
                            if (colorRegion.Region.Contains(new(px, py)))
                            {
                                whiteNits = colorRegion.ColorInfo.SdrWhiteNits;
                                break;
                            }

                        var white = whiteNits / FrameColorInfo.ScRgbReferenceWhiteNits;
                        for (var channel = 0; channel < 3; channel++)
                        {
                            var linear = channel switch { 0 => linearRed, 1 => linearGreen, _ => linearBlue };
                            var behind = ReadMarkerHalf(pixel[(channel * 2)..]);
                            if (source.AlphaMode == CaptureAlphaMode.Premultiplied)
                                behind = behindAlpha > 0 ? behind / behindAlpha : 0;
                            var value = multiply
                                ? MarkerChannel(behind / white, linear, alpha, behindAlpha, true, resultAlpha) * white
                                : MarkerChannel(behind, linear * white, alpha, behindAlpha, false, resultAlpha);
                            if (source.AlphaMode == CaptureAlphaMode.Premultiplied) value *= resultAlpha;
                            WriteMarkerHalf(pixel[(channel * 2)..], value);
                        }

                        WriteMarkerHalf(pixel[6..], source.AlphaMode == CaptureAlphaMode.Opaque ? 1 : resultAlpha);
                    }
                }
            }, token);
        }

        private static double MarkerToLinear(byte value)
        {
            var encoded = value / 255d;
            return encoded <= .04045 ? encoded / 12.92 : Math.Pow((encoded + .055) / 1.055, 2.4);
        }

        private static double ReadMarkerHalf(ReadOnlySpan<byte> pixel)
        {
            var value = (double)BitConverter.UInt16BitsToHalf(BinaryPrimitives.ReadUInt16LittleEndian(pixel));
            return double.IsFinite(value) ? value : 0;
        }

        private static void WriteMarkerHalf(Span<byte> pixel, double value)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(pixel,
                BitConverter.HalfToUInt16Bits((Half)Math.Clamp(value, -65504, 65504)));
        }
    }
}
