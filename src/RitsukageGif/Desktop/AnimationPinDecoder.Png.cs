using System.Buffers;
using System.Runtime.InteropServices;
using SkiaSharp;

namespace RitsukageGif.Desktop
{
    internal sealed partial class AnimationPinDecoder
    {
        private PreviewFrame? ReadPng(int index, bool snapshot, CancellationToken token)
        {
            var frame = _metadata.Frames[index];
            var encoded = _metadata.EncodePngFrame(index, token);
            using var codec = SKCodec.Create(new MemoryStream(encoded, false)) ?? throw new InvalidDataException();
            if (_metadata.IsHdr) return ReadHdrPng(codec, frame, snapshot, token);
            var canvas = _canvas!;
            if (frame.Disposal == 2) Copy(canvas, _restore!);
            using var patch = new SKBitmap(new(frame.Region.Width, frame.Region.Height, SKColorType.Bgra8888,
                SKAlphaType.Premul, SKColorSpace.CreateSrgb()));
            if (codec.GetPixels(patch.Info, patch.GetPixels()) != SKCodecResult.Success)
                throw new InvalidDataException();
            using var drawing = new SKCanvas(canvas);
            using var paint = new SKPaint();
            paint.BlendMode = frame.Blend == 0 ? SKBlendMode.Src : SKBlendMode.SrcOver;
            drawing.DrawBitmap(patch, frame.Region.X, frame.Region.Y, paint);
            var result = snapshot ? Snapshot(token) : null;
            switch (frame.Disposal)
            {
                case 2:
                    Copy(_restore!, canvas);
                    break;
                case 1:
                    paint.BlendMode = SKBlendMode.Clear;
                    drawing.DrawRect(frame.Region.X, frame.Region.Y, frame.Region.Width, frame.Region.Height, paint);
                    break;
            }

            return result;
        }

        private PreviewFrame? ReadHdrPng(SKCodec codec, AnimationPinFrameInfo frame, bool snapshot,
            CancellationToken token)
        {
            var canvas = _linear!;
            var previous = frame.Disposal == 2 ? (byte[])canvas.Clone() : null;
            var region = frame.Region;
            using var patch = new SKBitmap(new(region.Width, region.Height, SKColorType.RgbaF16,
                SKAlphaType.Unpremul, codec.Info.ColorSpace));
            if (codec.GetPixels(patch.Info, patch.GetPixels()) != SKCodecResult.Success)
                throw new InvalidDataException();
            var row = ArrayPool<byte>.Shared.Rent(checked(region.Width * 8));
            try
            {
                for (var y = 0; y < region.Height; y++)
                {
                    token.ThrowIfCancellationRequested();
                    Marshal.Copy(patch.GetPixels() + y * patch.RowBytes, row, 0, region.Width * 8);
                    var source = row.AsSpan(0, region.Width * 8);
                    HdrPngColor.DecodePreviewRow(source, source);
                    var target = canvas.AsSpan(((region.Y + y) * _metadata.Width + region.X) * 8, source.Length);
                    if (frame.Blend == 0) source.CopyTo(target);
                    else Blend(source, target);
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(row);
            }

            var result = snapshot ? SnapshotHdr(token) : null;
            if (previous is not null) _linear = previous;
            else if (frame.Disposal == 1)
                for (var y = 0; y < region.Height; y++)
                    canvas.AsSpan(((region.Y + y) * _metadata.Width + region.X) * 8, region.Width * 8).Clear();
            return result;
        }

        private PreviewFrame SnapshotHdr(CancellationToken token)
        {
            var length = checked(_metadata.Width * _metadata.Height * 8);
            var frame = new CapturedFrame(_metadata.Width, _metadata.Height, _metadata.Width * 8,
                CapturePixelFormat.Rgba16FloatScRgb, _linear!, new(true), CaptureAlphaMode.Straight, 0, []);
            var mapped = HdrToneMapping.Map(frame, token: token);
            var sdr = ArrayPool<byte>.Shared.Rent(checked(_metadata.Width * _metadata.Height * 4));
            byte[]? hdr = null;
            try
            {
                hdr = ArrayPool<byte>.Shared.Rent(length);
                _linear!.AsSpan(0, length).CopyTo(hdr);
                mapped.Pixels.Span.CopyTo(sdr);
                return new(sdr, _metadata.Width, _metadata.Height, 0, hdr);
            }
            catch
            {
                ArrayPool<byte>.Shared.Return(sdr);
                if (hdr is not null) ArrayPool<byte>.Shared.Return(hdr);
                throw;
            }
        }

        private static void Blend(ReadOnlySpan<byte> source, Span<byte> destination)
        {
            var from = MemoryMarshal.Cast<byte, Half>(source);
            var to = MemoryMarshal.Cast<byte, Half>(destination);
            for (var i = 0; i < from.Length; i += 4)
            {
                var alpha = Math.Clamp((float)from[i + 3], 0, 1);
                var prior = Math.Clamp((float)to[i + 3], 0, 1) * (1 - alpha);
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
