namespace RitsukageGif.Desktop
{
    internal sealed record ScreenshotAnnotationDocument
    {
        public ScreenshotAnnotationRecord?[] Layers { get; init; } = [];
        public int[] Current { get; init; } = [];
        public int[]?[] Undo { get; init; } = [];
        public int[]?[] Redo { get; init; } = [];
        public bool Visible { get; init; } = true;

        internal IReadOnlyDictionary<string, byte[]> Images { get; init; } =
            new Dictionary<string, byte[]>(StringComparer.Ordinal);

        internal long EstimatedBytes => Images.Values.Sum(image => (long)image.Length) +
                                        Layers.OfType<ScreenshotAnnotationRecord>().Sum(layer =>
                                            256L + layer.Points.LongLength * 16 + layer.Text.Length * 2L) +
                                        (Current.LongLength + Undo.Concat(Redo).Sum(state => state?.LongLength ?? 0)) *
                                        4;
    }
}
