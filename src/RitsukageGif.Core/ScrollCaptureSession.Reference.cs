namespace RitsukageGif.Core
{
    public sealed partial class ScrollCaptureSession
    {
        private readonly byte[] _differenceMask;
        private readonly double[] _verificationErrors;
        private bool _hasReference;
        private int _referenceOffset;
        private byte[] _referencePixels;

        private void CommitReference(int offset)
        {
            (_scratch, _referencePixels) = (_referencePixels, _scratch);
            _referenceOffset = offset;
            _hasReference = true;
        }

        private void BuildDifferenceMask(byte[] pixels, CancellationToken token)
        {
            for (var index = 0; index < _differenceMask.Length; index++)
            {
                if ((index & 16383) == 0) token.ThrowIfCancellationRequested();
                _differenceMask[index] = pixels.AsSpan(index * 4, 4)
                    .SequenceEqual(_referencePixels.AsSpan(index * 4, 4))
                    ? (byte)0
                    : (byte)1;
            }
        }

        private ReadOnlySpan<byte> ReferenceRow(int position, bool previous)
        {
            return previous
                ? _referencePixels.AsSpan((position - _referenceOffset) * _rowBytes, _rowBytes)
                : Row(position);
        }

        private bool ActiveSample(int position, int offset, int cross)
        {
            return _differenceMask[(position - offset) * _crossLength + cross] != 0 &&
                   _differenceMask[(position - _referenceOffset) * _crossLength + cross] != 0;
        }

        private bool HasDistributedMotion(byte[] pixels, int offset, CancellationToken token)
        {
            var from = Math.Max(_referenceOffset, offset);
            var to = Math.Min(_referenceOffset + _axisLength, offset + _axisLength) - 1;
            for (var regionY = 0; regionY < 2; regionY++)
            for (var regionX = 0; regionX < 2; regionX++)
            {
                var top = from + (to - from) * regionY / 2;
                var bottom = from + (to - from) * (regionY + 1) / 2;
                var left = _crossLength * regionX / 2;
                var right = _crossLength * (regionX + 1) / 2;
                var candidates = 0;
                var count = 0;
                var candidateError = 0L;
                var stationaryError = 0L;
                var texture = 0L;
                for (var position = top; position < bottom; position++)
                {
                    if ((position & 31) == 0) token.ThrowIfCancellationRequested();
                    var input = pixels.AsSpan((position - offset) * _rowBytes, _rowBytes);
                    var inputNext = pixels.AsSpan((position - offset + 1) * _rowBytes, _rowBytes);
                    var matched = ReferenceRow(position, true);
                    var matchedNext = ReferenceRow(position + 1, true);
                    var stationary = _referencePixels.AsSpan((position - offset) * _rowBytes, _rowBytes);
                    for (var cross = left; cross < right; cross++)
                    {
                        var candidate = 0;
                        var zero = 0;
                        for (var channel = 0; channel < 3; channel++)
                        {
                            var index = cross * 4 + channel;
                            candidate += Math.Abs(input[index] - matched[index]);
                            zero += Math.Abs(input[index] - stationary[index]);
                            texture += Math.Min(Math.Abs(inputNext[index] - input[index]),
                                Math.Abs(matchedNext[index] - matched[index]));
                        }

                        candidateError += candidate;
                        stationaryError += zero;
                        if (candidate < zero) candidates++;
                        count++;
                    }
                }

                if (count == 0 || candidates <= count / 2 || candidateError >= stationaryError ||
                    (double)texture / (count * 3) < 2) return false;
            }

            return true;
        }
    }
}
