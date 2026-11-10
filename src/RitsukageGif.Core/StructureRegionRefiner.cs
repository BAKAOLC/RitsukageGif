using System.Buffers;

namespace RitsukageGif.Core
{
    internal static class StructureRegionRefiner
    {
        private static readonly IComparer<PixelRect> LineOrder = Comparer<PixelRect>.Create(static (first, second) =>
        {
            var order = (first.Y + first.Height / 2).CompareTo(second.Y + second.Height / 2);
            return order != 0 ? order : first.X.CompareTo(second.X);
        });

        internal static (IReadOnlyList<PixelRect> Regions, IReadOnlyList<bool> ImageEligible,
            IReadOnlyList<bool> RelationEligible) Refine(ReadOnlySpan<byte> pixels, int stride,
                StructureRegionAnalysis analysis, CancellationToken token)
        {
            var coarse = analysis.Regions;
            if (coarse.Count == 0) return (coarse, [], []);
            var regions = new List<PixelRect>(coarse);
            var imageEligible = new List<bool>(coarse.Count);
            var relationEligible = new List<bool>(coarse.Count);
            var output = new Dictionary<PixelRect, int>();
            var reliable = new bool[coarse.Count];
            for (var index = 0; index < coarse.Count; index++)
            {
                imageEligible.Add(!analysis.ContainerParents.Contains(coarse[index]));
                relationEligible.Add(!analysis.ContainerParents.Contains(coarse[index]));
                output.TryAdd(coarse[index], index);
                reliable[index] = imageEligible[index] &&
                                  StructureCandidateClassifier.IsReliableImage(pixels, stride, coarse[index]);
            }

            var cores = new List<PixelRect>();
            var coreIndices = new Dictionary<PixelRect, int>();
            var owners = new List<(int Core, int Parent)>();
            var step = analysis.Step;
            var maximumColumns = (analysis.Width + step - 1) / step;
            var buffer = ArrayPool<int>.Shared.Rent(checked(maximumColumns * 3));
            try
            {
                for (var parentIndex = 0; parentIndex < coarse.Count; parentIndex++)
                {
                    token.ThrowIfCancellationRequested();
                    var parent = coarse[parentIndex];
                    if (analysis.ContainerParents.Contains(parent) || parent.Height < 16 ||
                        (analysis.BoundaryOnly[parentIndex] && !reliable[parentIndex])) continue;
                    var columns = (parent.Width + step - 1) / step;
                    var rows = (parent.Height + step - 1) / step;
                    var background = Background(pixels, stride, parent);
                    var counts = buffer.AsSpan(0, columns);
                    var tops = buffer.AsSpan(maximumColumns, columns);
                    var bottoms = buffer.AsSpan(maximumColumns * 2, columns);
                    counts.Clear();
                    tops.Fill(rows);
                    bottoms.Fill(-1);
                    for (var y = 0; y < rows; y++)
                    {
                        token.ThrowIfCancellationRequested();
                        for (var x = 0; x < columns; x++)
                        {
                            var sourceX = Math.Min(parent.Right - 1, parent.X + x * step + step / 2);
                            var sourceY = Math.Min(parent.Bottom - 1, parent.Y + y * step + step / 2);
                            if (ColorDifference(VisibleColor(pixels, sourceY * stride + sourceX * 4), background) <
                                30 &&
                                !IsEdge(pixels, stride, parent, x, y, step)) continue;
                            counts[x]++;
                            tops[x] = Math.Min(tops[x], y);
                            bottoms[x] = y;
                        }
                    }

                    var start = -1;
                    var firstOwner = owners.Count;
                    for (var x = 0; x <= columns; x++)
                    {
                        var tall = x < columns && (counts[x] >= Math.Max(3, rows * .35) ||
                                                   bottoms[x] - tops[x] + 1 >= rows * .75);
                        if (tall && start < 0) start = x;
                        if (tall || start < 0) continue;
                        var top = rows;
                        var bottom = -1;
                        for (var column = start; column < x; column++)
                        {
                            top = Math.Min(top, tops[column]);
                            bottom = Math.Max(bottom, bottoms[column]);
                        }

                        var left = Math.Max(parent.X, parent.X + (start - 1) * step);
                        var right = Math.Min(parent.Right, parent.X + x * step);
                        var upper = Math.Max(parent.Y, parent.Y + (top - 1) * step);
                        var lower = Math.Min(parent.Bottom, parent.Y + (bottom + 1) * step);
                        var core = new PixelRect(left, upper, right - left, lower - upper);
                        start = -1;
                        if (core.Width < core.Height * .6 || core.Width > core.Height * 1.65 || core.Height < 16)
                            continue;
                        if (!coreIndices.TryGetValue(core, out var coreIndex))
                        {
                            if (!StructureCandidateClassifier.IsReliableImage(pixels, stride, core)) continue;
                            coreIndex = cores.Count;
                            coreIndices.Add(core, coreIndex);
                            cores.Add(core);
                        }

                        owners.Add((coreIndex, parentIndex));
                    }

                    if (owners.Count == firstOwner && reliable[parentIndex])
                    {
                        if (!coreIndices.TryGetValue(parent, out var coreIndex))
                        {
                            coreIndex = cores.Count;
                            coreIndices.Add(parent, coreIndex);
                            cores.Add(parent);
                        }

                        owners.Add((coreIndex, parentIndex));
                    }
                }
            }
            finally
            {
                ArrayPool<int>.Shared.Return(buffer);
            }

            if (cores.Count == 0) return (regions, imageEligible, relationEligible);
            var kinds = new PrivacyCandidateKind[cores.Count];
            Array.Fill(kinds, PrivacyCandidateKind.ImageBlock);
            using var indexOfCores = new StructureSeedIndex(cores, kinds, token);
            var sides = new List<PixelRect>[cores.Count];
            for (var index = 0; index < cores.Count; index++)
            {
                token.ThrowIfCancellationRequested();
                var core = cores[index];
                var reach = (int)Math.Ceiling(core.Width * 4.55);
                var top = Math.Max(0, (int)Math.Floor(core.Y - core.Height * .2));
                var bottom = Math.Min(analysis.Height, (int)Math.Ceiling(core.Bottom + core.Height * .2));
                var fragments = new List<PixelRect>();
                AddFragments(analysis, new(Math.Max(0, core.X - reach), top,
                    core.X - Math.Max(0, core.X - reach), bottom - top), indexOfCores, fragments, token);
                AddFragments(analysis, new(core.Right, top, Math.Min(analysis.Width, core.Right + reach) - core.Right,
                    bottom - top), indexOfCores, fragments, token);
                sides[index] = GroupLines(fragments);
            }

            var countsPerParent = new int[coarse.Count];
            foreach (var owner in owners) countsPerParent[owner.Parent]++;
            foreach (var owner in owners)
            {
                var core = cores[owner.Core];
                var same = core == coarse[owner.Parent];
                if (!same)
                {
                    if (reliable[owner.Parent] && sides[owner.Core].Count == 0 &&
                        countsPerParent[owner.Parent] < 2) continue;
                    imageEligible[owner.Parent] = false;
                    relationEligible[owner.Parent] = false;
                    if (!output.ContainsKey(core))
                    {
                        output.Add(core, regions.Count);
                        regions.Add(core);
                        imageEligible.Add(true);
                        relationEligible.Add(false);
                    }
                }

                foreach (var side in sides[owner.Core])
                {
                    if (!output.TryAdd(side, regions.Count)) continue;
                    regions.Add(side);
                    imageEligible.Add(false);
                    relationEligible.Add(true);
                }
            }

            return (regions, imageEligible, relationEligible);
        }

        private static void AddFragments(StructureRegionAnalysis analysis, PixelRect area, StructureSeedIndex cores,
            List<PixelRect> fragments, CancellationToken token)
        {
            foreach (var fragment in analysis.LocalComponents(area, token))
                if (cores.Query(fragment).IsEmpty)
                    fragments.Add(fragment);
        }

        private static List<PixelRect> GroupLines(List<PixelRect> fragments)
        {
            fragments.Sort(LineOrder);
            var result = new List<PixelRect>();
            foreach (var fragment in fragments)
            {
                var selected = -1;
                var bestOverlap = 0;
                var bestHeight = 1;
                var bestGap = int.MaxValue;
                for (var index = 0; index < result.Count; index++)
                {
                    var line = result[index];
                    var overlap = Math.Min(line.Bottom, fragment.Bottom) - Math.Max(line.Y, fragment.Y);
                    var smallerHeight = Math.Min(line.Height, fragment.Height);
                    var largerHeight = Math.Max(line.Height, fragment.Height);
                    var gap = Math.Max(0, Math.Max(line.X - fragment.Right, fragment.X - line.Right));
                    if (overlap <= 0 || largerHeight > smallerHeight * 2 || gap > largerHeight) continue;
                    var comparison = (long)overlap * bestHeight - (long)bestOverlap * smallerHeight;
                    if (comparison < 0 || (comparison == 0 && gap >= bestGap)) continue;
                    selected = index;
                    bestOverlap = overlap;
                    bestHeight = smallerHeight;
                    bestGap = gap;
                }

                if (selected < 0)
                {
                    result.Add(fragment);
                    continue;
                }

                var previous = result[selected];
                var left = Math.Min(previous.X, fragment.X);
                var top = Math.Min(previous.Y, fragment.Y);
                var right = Math.Max(previous.Right, fragment.Right);
                var bottom = Math.Max(previous.Bottom, fragment.Bottom);
                result[selected] = new(left, top, right - left, bottom - top);
            }

            return result;
        }

        private static bool IsEdge(ReadOnlySpan<byte> pixels, int stride, PixelRect area, int x, int y, int step)
        {
            var sourceX = Math.Min(area.Right - 1, area.X + x * step + step / 2);
            var sourceY = Math.Min(area.Bottom - 1, area.Y + y * step + step / 2);
            var offset = sourceY * stride + sourceX * 4;
            return (x > 0 && Contrast(pixels, offset, sourceY * stride + Math.Max(area.X, sourceX - step) * 4) >= 30) ||
                   (y > 0 && Contrast(pixels, offset, Math.Max(area.Y, sourceY - step) * stride + sourceX * 4) >= 30);
        }

        private static int Contrast(ReadOnlySpan<byte> pixels, int first, int second)
        {
            return Math.Abs(Composite(pixels, first, 0) - Composite(pixels, second, 0)) +
                   Math.Abs(Composite(pixels, first, 1) - Composite(pixels, second, 1)) +
                   Math.Abs(Composite(pixels, first, 2) - Composite(pixels, second, 2));
        }

        private static int Composite(ReadOnlySpan<byte> pixels, int offset, int channel)
        {
            var alpha = pixels[offset + 3];
            return (pixels[offset + channel] * alpha + 255 * (255 - alpha) + 127) / 255;
        }

        private static uint VisibleColor(ReadOnlySpan<byte> pixels, int offset)
        {
            return (uint)((Composite(pixels, offset, 2) << 16) | (Composite(pixels, offset, 1) << 8) |
                          Composite(pixels, offset, 0));
        }

        private static int ColorDifference(uint left, uint right)
        {
            return Math.Abs((int)(left & 255) - (int)(right & 255)) +
                   Math.Abs((int)((left >> 8) & 255) - (int)((right >> 8) & 255)) +
                   Math.Abs((int)(left >> 16) - (int)(right >> 16));
        }

        private static uint Background(ReadOnlySpan<byte> pixels, int stride, PixelRect area)
        {
            var result = 0u;
            var minimum = int.MaxValue;
            for (var corner = 0; corner < 4; corner++)
            {
                var x = (corner & 1) == 0 ? area.X : area.Right - 1;
                var y = corner < 2 ? area.Y : area.Bottom - 1;
                var color = VisibleColor(pixels, y * stride + x * 4);
                var difference = 0;
                for (var other = 0; other < 4; other++)
                {
                    var otherX = (other & 1) == 0 ? area.X : area.Right - 1;
                    var otherY = other < 2 ? area.Y : area.Bottom - 1;
                    difference += ColorDifference(color, VisibleColor(pixels, otherY * stride + otherX * 4));
                }

                if (difference >= minimum) continue;
                minimum = difference;
                result = color;
            }

            return result;
        }
    }
}
