namespace RitsukageGif.Core
{
    internal static class StructureRegionDetector
    {
        internal static StructureRegionAnalysis Analyze(ReadOnlySpan<byte> pixels, int width, int height,
            int stride, CancellationToken token)
        {
            return new(pixels, width, height, stride, token);
        }
    }
}
