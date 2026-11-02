namespace RitsukageGif.Desktop
{
    internal static class CodeRecognitionOutput
    {
        internal static string? FirstText(IReadOnlyList<BarcodeRecognitionResult> codes)
        {
            BarcodeRecognitionResult? first = null;
            var top = float.PositiveInfinity;
            var left = float.PositiveInfinity;
            foreach (var code in codes)
            {
                var points = code.Outline.Count > 0 ? code.Outline : code.Points;
                var y = points.Count == 0 ? float.PositiveInfinity : points.Min(point => point.Y);
                var x = points.Count == 0 ? float.PositiveInfinity : points.Min(point => point.X);
                if (first is not null && (y > top || (y.Equals(top) && x >= left))) continue;
                first = code;
                top = y;
                left = x;
            }

            return first?.Text;
        }
    }
}
