namespace RitsukageGif.Core
{
    public sealed partial class ScrollCaptureSession
    {
        private bool VerifyMatch(byte[] pixels, int offset, bool previous, bool masked, CancellationToken token)
        {
            var from = Math.Max(previous ? _referenceOffset : StartOffset, offset);
            var to = Math.Min(previous ? _referenceOffset + _axisLength : _end, offset + _axisLength);
            var inset = Math.Min((to - from) / 8, _axisLength / 10);
            from += inset;
            to -= inset;
            var count = 0;
            for (var position = from; position < to; position++)
            {
                if ((position & 31) == 0) token.ThrowIfCancellationRequested();
                var input = pixels.AsSpan((position - offset) * _rowBytes, _rowBytes);
                var stored = ReferenceRow(position, previous);
                var sum = 0L;
                var bytes = 0;
                for (var cross = 0; cross < _crossLength; cross++)
                {
                    if (masked && !ActiveSample(position, offset, cross)) continue;
                    for (var channel = 0; channel < 4; channel++)
                        sum += Math.Abs(input[cross * 4 + channel] - stored[cross * 4 + channel]);
                    bytes += 4;
                }

                if (bytes != 0) _verificationErrors[count++] = (double)sum / bytes;
            }

            if (count == 0) return false;
            var errors = _verificationErrors.AsSpan(0, count);
            errors.Sort();
            var stable = count - count / 8;
            var total = 0.0;
            for (var index = 0; index < stable; index++) total += errors[index];
            return total / stable <= 4;
        }
    }
}
