namespace RitsukageGif.Core
{
    public sealed partial class ScrollCaptureSession
    {
        private ScrollCaptureResult FindMatch(byte[] pixels, CancellationToken token)
        {
            if (Score(pixels, CurrentOffset, 48, 48, out _) < 0.05 && VerifyMatch(pixels, CurrentOffset, token))
                return Result(ScrollCaptureStatus.AlreadyCovered, CurrentOffset, 0, 1);
            var minimum = Math.Max(16, (int)Math.Ceiling(_axisLength * _options.MinimumOverlap));
            var first = StartOffset - _axisLength + minimum;
            var last = _end - minimum;
            Span<int> offsets = stackalloc int[12];
            Span<double> scores = stackalloc double[12];
            scores.Fill(double.PositiveInfinity);
            for (var offset = first; offset <= last; offset++)
            {
                if ((offset & 127) == 0) token.ThrowIfCancellationRequested();
                var score = Score(pixels, offset, 12, 12, out var texture, scores[^1]);
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
                var score = Score(pixels, offsets[i], 64, 64, out var texture);
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
            if (!VerifyMatch(pixels, bestOffset, token)) return Result(ScrollCaptureStatus.Unmatched);
            return Result(ScrollCaptureStatus.Appended, bestOffset, 0, confidence);
        }

        private double Score(byte[] pixels, int offset, int axisSamples, int crossSamples, out double texture,
            double maximumScore = double.PositiveInfinity)
        {
            var from = Math.Max(StartOffset, offset);
            var to = Math.Min(_end, offset + _axisLength);
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
            var bounded = axisSamples < 16 && double.IsFinite(maximumScore);
            var retainedCount = axisSamples - axisSamples / 8;
            for (var y = 0; y < axisSamples; y++)
            {
                var position = from + y * (to - from - 2) / Math.Max(1, axisSamples - 1);
                var current = pixels.AsSpan((position - offset) * _rowBytes, _rowBytes);
                var next = pixels.AsSpan((position - offset + 1) * _rowBytes, _rowBytes);
                var stored = Row(position);
                var storedNext = Row(position + 1);
                var error = 0.0;
                for (var x = 0; x < crossSamples; x++)
                {
                    var index = (_crossLength - 1) * x / Math.Max(1, crossSamples - 1) * 4;
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

                errors[y] = error / (crossSamples * 3);
                if (!bounded) continue;
                // With fewer than 16 rows, trimming removes at most one row. Unseen errors cannot lower this bound.
                lowerBoundTotal += errors[y];
                if (axisSamples >= 8) largestError = Math.Max(largestError, errors[y]);
                if ((lowerBoundTotal - largestError) / retainedCount <= maximumScore + 1e-9) continue;
                texture = 0;
                return double.PositiveInfinity;
            }

            errors = errors[..axisSamples];
            errors.Sort();
            var count = Math.Max(1, axisSamples - axisSamples / 8);
            var total = 0.0;
            for (var i = 0; i < count; i++) total += errors[i];
            texture = textureTotal / (axisSamples * crossSamples * 3);
            return total / count;
        }
    }
}
