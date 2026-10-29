using System.Buffers.Binary;

namespace RitsukageGif.Desktop
{
    internal static class ScreenshotOutputDecoration
    {
        internal static Task<ScreenshotResult> PrepareAsync(ScreenshotResult source, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(source.Decoration);
            token.ThrowIfCancellationRequested();
            source.Decoration.Validate();
            if (source.Decoration.IsEmpty) return Task.FromResult(source);
            var dimensions = PngImageCodec.ReadDimensions(source.Png);
            if (source.Hdr is not null &&
                (source.Hdr.Width != dimensions.Width || source.Hdr.Height != dimensions.Height))
                throw new InvalidDataException(UiText.Get("InvalidFrameBuffer"));
            var channels = source.Png[25] switch
            {
                0 or 3 => 1, 2 => 3, 4 => 2, 6 => 4, _ => throw new InvalidDataException(),
            };
            var bytes = source.Png[24] == 16 ? 2 : 1;
            var includeHdr = source.ColorMode != OutputColorMode.Sdr && source.Hdr is not null;
            var sourceBytes = source.Png.LongLength + (long)dimensions.Width * dimensions.Height * channels * bytes +
                              (source.Hdr?.Pixels.Length ?? 0);
            var geometry = CaptureDecorationProcessor.ValidateBudget(dimensions.Width, dimensions.Height,
                source.Decoration, sourceBytes, bytes * 4 + (includeHdr ? 8 : 0));
            var offset = new PixelPoint(checked(source.ContentOffset.X + geometry.ContentOffset.X),
                checked(source.ContentOffset.Y + geometry.ContentOffset.Y));
            var origin = source.OutputOrigin is { } position
                ? new PixelPoint(checked(position.X - geometry.ContentOffset.X),
                    checked(position.Y - geometry.ContentOffset.Y))
                : (PixelPoint?)null;
            return Task.Run(() =>
            {
                var raster = ExpandLowBitDepth(PngImageCodec.Decode(source.Png, token), token);
                var decorated = ScreenshotDecorationPng.Apply(raster, source.Decoration, token);
                var png = PngImageCodec.Encode(decorated, token);
                var hdr = includeHdr ? CaptureDecorationProcessor.Apply(source.Hdr!, source.Decoration, token) : null;
                if (decorated.Width != geometry.Width || decorated.Height != geometry.Height ||
                    (hdr is not null && (hdr.Width != geometry.Width || hdr.Height != geometry.Height)))
                    throw new InvalidDataException(UiText.Get("InvalidFrameBuffer"));
                return source with
                {
                    Png = png, Hdr = hdr, Decoration = CaptureDecoration.None,
                    ContentOffset = offset, OutputOrigin = origin,
                };
            }, token);
        }

        private static PngRaster ExpandLowBitDepth(PngRaster source, CancellationToken token)
        {
            if (source.Depth >= 8) return source;
            if (source.ColorType == 3) return source with { Depth = 8 };
            if (source.ColorType != 0 || source.Depth is not (1 or 2 or 4)) throw new InvalidDataException();
            var maximum = (1 << source.Depth) - 1;
            var samples = new byte[source.Samples.Length];
            for (var y = 0; y < source.Height; y++)
            {
                token.ThrowIfCancellationRequested();
                for (var x = 0; x < source.Width; x++)
                {
                    var index = y * source.Width + x;
                    samples[index] = (byte)(source.Samples[index] * 255 / maximum);
                }
            }

            var metadata = source.Metadata.Select(chunk =>
            {
                if (chunk.Type is not ("tRNS" or "bKGD")) return chunk;
                if (chunk.Data.Length != 2) throw new InvalidDataException();
                var value = BinaryPrimitives.ReadUInt16BigEndian(chunk.Data.Span);
                if (value > maximum) throw new InvalidDataException();
                var expanded = new byte[2];
                BinaryPrimitives.WriteUInt16BigEndian(expanded, (ushort)(value * 255 / maximum));
                return (chunk.Type, (ReadOnlyMemory<byte>)expanded);
            }).ToArray();
            return source with { Depth = 8, Samples = samples, Metadata = metadata };
        }
    }
}
