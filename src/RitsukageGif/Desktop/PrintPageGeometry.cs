namespace RitsukageGif.Desktop
{
    internal sealed record PrintPageGeometry(
        double PaperWidthPoints,
        double PaperHeightPoints,
        double PrintableXPoints,
        double PrintableYPoints,
        double PrintableWidthPoints,
        double PrintableHeightPoints)
    {
        internal void Validate()
        {
            DocumentPrintLayout.Validate(96, PaperWidthPoints, PaperHeightPoints);
            if (!double.IsFinite(PrintableXPoints) || !double.IsFinite(PrintableYPoints) ||
                !double.IsFinite(PrintableWidthPoints) || !double.IsFinite(PrintableHeightPoints) ||
                PrintableXPoints < 0 || PrintableYPoints < 0 || PrintableWidthPoints <= 0 ||
                PrintableHeightPoints <= 0 ||
                PrintableXPoints + PrintableWidthPoints > PaperWidthPoints + .001 ||
                PrintableYPoints + PrintableHeightPoints > PaperHeightPoints + .001)
                throw new InvalidDataException("The printer returned invalid printable page bounds.");
        }

        internal DocumentOutputOptions Apply(DocumentOutputOptions options)
        {
            Validate();
            const double millimetersPerPoint = 25.4 / 72;
            return options with
            {
                PaperSize = DocumentPaperSize.Custom,
                Orientation = PaperWidthPoints > PaperHeightPoints
                    ? DocumentOrientation.Landscape
                    : DocumentOrientation.Portrait,
                WidthMillimeters = PaperWidthPoints * millimetersPerPoint,
                HeightMillimeters = PaperHeightPoints * millimetersPerPoint,
                Margins = new()
                {
                    Left = Math.Max(options.Margins.Left, PrintableXPoints * millimetersPerPoint),
                    Top = Math.Max(options.Margins.Top, PrintableYPoints * millimetersPerPoint),
                    Right = Math.Max(options.Margins.Right,
                        (PaperWidthPoints - PrintableXPoints - PrintableWidthPoints) * millimetersPerPoint),
                    Bottom = Math.Max(options.Margins.Bottom,
                        (PaperHeightPoints - PrintableYPoints - PrintableHeightPoints) * millimetersPerPoint),
                },
            };
        }
    }
}
