namespace RitsukageGif.Core
{
    public sealed partial class ScrollCaptureSession
    {
        private bool TryFindRegionalMotion(byte[] pixels, CancellationToken token, out int offset,
            out double confidence)
        {
            const int columns = 16;
            offset = _referenceOffset;
            confidence = 0;
            if (_crossLength < columns * 4) return false;
            var minimum = Math.Max(16, (int)Math.Ceiling(_axisLength * _options.MinimumOverlap));
            var radius = _axisLength - minimum;
            var found = false;
            for (var delta = -radius; delta <= radius; delta++)
            {
                if ((delta & 31) == 0) token.ThrowIfCancellationRequested();
                if (delta == 0) continue;
                var from = Math.Max(0, delta);
                var to = Math.Min(_axisLength, _axisLength + delta);
                var middle = from + (to - from) / 2;
                if (!SupportsRegionalMotion(pixels, delta, from, middle, false, out _) ||
                    !SupportsRegionalMotion(pixels, delta, middle, to, false, out _) ||
                    !SupportsRegionalMotion(pixels, delta, from, middle, true, out var firstConfidence) ||
                    !SupportsRegionalMotion(pixels, delta, middle, to, true, out var secondConfidence)) continue;
                if (found) return false;
                offset = _referenceOffset + delta;
                confidence = Math.Min(firstConfidence, secondConfidence);
                found = true;
            }

            return found;
        }

        private bool SupportsRegionalMotion(byte[] pixels, int delta, int from, int to, bool verify,
            out double confidence)
        {
            const int columns = 16;
            var supported = 0;
            var supportingSamples = 0;
            var supportingError = 0;
            var leading = false;
            var trailing = false;
            var rowCount = Math.Min(verify ? 16 : 4, to - from - 1);
            var columnCount = verify ? 8 : 2;
            for (var column = 0; column < columns; column++)
            {
                var left = column * _crossLength / columns;
                var right = (column + 1) * _crossLength / columns;
                var evidence = 0;
                var error = 0;
                var texture = 0;
                var samples = rowCount * columnCount;
                for (var row = 0; row < rowCount; row++)
                {
                    var oldRow = from + row * (to - from - 2) / Math.Max(1, rowCount - 1);
                    var newRow = oldRow - delta;
                    for (var x = 0; x < columnCount; x++)
                    {
                        var cross = left + x * (right - left - 1) / (columnCount - 1);
                        var first = oldRow * _crossLength + cross;
                        var second = newRow * _crossLength + cross;
                        if (_differenceMask[first] == 0 || _differenceMask[second] == 0) continue;
                        evidence++;
                        var a = first * 4;
                        var b = second * 4;
                        error += Math.Abs(pixels[b] - _referencePixels[a]) +
                                 Math.Abs(pixels[b + 1] - _referencePixels[a + 1]) +
                                 Math.Abs(pixels[b + 2] - _referencePixels[a + 2]) +
                                 Math.Abs(pixels[b + 3] - _referencePixels[a + 3]);
                        if (error > samples * 4) break;
                        if (verify)
                            texture += Math.Min(Math.Abs(pixels[b] - pixels[b + _rowBytes]),
                                           Math.Abs(_referencePixels[a] - _referencePixels[a + _rowBytes])) +
                                       Math.Min(Math.Abs(pixels[b + 1] - pixels[b + _rowBytes + 1]),
                                           Math.Abs(_referencePixels[a + 1] - _referencePixels[a + _rowBytes + 1])) +
                                       Math.Min(Math.Abs(pixels[b + 2] - pixels[b + _rowBytes + 2]),
                                           Math.Abs(_referencePixels[a + 2] - _referencePixels[a + _rowBytes + 2]));
                    }

                    if (error > samples * 4) break;
                }

                if (evidence * 5 < samples * 4 || error > evidence * 4 ||
                    (verify && texture < evidence * 36)) continue;
                supported++;
                supportingSamples += evidence;
                supportingError += error;
                leading |= column < columns / 4;
                trailing |= column >= columns * 3 / 4;
            }

            // A correct regional match earns credit only for the samples that support it.
            var totalSamples = columns * rowCount * columnCount;
            confidence = supportingSamples == 0
                ? 0
                : (double)supportingSamples / totalSamples *
                  (1 - (double)supportingError / (supportingSamples * 4 * 12));
            return supported >= 3 && leading && trailing;
        }
    }
}
