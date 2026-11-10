using System.Buffers;

namespace RitsukageGif.Core
{
    internal sealed class StructureSeedIndex : IDisposable
    {
        private readonly IReadOnlyList<PixelRect> _regions;
        private readonly int[] _indices;
        private readonly int[] _results;
        private readonly StructureSeedNode[] _nodes;
        private readonly IComparer<int> _horizontal;
        private readonly IComparer<int> _vertical;
        private readonly CancellationToken _token;
        private int _nodeCount;
        private bool _disposed;

        internal StructureSeedIndex(IReadOnlyList<PixelRect> regions, ReadOnlySpan<PrivacyCandidateKind> kinds,
            CancellationToken token)
        {
            _regions = regions;
            _token = token;
            var count = 0;
            foreach (var kind in kinds)
                if (kind == PrivacyCandidateKind.ImageBlock)
                    count++;
            _indices = ArrayPool<int>.Shared.Rent(count);
            _results = ArrayPool<int>.Shared.Rent(count);
            _nodes = ArrayPool<StructureSeedNode>.Shared.Rent(checked(count * 2));
            var write = 0;
            for (var index = 0; index < kinds.Length; index++)
                if (kinds[index] == PrivacyCandidateKind.ImageBlock)
                {
                    _indices[write++] = index;
                    MaximumWidth = Math.Max(MaximumWidth, regions[index].Width);
                }

            _horizontal = Comparer<int>.Create((left, right) => Compare(left, right, true));
            _vertical = Comparer<int>.Create((left, right) => Compare(left, right, false));
            try
            {
                Build(0, count);
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        internal int MaximumWidth { get; }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            ArrayPool<int>.Shared.Return(_indices);
            ArrayPool<int>.Shared.Return(_results);
            ArrayPool<StructureSeedNode>.Shared.Return(_nodes);
        }

        internal ReadOnlySpan<int> Query(PixelRect bounds)
        {
            _token.ThrowIfCancellationRequested();
            var count = 0;
            Collect(0, bounds, ref count);
            return _results.AsSpan(0, count);
        }

        private int Build(int start, int count)
        {
            _token.ThrowIfCancellationRequested();
            var bounds = _regions[_indices[start]];
            for (var offset = 1; offset < count; offset++)
            {
                var region = _regions[_indices[start + offset]];
                var left = Math.Min(bounds.X, region.X);
                var top = Math.Min(bounds.Y, region.Y);
                bounds = new(left, top, Math.Max(bounds.Right, region.Right) - left,
                    Math.Max(bounds.Bottom, region.Bottom) - top);
            }

            var node = _nodeCount++;
            if (count <= 4)
            {
                _nodes[node] = new(bounds, start, count, -1, -1);
                return node;
            }

            Array.Sort(_indices, start, count, bounds.Width >= bounds.Height ? _horizontal : _vertical);
            var half = count / 2;
            var leftNode = Build(start, half);
            var rightNode = Build(start + half, count - half);
            _nodes[node] = new(bounds, start, 0, leftNode, rightNode);
            return node;
        }

        private void Collect(int index, PixelRect bounds, ref int count)
        {
            while (true)
            {
                if ((index & 63) == 0) _token.ThrowIfCancellationRequested();
                var node = _nodes[index];
                if (node.Bounds.Intersect(bounds).IsEmpty) return;
                if (node.Count == 0)
                {
                    Collect(node.Left, bounds, ref count);
                    index = node.Right;
                    continue;
                }

                for (var offset = 0; offset < node.Count; offset++)
                {
                    var seed = _indices[node.Start + offset];
                    if (!_regions[seed].Intersect(bounds).IsEmpty) _results[count++] = seed;
                }

                return;
            }
        }

        private int Compare(int left, int right, bool horizontal)
        {
            var first = _regions[left];
            var second = _regions[right];
            var firstCenter = horizontal ? first.X + first.Width / 2 : first.Y + first.Height / 2;
            var secondCenter = horizontal ? second.X + second.Width / 2 : second.Y + second.Height / 2;
            var order = firstCenter.CompareTo(secondCenter);
            return order != 0 ? order : left.CompareTo(right);
        }
    }
}
