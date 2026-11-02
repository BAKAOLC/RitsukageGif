namespace RitsukageGif.Contracts
{
    public sealed record DocumentOutputOptions
    {
        public DocumentPaperSize PaperSize { get; init; } = DocumentPaperSize.Original;
        public DocumentOrientation Orientation { get; init; }
        public DocumentPageMode PageMode { get; init; }
        public DocumentPaginationDirection PaginationDirection { get; init; }
        public double WidthMillimeters { get; init; } = 210;
        public double HeightMillimeters { get; init; } = 297;
        public DocumentMargins Margins { get; init; } = new();
        public double Dpi { get; init; } = 96;
        public string Background { get; init; } = "#ffffff";
        public bool Lossless { get; init; } = true;
        public int Quality { get; init; } = 90;
        public bool Overwrite { get; init; }
    }
}
