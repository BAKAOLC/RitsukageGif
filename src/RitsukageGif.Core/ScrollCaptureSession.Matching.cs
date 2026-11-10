namespace RitsukageGif.Core
{
    public sealed partial class ScrollCaptureSession
    {
        private ScrollCaptureResult FindMatch(byte[] pixels, CancellationToken token, out bool useDifferenceMask)
        {
            useDifferenceMask = false;
            if (_hasReference)
            {
                var direct = FindMatchAgainst(pixels, true, false, token);
                if (direct.Status is ScrollCaptureStatus.Appended or ScrollCaptureStatus.AlreadyCovered) return direct;
                BuildDifferenceMask(pixels, token);
                var masked = FindMatchAgainst(pixels, true, true, token);
                if (masked.Status == ScrollCaptureStatus.Appended && HasDistributedMotion(pixels, masked.Offset, token))
                {
                    useDifferenceMask = true;
                    return masked;
                }
            }

            return FindMatchAgainst(pixels, false, false, token);
        }

        private ScrollCaptureResult FindMatchAgainst(byte[] pixels, bool previous, bool masked, CancellationToken token)
        {
            var start = previous ? _referenceOffset : StartOffset;
            var end = previous ? _referenceOffset + _axisLength : _end;
            var current = previous ? _referenceOffset : CurrentOffset;
            if (!masked && Score(pixels, current, 48, 48, out _, previous, false) < 0.05 &&
                VerifyMatch(pixels, current, previous, false, token))
                return Result(ScrollCaptureStatus.AlreadyCovered, current, 0, 1);
            var minimum = Math.Max(16, (int)Math.Ceiling(_axisLength * _options.MinimumOverlap));
            var first = start - _axisLength + minimum;
            var last = end - minimum;
            Span<int> offsets = stackalloc int[12];
            Span<double> scores = stackalloc double[12];
            scores.Fill(double.PositiveInfinity);
            for (var offset = first; offset <= last; offset++)
            {
                if ((offset & 127) == 0) token.ThrowIfCancellationRequested();
                if (masked && offset == current) continue;
                var score = Score(pixels, offset, 12, 12, out var texture, previous, masked, scores[^1]);
                if (texture < 2 || score >= scores[^1]) continue;
                var index = scores.Length - 1;
                while (index > 0 && score < scores[index - 1])
                {
                    scores[index] = scores[index - 1];
                    offsets[index] = offsets[index - 1];
                    index--;
                }

                scores[index] = score;
                offsets[index] = offset;
            }

            var best = double.PositiveInfinity;
            var second = double.PositiveInfinity;
            var bestOffset = 0;
            for (var i = 0; i < offsets.Length; i++)
            {
                if (!double.IsFinite(scores[i])) continue;
                token.ThrowIfCancellationRequested();
                var score = Score(pixels, offsets[i], 64, 64, out var texture, previous, masked);
                if (texture < 2) continue;
                if (score < best)
                {
                    second = best;
                    best = score;
                    bestOffset = offsets[i];
                }
                else
                {
                    second = Math.Min(second, score);
                }
            }

            if (best > 6) return Result(ScrollCaptureStatus.Unmatched);
            var confidence = Math.Clamp(1 - best / 12, 0, 1);
            if (second < best * 1.5 + 0.15)
                return Result(ScrollCaptureStatus.Ambiguous, bestOffset, 0, confidence);
            if (!VerifyMatch(pixels, bestOffset, previous, masked, token)) return Result(ScrollCaptureStatus.Unmatched);
            return Result(ScrollCaptureStatus.Appended, bestOffset, 0, confidence);
        }

        private double Score(byte[] pixels, int offset, int axisSamples, int crossSamples, out double texture,
            bool previous, bool masked, double maximumScore = double.PositiveInfinity)
        {
            var from = Math.Max(previous ? _referenceOffset : StartOffset, offset);
            var to = Math.Min(previous ? _referenceOffset + _axisLength : _end, offset + _axisLength);
            var inset = Math.Min((to - from) / 8, _axisLength / 10);
            from += inset;
            to -= inset;
            if (to - from < 4)
            {
                texture = 0;
                return double.PositiveInfinity;
            }

            axisSamples = Math.Min(axisSamples, to - from - 1);
            crossSamples = Math.Min(crossSamples, _crossLength);
            Span<double> errors = stackalloc double[64];
            var textureTotal = 0.0;
            var lowerBoundTotal = 0.0;
            var largestError = 0.0;
            var bounded = !masked && axisSamples < 16 && double.IsFinite(maximumScore);
            var validRows = 0;
            var validSamples = 0;
            var retainedCount = axisSamples - axisSamples / 8;
            for (var y = 0; y < axisSamples; y++)
            {
                var position = from + y * (to - from - 2) / Math.Max(1, axisSamples - 1);
                var current = pixels.AsSpan((position - offset) * _rowBytes, _rowBytes);
                var next = pixels.AsSpan((position - offset + 1) * _rowBytes, _rowBytes);
                var stored = ReferenceRow(position, previous);
                var storedNext = ReferenceRow(position + 1, previous);
                var error = 0.0;
                var samples = 0;
                for (var x = 0; x < crossSamples; x++)
                {
                    var cross = (_crossLength - 1) * x / Math.Max(1, crossSamples - 1);
                    if (masked && !ActiveSample(position, offset, cross)) continue;
                    var index = cross * 4;
                    samples++;
                    for (var channel = 0; channel < 3; channel++)
                    {
                        var value = current[index + channel];
                        var reference = stored[index + channel];
                        var gradient = next[index + channel] - value;
                        var referenceGradient = storedNext[index + channel] - reference;
                        error += Math.Abs(value - reference) + Math.Abs(gradient - referenceGradient) * 0.5;
                        textureTotal += Math.Min(Math.Abs(gradient), Math.Abs(referenceGradient));
                    }

                    error += Math.Abs(current[index + 3] - stored[index + 3]);
                }

                if (samples == 0) continue;
                validSamples += samples;
                errors[validRows++] = error / (samples * 3);
                if (!bounded) continue;
                // With fewer than 16 rows, trimming removes at most one row. Unseen errors cannot lower this bound.
                lowerBoundTotal += errors[validRows - 1];
                if (axisSamples >= 8) largestError = Math.Max(largestError, errors[validRows - 1]);
                if ((lowerBoundTotal - largestError) / retainedCount <= maximumScore + 1e-9) continue;
                texture = 0;
                return double.PositiveInfinity;
            }

            if (validRows == 0)
            {
                texture = 0;
                return double.PositiveInfinity;
            }

            errors = errors[..validRows];
            errors.Sort();
            var count = Math.Max(1, validRows - validRows / 8);
            var total = 0.0;
            for (var i = 0; i < count; i++) total += errors[i];
            texture = textureTotal / (validSamples * 3);
            return total / count;
        }
    }
}
