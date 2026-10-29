namespace RitsukageGif.Core
{
    public sealed partial class ScrollCaptureSession
    {
        public ScrollCaptureImage ExportPreviewPixels(int maxWidth = 160, int maxHeight = 220,
            CancellationToken token = default)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxWidth);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxHeight);
            if (_strips.Count == 0) throw new InvalidOperationException("No frame has been accepted.");
            token.ThrowIfCancellationRequested();
            var scale = Math.Min(1, Math.Min((double)maxWidth / Width, (double)maxHeight / Height));
            var width = Math.Max(1, (int)(Width * scale));
            var height = Math.Max(1, (int)(Height * scale));
            var pixels = new byte[checked(width * height * 4)];
            var vertical = _options.Axis == ScrollCaptureAxis.Vertical;
            for (var y = 0; y < height; y++)
            {
                token.ThrowIfCancellationRequested();
                var sourceY = (int)((long)y * Height / height);
                for (var x = 0; x < width; x++)
                {
                    var sourceX = (int)((long)x * Width / width);
                    var row = Row(StartOffset + (vertical ? sourceY : sourceX));
                    row.Slice((vertical ? sourceX : sourceY) * 4, 4).CopyTo(pixels.AsSpan((y * width + x) * 4, 4));
                }
            }

            return new(pixels, width, height);
        }
    }
}
