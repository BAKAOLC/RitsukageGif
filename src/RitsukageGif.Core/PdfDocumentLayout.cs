namespace RitsukageGif.Core
{
    public static class PdfDocumentLayout
    {
        private const double PointsPerMillimeter = 72 / 25.4;

        public static IReadOnlyList<DocumentPageLayout> Create(int width, int height, DocumentOutputOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);
            if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));
            if (!Enum.IsDefined(options.PaperSize) || !Enum.IsDefined(options.Orientation) ||
                !Enum.IsDefined(options.PageMode) || !Enum.IsDefined(options.PaginationDirection) ||
                !double.IsFinite(options.Dpi) || options.Dpi <= 0 || options.Quality is < 0 or > 100)
                throw new ArgumentOutOfRangeException(nameof(options));
            var margins = options.Margins;
            ArgumentNullException.ThrowIfNull(margins);
            var left = Margin(margins.Left);
            var right = Margin(margins.Right);
            var top = Margin(margins.Top);
            var bottom = Margin(margins.Bottom);
            var originalScale = 72 / options.Dpi;
            var (pageWidth, pageHeight) = options.PaperSize switch
            {
                DocumentPaperSize.Original => (width * originalScale + left + right,
                    height * originalScale + top + bottom),
                DocumentPaperSize.A4 => (210 * PointsPerMillimeter, 297 * PointsPerMillimeter),
                DocumentPaperSize.A3 => (297 * PointsPerMillimeter, 420 * PointsPerMillimeter),
                DocumentPaperSize.Letter => (612d, 792d),
                DocumentPaperSize.Custom => (options.WidthMillimeters * PointsPerMillimeter,
                    options.HeightMillimeters * PointsPerMillimeter),
                _ => throw new ArgumentOutOfRangeException(nameof(options)),
            };
            if (options.PaperSize != DocumentPaperSize.Original)
            {
                var landscape = options.Orientation == DocumentOrientation.Landscape ||
                                (options.Orientation == DocumentOrientation.Automatic && width > height);
                if (landscape != pageWidth > pageHeight) (pageWidth, pageHeight) = (pageHeight, pageWidth);
            }

            if (!double.IsFinite(pageWidth) || !double.IsFinite(pageHeight) || pageWidth is <= 0 or > 14400 ||
                pageHeight is <= 0 or > 14400)
                throw new ArgumentException(
                    "PDF page dimensions must be between 0 and 14400 points. Use a paper size and pagination for larger images.",
                    nameof(options));
            var contentWidth = pageWidth - left - right;
            var contentHeight = pageHeight - top - bottom;
            if (contentWidth <= 0 || contentHeight <= 0)
                throw new ArgumentException("PDF margins leave no printable content area.", nameof(options));
            if (options.PageMode == DocumentPageMode.Fit || options.PaperSize == DocumentPaperSize.Original)
            {
                var scale = Math.Min(originalScale, Math.Min(contentWidth / width, contentHeight / height));
                return
                [
                    new(pageWidth, pageHeight, 0, 0, width, height,
                        left + (contentWidth - width * scale) / 2, top + (contentHeight - height * scale) / 2,
                        width * scale, height * scale),
                ];
            }

            var vertical = options.PaginationDirection == DocumentPaginationDirection.Vertical;
            var pagingScale = Math.Min(originalScale, vertical ? contentWidth / width : contentHeight / height);
            var slice = (vertical ? contentHeight : contentWidth) / pagingScale;
            var length = vertical ? height : width;
            var count = Math.Ceiling(length / slice);
            if (count > 4096) throw new InvalidOperationException("PDF output exceeds the 4096-page limit.");
            var pages = new List<DocumentPageLayout>((int)count);
            for (var index = 0; index < (int)count; index++)
            {
                var start = index * slice;
                var part = Math.Min(slice, length - start);
                var sourceWidth = vertical ? width : part;
                var sourceHeight = vertical ? part : height;
                pages.Add(new(pageWidth, pageHeight, vertical ? 0 : start, vertical ? start : 0,
                    sourceWidth, sourceHeight,
                    left + (vertical ? (contentWidth - width * pagingScale) / 2 : 0),
                    top + (vertical ? 0 : (contentHeight - height * pagingScale) / 2),
                    sourceWidth * pagingScale, sourceHeight * pagingScale));
            }

            return pages;
        }

        private static double Margin(double millimeters)
        {
            if (!double.IsFinite(millimeters) || millimeters < 0)
                throw new ArgumentOutOfRangeException(nameof(millimeters));
            return millimeters * PointsPerMillimeter;
        }
    }
}
