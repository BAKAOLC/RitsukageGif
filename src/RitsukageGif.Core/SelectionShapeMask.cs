using System.Buffers.Binary;
using System.Runtime.InteropServices;
using SkiaSharp;

namespace RitsukageGif.Core
{
    public static class SelectionShapeMask
    {
        private static SKPath CreatePath(SelectionShape shape, PixelRect region, int width, int height)
        {
            var path = SKPath.ParseSvgPathData(shape.PathData) ??
                       throw new InvalidDataException("Invalid capture selection path.");
            path.FillType = shape.EvenOdd ? SKPathFillType.EvenOdd : SKPathFillType.Winding;
            var scaleX = (float)width / region.Width;
            var scaleY = (float)height / region.Height;
            path.Transform(new()
            {
                ScaleX = scaleX, ScaleY = scaleY, TransX = -region.X * scaleX,
                TransY = -region.Y * scaleY, Persp2 = 1,
            });
            return path;
        }

        public static byte[] Rasterize(SelectionShape shape, PixelRect region, int width, int height,
            CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            using var path = CreatePath(shape, region, width, height);
            using var bitmap = new SKBitmap(width, height, SKColorType.Alpha8, SKAlphaType.Premul);
            using var drawing = new SKCanvas(bitmap);
            using var paint = new SKPaint();
            paint.Color = SKColors.White;
            paint.IsAntialias = true;
            drawing.Clear(SKColors.Transparent);
            drawing.DrawPath(path, paint);
            drawing.Flush();
            var mask = new byte[checked(width * height)];
            for (var y = 0; y < height; y++)
            {
                token.ThrowIfCancellationRequested();
                Marshal.Copy(bitmap.GetPixels() + y * bitmap.RowBytes, mask, y * width, width);
            }

            return mask;
        }

        public static CapturedFrame Apply(CapturedFrame frame, SelectionShape shape, PixelRect region,
            CancellationToken token = default)
        {
            var mask = Rasterize(shape, region, frame.Width, frame.Height, token);
            var output = frame.Pixels.ToArray();
            var premultiplied = frame.AlphaMode == CaptureAlphaMode.Premultiplied;
            var bytes = frame.PixelFormat == CapturePixelFormat.Rgba16FloatScRgb ? 8 : 4;
            for (var y = 0; y < frame.Height; y++)
            {
                token.ThrowIfCancellationRequested();
                var row = output.AsSpan(y * frame.Stride, frame.Width * bytes);
                for (var x = 0; x < frame.Width; x++)
                {
                    var coverage = mask[y * frame.Width + x];
                    if (coverage == 255 && frame.AlphaMode != CaptureAlphaMode.Opaque) continue;
                    if (bytes == 4)
                    {
                        var pixel = row.Slice(x * 4, 4);
                        if (frame.AlphaMode == CaptureAlphaMode.Opaque)
                        {
                            pixel[3] = coverage;
                            continue;
                        }

                        for (var channel = premultiplied ? 0 : 3; channel < 4; channel++)
                            pixel[channel] = (byte)((pixel[channel] * coverage + 127) / 255);
                    }
                    else
                    {
                        var pixel = row.Slice(x * 8, 8);
                        if (frame.AlphaMode == CaptureAlphaMode.Opaque)
                        {
                            BinaryPrimitives.WriteHalfLittleEndian(pixel[6..], (Half)(coverage / 255f));
                            continue;
                        }

                        for (var channel = premultiplied ? 0 : 3; channel < 4; channel++)
                        {
                            var component = pixel.Slice(channel * 2, 2);
                            var value = (float)BitConverter.UInt16BitsToHalf(
                                BinaryPrimitives.ReadUInt16LittleEndian(component));
                            BinaryPrimitives.WriteUInt16LittleEndian(component,
                                BitConverter.HalfToUInt16Bits((Half)(coverage == 0 ? 0 : value * (coverage / 255f))));
                        }
                    }
                }
            }

            return frame with
            {
                Pixels = output,
                AlphaMode = frame.AlphaMode == CaptureAlphaMode.Opaque ? CaptureAlphaMode.Straight : frame.AlphaMode,
            };
        }
    }
}
