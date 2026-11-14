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
            var axisSize = vertical ? height : width;
            var crossSize = vertical ? width : height;
            var axisLength = _end - StartOffset;
            for (var axis = 0; axis < axisSize; axis++)
            {
                token.ThrowIfCancellationRequested();
                var sourceAxis = (int)((long)axis * axisLength / axisSize);
                var row = Row(StartOffset + sourceAxis);
                for (var cross = 0; cross < crossSize; cross++)
                {
                    var sourceCross = (int)((long)cross * _crossLength / crossSize);
                    var destination = vertical ? axis * width + cross : cross * width + axis;
                    row.Slice(sourceCross * 4, 4).CopyTo(pixels.AsSpan(destination * 4, 4));
                }
            }

            return new(pixels, width, height) { Hdr = _options.RetainHdr ? ExportHdr(width, height, token) : null };
        }
    }
}
