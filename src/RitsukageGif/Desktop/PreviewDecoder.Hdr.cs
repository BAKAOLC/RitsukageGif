using System.Buffers;
using System.Runtime.InteropServices;
using SkiaSharp;

namespace RitsukageGif.Desktop
{
    internal static partial class PreviewDecoder
    {
        private static IEnumerable<PreviewFrame> ReadHdr(string path, bool preserveHdr, CancellationToken token)
        {
            byte[]? canvas = null;
            var canvasWidth = 0;
            var canvasHeight = 0;
            using var decoded = new SKBitmap();
            foreach (var frame in PngAnimation.ReadEncodedFrames(path, token))
            {
                token.ThrowIfCancellationRequested();
                if (frame.Poster) continue;
                if (canvas is null)
                {
                    canvasWidth = frame.CanvasWidth;
                    canvasHeight = frame.CanvasHeight;
                    canvas = new byte[checked(canvasWidth * canvasHeight * 8)];
                }

                if (frame.CanvasWidth != canvasWidth || frame.CanvasHeight != canvasHeight)
                    throw new InvalidDataException();
                var previous = frame.Disposal == 2 ? (byte[])canvas.Clone() : null;
                using (var encoded = new MemoryStream(frame.Png, false))
                using (var codec = SKCodec.Create(encoded) ?? throw new InvalidDataException())
                {
                    // Matching source/destination color spaces avoids implicit transfer or primary conversion.
                    // FP16 here is a preview intermediate; the saved PNG remains untouched at 16-bit integer precision.
                    var info = new SKImageInfo(frame.Region.Width, frame.Region.Height, SKColorType.RgbaF16,
                        SKAlphaType.Unpremul, codec.Info.ColorSpace);
                    if (decoded.Info != info && !decoded.TryAllocPixels(info)) throw new OutOfMemoryException();
                    if (codec.GetPixels(info, decoded.GetPixels()) != SKCodecResult.Success)
                        throw new InvalidDataException();
                    var length = checked(frame.Region.Width * frame.Region.Height * 8);
                    var pixels = ArrayPool<byte>.Shared.Rent(length);
                    try
                    {
                        for (var y = 0; y < frame.Region.Height; y++)
                            Marshal.Copy(decoded.GetPixels() + y * decoded.RowBytes, pixels, y * frame.Region.Width * 8,
                                frame.Region.Width * 8);
                        var frameCanvas = canvas;
                        var frameCanvasWidth = canvasWidth;
                        Parallel.For(0, frame.Region.Height, new() { CancellationToken = token }, y =>
                        {
                            var row = pixels.AsSpan(y * frame.Region.Width * 8, frame.Region.Width * 8);
                            HdrPngColor.DecodePreviewRow(row, row);
                            var destination = frameCanvas.AsSpan(
                                ((frame.Region.Y + y) * frameCanvasWidth + frame.Region.X) * 8,
                                row.Length);
                            if (frame.Blend == 0) row.CopyTo(destination);
                            else BlendLinear(row, destination);
                        });
                    }
                    finally
                    {
                        ArrayPool<byte>.Shared.Return(pixels);
                    }
                }

                var mapped = HdrToneMapping.Map(new(canvasWidth, canvasHeight, canvasWidth * 8,
                        CapturePixelFormat.Rgba16FloatScRgb, canvas, new(true), CaptureAlphaMode.Straight, 0, []),
                    token: token);
                var buffer = ArrayPool<byte>.Shared.Rent(mapped.Pixels.Length);
                try
                {
                    mapped.Pixels.Span.CopyTo(buffer);
                    for (var i = 0; i < mapped.Pixels.Length; i += 4)
                    {
                        var alpha = buffer[i + 3];
                        buffer[i] = (byte)((buffer[i] * alpha + 127) / 255);
                        buffer[i + 1] = (byte)((buffer[i + 1] * alpha + 127) / 255);
                        buffer[i + 2] = (byte)((buffer[i + 2] * alpha + 127) / 255);
                    }
                }
                catch
                {
                    ArrayPool<byte>.Shared.Return(buffer);
                    throw;
                }

                byte[]? hdrPixels = null;
                PreviewFrame preview;
                try
                {
                    if (preserveHdr)
                    {
                        hdrPixels = ArrayPool<byte>.Shared.Rent(canvas.Length);
                        canvas.CopyTo(hdrPixels, 0);
                        var linear = MemoryMarshal.Cast<byte, Half>(hdrPixels.AsSpan(0, canvas.Length));
                        for (var i = 0; i < linear.Length; i += 4)
                        {
                            var alpha = Math.Clamp((float)linear[i + 3], 0, 1);
                            for (var channel = 0; channel < 3; channel++)
                                linear[i + channel] = (Half)((float)linear[i + channel] * alpha);
                            linear[i + 3] = (Half)1;
                        }
                    }

                    preview = new(buffer, canvasWidth, canvasHeight, frame.DelayMs, hdrPixels);
                }
                catch
                {
                    ArrayPool<byte>.Shared.Return(buffer);
                    if (hdrPixels is not null) ArrayPool<byte>.Shared.Return(hdrPixels);
                    throw;
                }

                yield return preview;
                if (previous is not null) canvas = previous;
                else if (frame.Disposal == 1)
                    for (var y = 0; y < frame.Region.Height; y++)
                        canvas.AsSpan(((frame.Region.Y + y) * canvasWidth + frame.Region.X) * 8, frame.Region.Width * 8)
                            .Clear();
            }
        }

        private static void BlendLinear(ReadOnlySpan<byte> source, Span<byte> destination)
        {
            var from = MemoryMarshal.Cast<byte, Half>(source);
            var to = MemoryMarshal.Cast<byte, Half>(destination);
            for (var i = 0; i < from.Length; i += 4)
            {
                var alpha = (float)from[i + 3];
                var prior = (float)to[i + 3] * (1 - alpha);
                var combined = alpha + prior;
                for (var channel = 0; channel < 3; channel++)
                    to[i + channel] = combined > 0
                        ? (Half)(((float)from[i + channel] * alpha + (float)to[i + channel] * prior) / combined)
                        : (Half)0;
                to[i + 3] = (Half)combined;
            }
        }
    }
}
