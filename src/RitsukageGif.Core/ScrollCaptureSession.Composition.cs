namespace RitsukageGif.Core
{
    public sealed partial class ScrollCaptureSession
    {
        private bool TryFindHandoff(byte[] pixels, int offset, bool masked, CancellationToken token, out int handoff)
        {
            var from = Math.Max(StartOffset, offset);
            var to = Math.Min(_end, offset + _axisLength);
            var runStart = from;
            var bestStart = from;
            var bestLength = 0;
            for (var position = from; position < to - 1; position++)
            {
                if ((position & 31) == 0) token.ThrowIfCancellationRequested();
                if (masked && ChangedPixels(position - offset) <= _crossLength / 2)
                {
                    runStart = position + 1;
                    continue;
                }

                var input = pixels.AsSpan((position - offset) * _rowBytes, _rowBytes);
                var next = pixels.AsSpan((position - offset + 1) * _rowBytes, _rowBytes);
                var stored = Row(position);
                var storedNext = Row(position + 1);
                var texture = 0L;
                for (var index = 0; index < _rowBytes; index += 4)
                for (var channel = 0; channel < 3; channel++)
                    texture += Math.Min(Math.Abs(next[index + channel] - input[index + channel]),
                        Math.Abs(storedNext[index + channel] - stored[index + channel]));
                var reliable = RowDifference(input, stored) <= 4 && (double)texture / (_crossLength * 3) >= 2;
                if (!reliable)
                {
                    runStart = position + 1;
                    continue;
                }

                var length = position - runStart + 1;
                if (length <= bestLength) continue;
                bestLength = length;
                bestStart = runStart;
            }

            handoff = bestStart + bestLength / 2;
            return bestLength >= 2;
        }

        private int ChangedPixels(int row)
        {
            var count = 0;
            var mask = _differenceMask.AsSpan(row * _crossLength, _crossLength);
            foreach (var changed in mask)
                if (changed != 0)
                    count++;
            return count;
        }

        private static double RowDifference(ReadOnlySpan<byte> input, ReadOnlySpan<byte> stored)
        {
            var error = 0L;
            for (var index = 0; index < input.Length; index++) error += Math.Abs(input[index] - stored[index]);
            return (double)error / input.Length;
        }

        private List<Strip> RetainOutsideHandoff(int handoff, bool prepend, out long sharedPayload,
            out long compactPayload, out int compactIndex)
        {
            var retained = new List<Strip>();
            sharedPayload = 0;
            compactPayload = 0;
            compactIndex = -1;
            foreach (var strip in _strips)
            {
                var from = prepend ? Math.Max(strip.Start, handoff) : strip.Start;
                var to = prepend ? strip.End : Math.Min(strip.End, handoff);
                if (from >= to) continue;
                retained.Add(from == strip.Start && to == strip.End
                    ? strip
                    : strip with
                    {
                        Start = from,
                        End = to,
                        DataOffset = checked(strip.DataOffset + (from - strip.Start) * _rowBytes),
                    });
                if (from == strip.Start && to == strip.End)
                {
                    sharedPayload += strip.Pixels.LongLength + (strip.HdrPixels?.LongLength ?? 0);
                }
                else
                {
                    compactPayload = (long)(to - from) * _storageRowBytes;
                    compactIndex = retained.Count - 1;
                }
            }

            return retained;
        }

        private Strip CompactBoundary(Strip strip, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var length = checked((strip.End - strip.Start) * _rowBytes);
            var pixels = strip.Pixels.AsSpan(strip.DataOffset, length).ToArray();
            token.ThrowIfCancellationRequested();
            var hdrPixels = strip.HdrPixels is { } hdr
                ? hdr.AsSpan(checked(strip.DataOffset * 2), checked(length * 2)).ToArray()
                : null;
            token.ThrowIfCancellationRequested();
            return strip with { Pixels = pixels, HdrPixels = hdrPixels, DataOffset = 0 };
        }
    }
}
