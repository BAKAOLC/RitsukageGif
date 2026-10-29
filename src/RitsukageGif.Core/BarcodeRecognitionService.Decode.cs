using System.Collections.ObjectModel;
using ZXing;

namespace RitsukageGif.Core
{
    public sealed partial class BarcodeRecognitionService
    {
        private static ReadOnlyCollection<BarcodeRecognitionResult> Recognize(byte[] luminance, int width, int height,
            CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (luminance.AsSpan().IndexOfAnyExcept(luminance[0]) < 0)
                return ReadOnlyCollection<BarcodeRecognitionResult>.Empty;
            var found = new List<BarcodeRecognitionResult>();
            var qrReader = CreateReader(true);
            var barcodeReader = CreateReader(false);
            var transformed = new byte[luminance.Length];
            for (var mirrored = 0; mirrored < 2; mirrored++)
            for (var rotated = 0; rotated < 2; rotated++)
            {
                token.ThrowIfCancellationRequested();
                var outputWidth = rotated == 0 ? width : height;
                var outputHeight = rotated == 0 ? height : width;
                for (var y = 0; y < height; y++)
                {
                    token.ThrowIfCancellationRequested();
                    for (var x = 0; x < width; x++)
                    {
                        var mx = mirrored == 0 ? x : width - 1 - x;
                        var tx = rotated == 0 ? mx : height - 1 - y;
                        var ty = rotated == 0 ? y : mx;
                        transformed[ty * outputWidth + tx] = luminance[y * width + x];
                    }
                }

                var source = new RGBLuminanceSource(transformed, outputWidth, outputHeight,
                    RGBLuminanceSource.BitmapFormat.Gray8);
                var qrCodes = qrReader.DecodeMultiple(source);
                token.ThrowIfCancellationRequested();
                if (qrCodes is not null)
                    foreach (var qrCode in qrCodes)
                        Add(qrCode, 0, 0);

                var windows = new Queue<(int X, int Y, int Width, int Height, int Depth)>();
                windows.Enqueue((0, 0, outputWidth, outputHeight, 0));
                for (var scan = 0; scan < 64 && windows.TryDequeue(out var window); scan++)
                {
                    token.ThrowIfCancellationRequested();
                    var result = barcodeReader.Decode(source.crop(window.X, window.Y, window.Width, window.Height));
                    token.ThrowIfCancellationRequested();
                    if (result is null) continue;
                    result = NormalizePdfPoints(result, window.Width, window.Height);
                    Add(result, window.X, window.Y);
                    var points = result.ResultPoints.Where(point => point is not null).ToArray();
                    if (points.Length == 0 || window.Depth >= 4) continue;
                    var left = Math.Clamp((int)points.Min(point => point.X), 0, window.Width);
                    var right = Math.Clamp((int)Math.Ceiling(points.Max(point => point.X)), 0, window.Width);
                    var top = Math.Clamp((int)points.Min(point => point.Y), 0, window.Height);
                    var bottom = Math.Clamp((int)Math.Ceiling(points.Max(point => point.Y)), 0, window.Height);
                    if (left >= 30) windows.Enqueue((window.X, window.Y, left, window.Height, window.Depth + 1));
                    if (top >= 30) windows.Enqueue((window.X, window.Y, window.Width, top, window.Depth + 1));
                    if (window.Width - right >= 30)
                        windows.Enqueue((window.X + right, window.Y, window.Width - right, window.Height,
                            window.Depth + 1));
                    if (window.Height - bottom >= 30)
                        windows.Enqueue((window.X, window.Y + bottom, window.Width, window.Height - bottom,
                            window.Depth + 1));
                }

                continue;

                void Add(Result result, int offsetX, int offsetY)
                {
                    var points = result.ResultPoints.Where(point => point is not null).Select(point =>
                    {
                        var tx = point.X + offsetX;
                        var ty = point.Y + offsetY;
                        var mx = rotated == 0 ? tx : ty;
                        var y = rotated == 0 ? ty : height - 1 - tx;
                        var x = mirrored == 0 ? mx : width - 1 - mx;
                        return new BarcodeRecognitionPoint(Math.Clamp(x, 0, width - 1),
                            Math.Clamp(y, 0, height - 1));
                    }).ToArray();
                    var candidate = new BarcodeRecognitionResult(result.Text, result.BarcodeFormat.ToString(),
                        Array.AsReadOnly(points));
                    if (!found.Any(existing => SameSymbol(existing, candidate, luminance, width))) found.Add(candidate);
                }
            }

            return found.AsReadOnly();
        }

        private static Result NormalizePdfPoints(Result result, int width, int height)
        {
            if (result.BarcodeFormat != BarcodeFormat.PDF_417 || result.ResultMetadata is null ||
                !result.ResultMetadata.TryGetValue(ResultMetadataType.ORIENTATION, out var orientation) ||
                orientation is not int rotation || rotation == 0) return result;
            var points = result.ResultPoints.Where(point => point is not null).Select(point => rotation switch
            {
                90 => new(width - 1 - point.Y, point.X),
                180 => new(width - 1 - point.X, height - 1 - point.Y),
                270 => new(point.Y, height - 1 - point.X),
                _ => point,
            }).ToArray();
            return new(result.Text, result.RawBytes, points, result.BarcodeFormat);
        }

        private static bool SameSymbol(BarcodeRecognitionResult left, BarcodeRecognitionResult right,
            byte[] luminance, int width)
        {
            if (left.Format != right.Format || left.Text != right.Text) return false;
            if (left.Points.Count == 0 || right.Points.Count == 0) return true;
            var leftX = left.Points.Average(point => point.X);
            var leftY = left.Points.Average(point => point.Y);
            var rightX = right.Points.Average(point => point.X);
            var rightY = right.Points.Average(point => point.Y);
            var extent = Math.Max(left.Points.Max(point => point.X) - left.Points.Min(point => point.X),
                left.Points.Max(point => point.Y) - left.Points.Min(point => point.Y));
            var tolerance = Math.Max(8, extent * 0.2f);
            if (left.Points.Count == 2 && right.Points.Count == 2)
                return SameLinearRegion(left, right, luminance, width);
            return Math.Abs(leftX - rightX) <= tolerance && Math.Abs(leftY - rightY) <= tolerance;
        }

        private static bool SameLinearRegion(BarcodeRecognitionResult left, BarcodeRecognitionResult right,
            byte[] luminance, int width)
        {
            var horizontal = Math.Abs(left.Points[0].X - left.Points[1].X) >=
                             Math.Abs(left.Points[0].Y - left.Points[1].Y);
            var start = left.Points.Min(point => horizontal ? point.X : point.Y);
            var end = left.Points.Max(point => horizontal ? point.X : point.Y);
            if (Math.Abs(start - right.Points.Min(point => horizontal ? point.X : point.Y)) > 8 ||
                Math.Abs(end - right.Points.Max(point => horizontal ? point.X : point.Y)) > 8) return false;
            var first = (int)Math.Round(left.Points.Average(point => horizontal ? point.Y : point.X));
            var last = (int)Math.Round(right.Points.Average(point => horizontal ? point.Y : point.X));
            var from = Math.Min(first, last);
            var to = Math.Max(first, last);
            for (var position = from; position <= to; position += 2)
            {
                var matches = 0;
                for (var sample = 0; sample < 64; sample++)
                {
                    var coordinate = (int)Math.Round(start + (end - start) * sample / 63);
                    var referenceIndex = horizontal ? first * width + coordinate : coordinate * width + first;
                    var currentIndex = horizontal ? position * width + coordinate : coordinate * width + position;
                    if (Math.Abs(luminance[referenceIndex] - luminance[currentIndex]) < 48) matches++;
                }

                if (matches < 58) return false;
            }

            return true;
        }
    }
}
