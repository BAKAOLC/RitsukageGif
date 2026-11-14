namespace RitsukageGif.Core
{
    public sealed partial class ScrollCaptureSession
    {
        private ScrollCaptureResult FindMatch(byte[] pixels, CancellationToken token, out bool useDifferenceMask,
            out int exactEdgeBytes)
        {
            useDifferenceMask = false;
            exactEdgeBytes = 0;
            if (_hasReference)
            {
                var direct = FindMatchAgainst(pixels, true, false, token);
                if (direct.Status is ScrollCaptureStatus.Appended or ScrollCaptureStatus.AlreadyCovered) return direct;
                if (FindSparseMatch(pixels, true, token) is { } sparse) return sparse;
                if (SparseEdgeBytes > 0 &&
                    FindSparseMatch(pixels, true, token, SparseEdgeBytes) is
                        { Status: ScrollCaptureStatus.Appended } inner &&
                    ExactOverlap(pixels, inner.Offset, false, token, SparseEdgeBytes))
                {
                    exactEdgeBytes = SparseEdgeBytes;
                    return inner;
                }

                BuildDifferenceMask(pixels, token);
                var masked = FindMatchAgainst(pixels, true, true, token);
                if (masked.Status == ScrollCaptureStatus.Appended && HasDistributedMotion(pixels, masked.Offset, token))
                {
                    useDifferenceMask = true;
                    return masked;
                }
            }

            if (_options.UseDiskStorage && _end - StartOffset > _axisLength * 2L)
            {
                if (FindSparseMatch(pixels, false, token) is { } exact) return exact;
                if (SparseEdgeBytes > 0 && FindSparseMatch(pixels, false, token, SparseEdgeBytes) is { } sparse)
                {
                    exactEdgeBytes = SparseEdgeBytes;
                    return sparse;
                }
            }

            var history = FindMatchAgainst(pixels, false, false, token);
            if (history.Status is not (ScrollCaptureStatus.Unmatched or ScrollCaptureStatus.Ambiguous)) return history;
            if (FindSparseMatch(pixels, false, token) is { } sparseHistory) return sparseHistory;
            if (SparseEdgeBytes > 0 && FindSparseMatch(pixels, false, token, SparseEdgeBytes) is { } innerHistory)
            {
                exactEdgeBytes = SparseEdgeBytes;
                return innerHistory;
            }

            if (!_hasReference || history.Status != ScrollCaptureStatus.Unmatched ||
                !TryFindRegionalMotion(pixels, token, out var offset, out var confidence)) return history;
            // Regional evidence must not change an already covered viewport without a verified join.
            if (offset >= StartOffset && offset + _axisLength <= _end) return history;
            useDifferenceMask = true;
            return Result(ScrollCaptureStatus.Appended, offset, 0, confidence);
        }

        private ScrollCaptureResult FindMatchAgainst(byte[] pixels, bool previous, bool masked, CancellationToken token)
        {
            var start = previous ? _referenceOffset : StartOffset;
            var end = previous ? _referenceOffset + _axisLength : _end;
            var current = previous ? _referenceOffset : CurrentOffset;
            if (!masked && Score(pixels, current, 48, 48, out _, previous, false) < 0.05 &&
                ExactOverlap(pixels, current, previous, token))
                return Result(ScrollCaptureStatus.AlreadyCovered, current, 0, 1);
            var minimum = Math.Max(16, (int)Math.Ceiling(_axisLength * _options.MinimumOverlap));
            var first = start - _axisLength + minimum;
            var last = end - minimum;
            if (!previous && _options.UseDiskStorage)
            {
                first = Math.Max(first, current - _axisLength);
                last = Math.Min(last, current + _axisLength);
            }

            Span<int> offsets = stackalloc int[12];
            Span<double> scores = stackalloc double[12];
            scores.Fill(double.PositiveInfinity);
            // Visit the last confirmed viewport first, then search both directions to tighten the score bound early.
            if (first > last) return Result(ScrollCaptureStatus.Unmatched);
            var center = Math.Clamp(current, first, last);
            var left = center;
            var right = center + 1;
            for (var visited = 0; left >= first || right <= last; visited++)
            {
                if ((visited & 127) == 0) token.ThrowIfCancellationRequested();
                var offset = left >= first && (right > last || center - left <= right - center) ? left-- : right++;
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

            for (var i = 0; i < offsets.Length; i++)
            {
                if (!double.IsFinite(scores[i])) continue;
                token.ThrowIfCancellationRequested();
                var score = Score(pixels, offsets[i], 64, 64, out var texture, previous, masked);
                scores[i] = texture >= 2 && score <= 6 ? score : double.PositiveInfinity;
            }

            var bestOffset = 0;
            var best = double.PositiveInfinity;
            while (true)
            {
                var candidate = 0;
                for (var i = 1; i < scores.Length; i++)
                    if (scores[i] < scores[candidate])
                        candidate = i;
                var score = scores[candidate];
                if (!double.IsFinite(score) || score >= best * 1.5 + 0.15) break;
                scores[candidate] = double.PositiveInfinity;
                var offset = offsets[candidate];
                if (!VerifyMatch(pixels, offset, previous, masked, token)) continue;
                if (double.IsFinite(best))
                    return Result(ScrollCaptureStatus.Ambiguous, bestOffset, 0, Math.Clamp(1 - best / 12, 0, 1));

                best = score;
                bestOffset = offset;
            }

            if (!double.IsFinite(best)) return Result(ScrollCaptureStatus.Unmatched);
            var confidence = Math.Clamp(1 - best / 12, 0, 1);
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
                var storedNext = ReferenceRow(position + 1, previous, true);
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
