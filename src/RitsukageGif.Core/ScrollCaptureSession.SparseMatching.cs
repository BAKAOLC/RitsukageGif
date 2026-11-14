using System.Runtime.InteropServices;

namespace RitsukageGif.Core
{
    public sealed partial class ScrollCaptureSession
    {
        private int SparseEdgeBytes => Math.Clamp(_crossLength / 24, 0, 32) * 4;

        private ScrollCaptureResult? FindSparseMatch(byte[] pixels, bool previous, CancellationToken token,
            int edgeBytes = 0)
        {
            var start = previous ? _referenceOffset : StartOffset;
            var end = previous ? start + _axisLength : _end;
            var minimum = Math.Max(16, (int)Math.Ceiling(_axisLength * _options.MinimumOverlap));
            var incoming = new Dictionary<ulong, int>();
            for (var row = 0; row < _axisLength; row++)
            {
                if ((row & 31) == 0) token.ThrowIfCancellationRequested();
                if (SparseRowSignature(pixels.AsSpan(row * _rowBytes + edgeBytes, _rowBytes - 2 * edgeBytes))
                    is not { } signature) continue;
                if (!incoming.TryAdd(signature, row)) incoming[signature] = -1;
            }

            if (incoming.Count < 3) return null;
            var votes = new Dictionary<int, (int Count, int First, int Last)>();
            if (!previous && _options.UseDiskStorage)
                foreach (var strip in _strips)
                {
                    token.ThrowIfCancellationRequested();
                    var disk = strip.Disk!;
                    var skip = strip.DataOffset / _rowBytes;
                    var signatures = edgeBytes == 0 ? disk.Signatures : disk.InnerSignatures;
                    foreach (var (row, signature) in signatures)
                    {
                        var position = strip.Start + row - skip;
                        if (position >= strip.Start && position < strip.End) Vote(position, signature);
                    }
                }
            else
                for (var row = start; row < end; row++)
                {
                    if ((row & 31) == 0) token.ThrowIfCancellationRequested();
                    if (SparseRowSignature(ReferenceRow(row, previous).Slice(edgeBytes, _rowBytes - 2 * edgeBytes))
                        is { } signature)
                        Vote(row, signature);
                }

            int? match = null;
            foreach (var (offset, vote) in votes)
            {
                if (vote.Count < 3 || !ExactOverlap(pixels, offset, previous, token, edgeBytes)) continue;
                if (match.HasValue) return Result(ScrollCaptureStatus.Ambiguous);
                match = offset;
            }

            if (match is { } found) return Result(ScrollCaptureStatus.Appended, found, 0, 1);
            if (previous || !_options.UseDiskStorage || edgeBytes != 0) return null;

            foreach (var candidate in votes.Where(pair => pair.Value.Count >= 8 &&
                                                          pair.Value.Last - pair.Value.First >= _axisLength / 4)
                         .OrderByDescending(pair => pair.Value.Count).Take(12))
            {
                token.ThrowIfCancellationRequested();
                var offset = candidate.Key;
                var score = Score(pixels, offset, 64, 64, out var texture, false, false);
                if (texture < 2 || score > 4 || !VerifyMatch(pixels, offset, false, false, token)) continue;
                if (match.HasValue) return Result(ScrollCaptureStatus.Ambiguous);
                match = offset;
            }

            return match is { } recovered ? Result(ScrollCaptureStatus.Appended, recovered, 0, 0.8) : null;

            void Vote(int row, ulong signature)
            {
                if (!incoming.TryGetValue(signature, out var index) || index < 0) return;
                var offset = row - index;
                if (offset < start - _axisLength + minimum || offset > end - minimum) return;
                var vote = votes.GetValueOrDefault(offset);
                votes[offset] = (vote.Count + 1, vote.Count == 0 ? row : vote.First, row);
            }
        }

        private bool ExactOverlap(byte[] pixels, int offset, bool previous, CancellationToken token,
            int edgeBytes = 0)
        {
            var from = Math.Max(previous ? _referenceOffset : StartOffset, offset);
            var to = Math.Min(previous ? _referenceOffset + _axisLength : _end, offset + _axisLength);
            if (to <= from) return false;
            for (var row = from; row < to; row++)
            {
                if ((row & 31) == 0) token.ThrowIfCancellationRequested();
                var length = _rowBytes - 2 * edgeBytes;
                if (!pixels.AsSpan((row - offset) * _rowBytes + edgeBytes, length)
                        .SequenceEqual(ReferenceRow(row, previous).Slice(edgeBytes, length)))
                    return false;
            }

            return true;
        }

        private static ulong? SparseRowSignature(ReadOnlySpan<byte> row)
        {
            var pixels = MemoryMarshal.Cast<byte, uint>(row);
            var first = pixels[0];
            var varied = false;
            var hash = 14695981039346656037UL;
            for (var x = 0; x < pixels.Length; x += 4)
            {
                var pixel = pixels[x];
                varied |= pixel != first;
                hash = (hash ^ pixel) * 1099511628211UL;
            }

            return varied ? hash : null;
        }
    }
}
