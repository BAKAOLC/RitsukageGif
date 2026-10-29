namespace RitsukageGif.Desktop
{
    // ReSharper disable once ClassCannotBeInstantiated
    internal sealed partial class PinnedImageWindow
    {
        private static byte[] ConvertTransformPngProfile(byte[] pixels, int width, int height, int depth, int colorType,
            IReadOnlyList<(string Type, ReadOnlyMemory<byte> Data)> chunks, CancellationToken token)
        {
            return PngColorTransform.ConvertToSrgb(pixels, width, height, depth, colorType, chunks, token);
        }

        private static bool IsTransformRawProfileChunk(string type, ReadOnlyMemory<byte> data, string profile)
        {
            return PngColorTransform.IsRawProfileChunk(type, data, profile);
        }
    }
}
