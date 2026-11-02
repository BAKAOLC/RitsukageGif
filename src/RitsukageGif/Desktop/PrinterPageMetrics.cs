namespace RitsukageGif.Desktop
{
    internal sealed record PrinterPageMetrics(
        int PaperWidth,
        int PaperHeight,
        int PrintableWidth,
        int PrintableHeight,
        int OffsetX,
        int OffsetY,
        int DpiX,
        int DpiY)
    {
        internal PrintPageGeometry Geometry => new(PaperWidth * 72d / DpiX, PaperHeight * 72d / DpiY,
            OffsetX * 72d / DpiX, OffsetY * 72d / DpiY, PrintableWidth * 72d / DpiX,
            PrintableHeight * 72d / DpiY);

        internal DocumentOutputOptions Apply(DocumentOutputOptions options)
        {
            Validate();
            return Geometry.Apply(options);
        }

        internal void Validate()
        {
            if (DpiX <= 0 || DpiY <= 0 || PrintableWidth <= 0 || PrintableHeight <= 0 ||
                OffsetX < 0 || OffsetY < 0 || PaperWidth < (long)PrintableWidth + OffsetX ||
                PaperHeight < (long)PrintableHeight + OffsetY)
                throw new InvalidDataException("The printer returned invalid paper geometry.");
            DocumentPrintLayout.Validate(96, PaperWidth * 72d / DpiX, PaperHeight * 72d / DpiY);
        }

        internal PrintPagePlacement Placement(double widthPoints, double heightPoints)
        {
            if (Math.Abs(widthPoints - PaperWidth * 72d / DpiX) > .1 ||
                Math.Abs(heightPoints - PaperHeight * 72d / DpiY) > .1)
                throw new InvalidDataException("A rendered page does not match the selected printer paper.");
            return new(-OffsetX, -OffsetY, PaperWidth, PaperHeight);
        }
    }
}
