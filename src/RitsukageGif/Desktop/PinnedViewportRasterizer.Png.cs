using Avalonia;

namespace RitsukageGif.Desktop
{
    internal static partial class PinnedViewportRasterizer
    {
        internal static byte[] RenderPng(byte[] scenePng, Matrix sourceToTarget, int targetWidth, int targetHeight,
            double opacity, bool smoothScaling, PinnedViewportClip? clip = null, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            var source = PngImageCodec.Decode(scenePng, token);
            return PngImageCodec.Encode(Rasterize(source, sourceToTarget, targetWidth, targetHeight, opacity,
                smoothScaling, clip, token), token);
        }

        private static PngRaster Rasterize(PngRaster source, Matrix sourceToTarget, int targetWidth, int targetHeight,
            double opacity, bool smoothScaling, PinnedViewportClip? clip, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var depth = source.Depth == 16 ? (byte)16 : (byte)8;
            var gray = source.ColorType is 0 or 4;
            var channels = gray ? 2 : 4;
            var output = Allocate(targetWidth, targetHeight, channels * (depth / 8));
            var reader = new PinnedViewportPngSource(source);
            var writer = new PinnedViewportPngTarget(output, depth, gray);
            RenderProjection(reader, writer, sourceToTarget, targetWidth, targetHeight, opacity, smoothScaling, clip,
                token);
            var metadata = source.Metadata
                .Where(static chunk => chunk.Type is not ("PLTE" or "tRNS" or "sBIT" or "bKGD" or "hIST")).ToArray();
            return new(targetWidth, targetHeight, depth, gray ? (byte)4 : (byte)6, output, metadata);
        }
    }
}
