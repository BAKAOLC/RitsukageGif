using System.Buffers;

namespace RitsukageGif.Core
{
    public sealed partial class ScrollCaptureSession
    {
        private bool VerifyMatch(byte[] pixels, int offset, CancellationToken token)
        {
            var from = Math.Max(StartOffset, offset);
            var to = Math.Min(_end, offset + _axisLength);
            var inset = Math.Min((to - from) / 8, _axisLength / 10);
            from += inset;
            to -= inset;
            var count = to - from;
            var buffer = ArrayPool<double>.Shared.Rent(count);
            try
            {
                var errors = buffer.AsSpan(0, count);
                for (var position = from; position < to; position++)
                {
                    if ((position & 31) == 0) token.ThrowIfCancellationRequested();
                    var input = pixels.AsSpan((position - offset) * _rowBytes, _rowBytes);
                    var stored = Row(position);
                    var sum = 0L;
                    for (var x = 0; x < _rowBytes; x++) sum += Math.Abs(input[x] - stored[x]);
                    errors[position - from] = (double)sum / _rowBytes;
                }

                errors.Sort();
                var stable = count - count / 8;
                var total = 0.0;
                for (var index = 0; index < stable; index++) total += errors[index];
                return total / stable <= 4;
            }
            finally
            {
                ArrayPool<double>.Shared.Return(buffer);
            }
        }
    }
}
