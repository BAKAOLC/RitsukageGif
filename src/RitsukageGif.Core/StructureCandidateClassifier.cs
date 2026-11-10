using System.Buffers;

namespace RitsukageGif.Core
{
    internal static class StructureCandidateClassifier
    {
        private const int SampleSide = 32;
        private const byte WeakImage = 1;
        private const byte RelatedContent = 2;

        internal static PrivacyCandidateKind[] Classify(ReadOnlySpan<byte> pixels, int stride,
            IReadOnlyList<PixelRect> regions, IReadOnlyList<bool> imageEligible,
            IReadOnlyList<bool> relationEligible, CancellationToken token)
        {
            var kinds = new PrivacyCandidateKind[regions.Count];
            if (regions.Count == 0) return kinds;
            var featureBuffer = ArrayPool<byte>.Shared.Rent(regions.Count);
            try
            {
                var features = featureBuffer.AsSpan(0, regions.Count);
                Span<uint> samples = stackalloc uint[SampleSide * SampleSide];
                Span<int> histogram = stackalloc int[32];
                Span<int> colorHistogram = stackalloc int[64];
                Span<int> activeCells = stackalloc int[9];
                Span<int> varyingCells = stackalloc int[9];
                var seedCount = 0;
                for (var index = 0; index < regions.Count; index++)
                {
                    token.ThrowIfCancellationRequested();
                    if (!imageEligible[index] && !relationEligible[index])
                    {
                        features[index] = 0;
                        kinds[index] = PrivacyCandidateKind.ImageStructure;
                        continue;
                    }

                    var (reliable, feature) = Analyze(pixels, stride, regions[index], samples, histogram,
                        colorHistogram, activeCells, varyingCells);
                    features[index] = (byte)((imageEligible[index] ? feature & WeakImage : 0) |
                                             (relationEligible[index] ? feature & RelatedContent : 0));
                    kinds[index] = reliable && imageEligible[index]
                        ? PrivacyCandidateKind.ImageBlock
                        : PrivacyCandidateKind.ImageStructure;
                    if (kinds[index] == PrivacyCandidateKind.ImageBlock) seedCount++;
                }

                if (seedCount == 0) return kinds;
                using var seeds = new StructureSeedIndex(regions, kinds, token);
                for (var index = 0; index < kinds.Length; index++)
                {
                    token.ThrowIfCancellationRequested();
                    if (kinds[index] == PrivacyCandidateKind.ImageBlock) continue;
                    if ((features[index] & WeakImage) != 0 && HasColumnSupport(regions[index], regions, seeds))
                        kinds[index] = PrivacyCandidateKind.ImageBlock;
                    else if ((features[index] & RelatedContent) != 0 && HasRelatedSeed(regions[index], regions, seeds))
                        kinds[index] = PrivacyCandidateKind.RelatedStructure;
                }

                return kinds;
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(featureBuffer);
            }
        }

        internal static bool IsReliableImage(ReadOnlySpan<byte> pixels, int stride, PixelRect region)
        {
            Span<uint> samples = stackalloc uint[SampleSide * SampleSide];
            Span<int> histogram = stackalloc int[32];
            Span<int> colorHistogram = stackalloc int[64];
            Span<int> activeCells = stackalloc int[9];
            Span<int> varyingCells = stackalloc int[9];
            return Analyze(pixels, stride, region, samples, histogram, colorHistogram, activeCells, varyingCells)
                .Reliable;
        }

        private static (bool Reliable, byte Features) Analyze(ReadOnlySpan<byte> pixels, int stride,
            PixelRect region, Span<uint> samples, Span<int> histogram, Span<int> colorHistogram, Span<int> activeCells,
            Span<int> varyingCells)
        {
            histogram.Clear();
            colorHistogram.Clear();
            activeCells.Clear();
            varyingCells.Clear();
            var minimumRed = 255;
            var maximumRed = 0;
            var minimumGreen = 255;
            var maximumGreen = 0;
            var minimumBlue = 255;
            var maximumBlue = 0;
            for (var y = 0; y < SampleSide; y++)
            {
                var sourceY = region.Y + (2 * y + 1) * region.Height / (2 * SampleSide);
                for (var x = 0; x < SampleSide; x++)
                {
                    var sourceX = region.X + (2 * x + 1) * region.Width / (2 * SampleSide);
                    var offset = sourceY * stride + sourceX * 4;
                    var alpha = pixels[offset + 3];
                    var blue = Composite(pixels[offset], alpha);
                    var green = Composite(pixels[offset + 1], alpha);
                    var red = Composite(pixels[offset + 2], alpha);
                    samples[y * SampleSide + x] = (uint)((red << 16) | (green << 8) | blue);
                    histogram[(77 * red + 150 * green + 29 * blue + 128) >> 8 >> 3]++;
                    colorHistogram[((red >> 6) << 4) | ((green >> 6) << 2) | (blue >> 6)]++;
                    minimumRed = Math.Min(minimumRed, red);
                    maximumRed = Math.Max(maximumRed, red);
                    minimumGreen = Math.Min(minimumGreen, green);
                    maximumGreen = Math.Max(maximumGreen, green);
                    minimumBlue = Math.Min(minimumBlue, blue);
                    maximumBlue = Math.Max(maximumBlue, blue);
                }
            }

            var (grayDiversity, grayDominant) = Distribution(histogram);
            var (colorDiversity, colorDominant) = Distribution(colorHistogram);
            var diversity = Math.Max(grayDiversity, colorDiversity);
            var dominant = Math.Min(grayDominant, colorDominant);
            var range = Math.Max(maximumRed - minimumRed,
                Math.Max(maximumGreen - minimumGreen, maximumBlue - minimumBlue));

            var variation = 0;
            var active = 0;
            var varying = 0;
            var interiorActive = 0;
            for (var y = 1; y < SampleSide - 1; y++)
            for (var x = 1; x < SampleSide - 1; x++)
            {
                var offset = y * SampleSide + x;
                var difference = (ColorDifference(samples[offset], samples[offset - 1]) +
                                  ColorDifference(samples[offset], samples[offset - SampleSide])) / 3;
                variation += difference;
                if (difference >= 2)
                {
                    varying++;
                    varyingCells[(y - 1) / 10 * 3 + (x - 1) / 10]++;
                }

                if (difference < 12) continue;
                active++;
                activeCells[(y - 1) / 10 * 3 + (x - 1) / 10]++;
                if (x is >= SampleSide / 8 and < SampleSide * 7 / 8 &&
                    y is >= SampleSide / 4 and < SampleSide * 3 / 4) interiorActive++;
            }

            var coveredCells = 0;
            foreach (var count in activeCells)
                if (count >= 8)
                    coveredCells++;
            var variedCells = 0;
            foreach (var count in varyingCells)
                if (count >= 8)
                    variedCells++;
            var shorter = Math.Min(region.Width, region.Height);
            var longer = Math.Max(region.Width, region.Height);
            var compact = longer <= shorter * 1.45;
            var continuous = diversity >= 5.5 && dominant <= 615 && range >= 64 &&
                             variation >= 900 * 8 && active >= 120 && coveredCells >= 5;
            var smooth = diversity >= 5.5 && dominant <= 410 && range >= 96 &&
                         variation >= 900 * 2 && varying >= 90 && variedCells >= 8;
            var weak = longer <= shorter * 1.65 && diversity >= 4.5 && dominant <= 665 &&
                       range >= 40 && variation >= 900 * 4 && active >= 60 && coveredCells >= 4;
            var related = diversity < 4.5 && dominant >= 300 && variation >= 900 * 3 &&
                          active >= 30 && interiorActive >= 8;
            return (compact && (continuous || smooth), (byte)((weak ? WeakImage : 0) | (related ? RelatedContent : 0)));
        }

        private static bool HasColumnSupport(PixelRect region, IReadOnlyList<PixelRect> regions,
            StructureSeedIndex seeds)
        {
            var centerX = region.X + region.Width / 2d;
            var centerY = region.Y + region.Height / 2d;
            var area = SearchBounds(centerX - region.Width * .25, centerY - region.Height * 8.1,
                centerX + region.Width * .25, centerY + region.Height * 8.1);
            foreach (var index in seeds.Query(area))
            {
                var seed = regions[index];
                var widthRatio = (double)Math.Max(region.Width, seed.Width) / Math.Min(region.Width, seed.Width);
                var heightRatio = (double)Math.Max(region.Height, seed.Height) / Math.Min(region.Height, seed.Height);
                if (widthRatio <= 1.35 && heightRatio <= 1.35 &&
                    Math.Abs(centerX - seed.X - seed.Width / 2d) <= Math.Min(region.Width, seed.Width) * .25 &&
                    Math.Abs(centerY - seed.Y - seed.Height / 2d) <= Math.Max(region.Height, seed.Height) * 6)
                    return true;
            }

            return false;
        }

        private static bool HasRelatedSeed(PixelRect region, IReadOnlyList<PixelRect> regions,
            StructureSeedIndex seeds)
        {
            var best = double.PositiveInfinity;
            var second = double.PositiveInfinity;
            var bestIndex = -1;
            var reach = seeds.MaximumWidth * .55 + 1;
            var area = SearchBounds(region.X - reach, region.Y, region.Right + reach, region.Bottom);
            foreach (var index in seeds.Query(area))
            {
                var seed = regions[index];
                if (seed.Y >= region.Bottom || region.Y >= seed.Bottom) continue;
                var left = seed.Right <= region.X;
                var right = region.Right <= seed.X;
                if (!left && !right) continue;
                var gap = left ? region.X - seed.Right : seed.X - region.Right;
                if (region.Height > seed.Height * .5 || region.Width > seed.Width * 4 ||
                    Math.Abs(region.Y - seed.Y) > seed.Height * .2 || gap > seed.Width * .55) continue;
                var score = (double)gap / seed.Width + (double)Math.Abs(region.Y - seed.Y) / seed.Height;
                if (score < best)
                {
                    second = best;
                    best = score;
                    bestIndex = index;
                }
                else
                {
                    second = Math.Min(second, score);
                }
            }

            if (bestIndex < 0 || second - best <= .15) return false;
            var support = regions[bestIndex];
            var corridorLeft = support.Right <= region.X ? support.Right : region.X;
            var corridorRight = support.Right <= region.X ? region.Right : support.X;
            var corridor = new PixelRect(corridorLeft, region.Y, corridorRight - corridorLeft, region.Height);
            foreach (var index in seeds.Query(corridor))
                if (index != bestIndex)
                    return false;
            return true;
        }

        private static PixelRect SearchBounds(double left, double top, double right, double bottom)
        {
            var x = (int)Math.Floor(left);
            var y = (int)Math.Floor(top);
            return new(x, y, (int)Math.Ceiling(right) - x, (int)Math.Ceiling(bottom) - y);
        }

        private static (double Diversity, int Dominant) Distribution(ReadOnlySpan<int> histogram)
        {
            var squares = 0;
            var dominant = 0;
            foreach (var count in histogram)
            {
                squares += count * count;
                dominant = Math.Max(dominant, count);
            }

            return ((double)(SampleSide * SampleSide * SampleSide * SampleSide) / squares, dominant);
        }

        private static int Composite(int value, int alpha)
        {
            return (value * alpha + 255 * (255 - alpha) + 127) / 255;
        }

        private static int ColorDifference(uint left, uint right)
        {
            return Math.Abs((int)(left & 255) - (int)(right & 255)) +
                   Math.Abs((int)((left >> 8) & 255) - (int)((right >> 8) & 255)) +
                   Math.Abs((int)(left >> 16) - (int)(right >> 16));
        }
    }
}
