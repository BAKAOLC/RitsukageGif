namespace RitsukageGif.Core
{
    public sealed partial class ScrollCaptureSession
    {
        public bool CanCropBeforeCurrentViewport => _strips.Count != 0 &&
                                                    CurrentOffset > StartOffset && CurrentOffset < _end;

        public bool CanCropAfterCurrentViewport => _strips.Count != 0 &&
                                                   (long)CurrentOffset + _axisLength > StartOffset &&
                                                   (long)CurrentOffset + _axisLength < _end;

        public bool CropBeforeCurrentViewport(CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            return CanCropBeforeCurrentViewport && CropRange(CurrentOffset, _end, token);
        }

        public bool CropAfterCurrentViewport(CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            return CanCropAfterCurrentViewport && CropRange(StartOffset, checked(CurrentOffset + _axisLength), token);
        }

        private bool CropRange(int start, int end, CancellationToken token)
        {
            var retained = new List<Strip>();
            foreach (var strip in _strips)
            {
                token.ThrowIfCancellationRequested();
                var from = Math.Max(start, strip.Start);
                var to = Math.Min(end, strip.End);
                if (from >= to) continue;
                retained.Add(from == strip.Start && to == strip.End
                    ? strip
                    : strip with
                    {
                        Start = from,
                        End = to,
                        DataOffset = checked(strip.DataOffset + (from - strip.Start) * _rowBytes),
                    });
            }

            token.ThrowIfCancellationRequested();
            if (retained.Count == 0) return false;
            _undo = new(StartOffset, _end, CurrentOffset, false, _strips);
            _strips = retained;
            StartOffset = start;
            _end = end;
            _rowStripIndex = 0;
            _limitReported = false;
            Logger.ZLogInformation(
                $"Scrolling capture result cropped: axis={_options.Axis.ToString()}, startOffset={StartOffset}, endOffset={_end}, currentOffset={CurrentOffset}, width={Width}, height={Height}, strips={_strips.Count}");
            return true;
        }
    }
}
