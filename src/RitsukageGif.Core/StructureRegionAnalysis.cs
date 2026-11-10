using System.Buffers;

namespace RitsukageGif.Core
{
    internal sealed class StructureRegionAnalysis : IDisposable
    {
        private readonly byte[] _edges;
        private readonly byte[] _scratch;
        private readonly int[] _queue;
        private readonly int _columns;
        private readonly int _rows;
        private readonly Dictionary<PixelRect, IReadOnlyList<PixelRect>> _local = [];
        private readonly List<PixelRect> _probes = [];
        private bool _disposed;

        internal StructureRegionAnalysis(ReadOnlySpan<byte> pixels, int width, int height, int stride,
            CancellationToken token)
        {
            Width = width;
            Height = height;
            Step = Math.Max(1, (Math.Max(width, height) + 2047) / 2048);
            _columns = (width + Step - 1) / Step;
            _rows = (height + Step - 1) / Step;
            var length = checked(_columns * _rows);
            _edges = ArrayPool<byte>.Shared.Rent(length);
            _scratch = ArrayPool<byte>.Shared.Rent(length);
            _queue = ArrayPool<int>.Shared.Rent(length);
            try
            {
                _edges.AsSpan(0, length).Clear();
                for (var y = 0; y < _rows; y++)
                {
                    token.ThrowIfCancellationRequested();
                    var sy = Math.Min(height - 1, y * Step + Step / 2);
                    for (var x = 0; x < _columns; x++)
                    {
                        var sx = Math.Min(width - 1, x * Step + Step / 2);
                        var offset = sy * stride + sx * 4;
                        var index = y * _columns + x;
                        if (x > 0 && Contrast(pixels, offset, sy * stride +
                                                              Math.Min(width - 1, (x - 1) * Step + Step / 2) * 4) >= 30)
                            _edges[index] = _edges[index - 1] = 1;
                        if (y > 0 && Contrast(pixels, offset,
                                Math.Min(height - 1, (y - 1) * Step + Step / 2) * stride + sx * 4) >= 30)
                            _edges[index] = _edges[index - _columns] = 1;
                    }
                }

                var regions = Components(new(0, 0, _columns, _rows), 8, true, token);
                RestoreContainerChildren(pixels, stride, regions, token);
                Regions = regions;
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        internal int Width { get; }
        internal int Height { get; }
        internal int Step { get; }
        internal IReadOnlyList<PixelRect> Regions { get; }
        internal List<bool> BoundaryOnly { get; } = [];
        internal HashSet<PixelRect> ContainerParents { get; } = [];

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            ArrayPool<byte>.Shared.Return(_edges);
            ArrayPool<byte>.Shared.Return(_scratch);
            ArrayPool<int>.Shared.Return(_queue);
        }

        internal IReadOnlyList<PixelRect> LocalComponents(PixelRect area, CancellationToken token)
        {
            area = area.Intersect(new(0, 0, Width, Height));
            if (area.IsEmpty) return [];
            if (_local.TryGetValue(area, out var cached)) return cached;
            var left = area.X / Step;
            var top = area.Y / Step;
            var right = Math.Min(_columns, (area.Right + Step - 1) / Step);
            var bottom = Math.Min(_rows, (area.Bottom + Step - 1) / Step);
            var result = Components(new(left, top, right - left, bottom - top), 3, false, token)
                .Select(bounds => bounds.Intersect(area)).Where(bounds => !bounds.IsEmpty).ToArray();
            _local.Add(area, result);
            return result;
        }

        private List<PixelRect> Components(PixelRect area, int minimumExtent, bool original,
            CancellationToken token)
        {
            Dilate(area, token);
            var result = new List<PixelRect>();
            for (var row = area.Y; row < area.Bottom; row++)
            for (var column = area.X; column < area.Right; column++)
            {
                var index = row * _columns + column;
                if ((_edges[index] & 6) != 2) continue;
                token.ThrowIfCancellationRequested();
                var read = 0;
                var write = 1;
                _queue[0] = index;
                _edges[index] |= 4;
                var left = area.Right;
                var right = area.X - 1;
                var top = area.Bottom;
                var bottom = area.Y - 1;
                while (read < write)
                {
                    if ((read & 4095) == 0) token.ThrowIfCancellationRequested();
                    var current = _queue[read++];
                    var x = current % _columns;
                    var y = current / _columns;
                    if ((_edges[current] & 1) != 0)
                    {
                        left = Math.Min(left, x);
                        right = Math.Max(right, x);
                        top = Math.Min(top, y);
                        bottom = Math.Max(bottom, y);
                    }

                    for (var ny = Math.Max(area.Y, y - 1); ny < Math.Min(area.Bottom, y + 2); ny++)
                    for (var nx = Math.Max(area.X, x - 1); nx < Math.Min(area.Right, x + 2); nx++)
                    {
                        var next = ny * _columns + nx;
                        if ((_edges[next] & 6) != 2) continue;
                        _edges[next] |= 4;
                        _queue[write++] = next;
                    }
                }

                var w = right - left + 1;
                var h = bottom - top + 1;
                if (w * Step < minimumExtent || h * Step < minimumExtent || w > h * 20 || h > w * 20 ||
                    write < Math.Max(w, h)) continue;
                if (original)
                {
                    _probes.Add(new(left, top, w, h));
                    if ((long)w * h > (long)area.Width * area.Height * .85) continue;
                }

                result.Add(new(left * Step, top * Step, Math.Min(Width, (right + 1) * Step) - left * Step,
                    Math.Min(Height, (bottom + 1) * Step) - top * Step));
                if (!original) continue;
                var depth = 0;
                for (var point = 0; point < write; point++)
                {
                    var cell = _queue[point];
                    if ((_edges[cell] & 1) == 0) continue;
                    var x = cell % _columns;
                    var y = cell / _columns;
                    depth = Math.Max(depth, Math.Min(Math.Min(x - left, right - x), Math.Min(y - top, bottom - y)));
                }

                BoundaryOnly.Add(depth <= Math.Max(3, Math.Min(w, h) / 20));
            }

            return result;
        }

        private void RestoreContainerChildren(ReadOnlySpan<byte> pixels, int stride, List<PixelRect> regions,
            CancellationToken token)
        {
            var known = new HashSet<PixelRect>(regions);
            var visited = new HashSet<PixelRect>();
            var interiors = new HashSet<PixelRect>();
            _probes.Sort(static (first, second) => ((long)second.Width * second.Height)
                .CompareTo((long)first.Width * first.Height));
            for (var probeIndex = 0; probeIndex < _probes.Count; probeIndex++)
            {
                token.ThrowIfCancellationRequested();
                var probe = _probes[probeIndex];
                if (!visited.Add(probe)) continue;
                if (!TryContainerInterior(pixels, stride, probe, out var interior)) continue;
                var parent = PhysicalBounds(probe);
                ContainerParents.Add(parent);
                if (known.Add(parent))
                {
                    regions.Add(parent);
                    BoundaryOnly.Add(true);
                }

                if (!interiors.Add(interior)) continue;
                foreach (var child in Components(interior, 8, false, token))
                {
                    _probes.Add(new(child.X / Step, child.Y / Step,
                        (child.Width + Step - 1) / Step, (child.Height + Step - 1) / Step));
                    if (!known.Add(child)) continue;
                    regions.Add(child);
                    BoundaryOnly.Add(false);
                }
            }
        }

        private PixelRect PhysicalBounds(PixelRect grid)
        {
            return new(grid.X * Step, grid.Y * Step, Math.Min(Width, grid.Right * Step) - grid.X * Step,
                Math.Min(Height, grid.Bottom * Step) - grid.Y * Step);
        }

        private bool TryContainerInterior(ReadOnlySpan<byte> pixels, int stride, PixelRect bounds,
            out PixelRect interior)
        {
            interior = default;
            var maximumInset = Math.Min(bounds.Width, bounds.Height) / 8;
            if (maximumInset < 2) return false;
            var previousFlat = false;
            var previousEdge = false;
            var previousColor = 0u;
            for (var inset = 0; inset <= maximumInset; inset++)
            {
                var ring = new PixelRect(bounds.X + inset, bounds.Y + inset,
                    bounds.Width - inset * 2, bounds.Height - inset * 2);
                var flat = FlatRing(pixels, stride, ring, out var color, out var edge);
                if (!flat)
                {
                    if (!interior.IsEmpty) break;
                    previousFlat = false;
                    continue;
                }

                if (previousFlat && (edge || previousEdge) && ColorDistance(color, previousColor) >= 30)
                {
                    var next = new PixelRect(ring.X + 1, ring.Y + 1, ring.Width - 2, ring.Height - 2);
                    if (next.Width * Step >= 16 && next.Height * Step >= 16 &&
                        FlatRing(pixels, stride, next, out var nextColor, out _) &&
                        ColorDistance(color, nextColor) < 30)
                        interior = next;
                }

                previousFlat = true;
                previousEdge = edge;
                previousColor = color;
            }

            return !interior.IsEmpty;
        }

        private bool FlatRing(ReadOnlySpan<byte> pixels, int stride, PixelRect ring, out uint color, out bool edge)
        {
            color = VisibleColor(pixels, stride, ring.X, ring.Y);
            edge = true;
            for (var side = 0; side < 4; side++)
            {
                var length = side < 2 ? ring.Width : ring.Height;
                var matching = 0;
                var raw = 0;
                for (var position = 0; position < length; position++)
                {
                    var x = side < 2 ? ring.X + position : side == 2 ? ring.X : ring.Right - 1;
                    var y = side >= 2 ? ring.Y + position : side == 0 ? ring.Y : ring.Bottom - 1;
                    if (ColorDistance(color, VisibleColor(pixels, stride, x, y)) < 30) matching++;
                    raw += _edges[y * _columns + x] & 1;
                }

                if (matching * 10 < length * 9) return false;
                if (raw * 5 < length * 4) edge = false;
            }

            return true;
        }

        private uint VisibleColor(ReadOnlySpan<byte> pixels, int stride, int x, int y)
        {
            var offset = Math.Min(Height - 1, y * Step + Step / 2) * stride +
                         Math.Min(Width - 1, x * Step + Step / 2) * 4;
            return (uint)(Composite(pixels, offset, 0) | (Composite(pixels, offset, 1) << 8) |
                          (Composite(pixels, offset, 2) << 16));
        }

        private static int ColorDistance(uint first, uint second)
        {
            return Math.Abs((int)(first & 255) - (int)(second & 255)) +
                   Math.Abs((int)((first >> 8) & 255) - (int)((second >> 8) & 255)) +
                   Math.Abs((int)(first >> 16) - (int)(second >> 16));
        }

        private void Dilate(PixelRect area, CancellationToken token)
        {
            for (var y = area.Y; y < area.Bottom; y++)
            {
                token.ThrowIfCancellationRequested();
                var count = 0;
                var row = y * _columns;
                for (var x = area.X; x <= Math.Min(area.X + 3, area.Right - 1); x++) count += _edges[row + x] & 1;
                for (var x = area.X; x < area.Right; x++)
                {
                    _scratch[row + x] = count == 0 ? (byte)0 : (byte)1;
                    if (x - 3 >= area.X) count -= _edges[row + x - 3] & 1;
                    if (x + 4 < area.Right) count += _edges[row + x + 4] & 1;
                }
            }

            for (var x = area.X; x < area.Right; x++)
            {
                token.ThrowIfCancellationRequested();
                var count = 0;
                for (var y = area.Y; y <= Math.Min(area.Y + 3, area.Bottom - 1); y++)
                    count += _scratch[y * _columns + x];
                for (var y = area.Y; y < area.Bottom; y++)
                {
                    var index = y * _columns + x;
                    _edges[index] = (byte)((_edges[index] & 1) | (count == 0 ? 0 : 2));
                    if (y - 3 >= area.Y) count -= _scratch[(y - 3) * _columns + x];
                    if (y + 4 < area.Bottom) count += _scratch[(y + 4) * _columns + x];
                }
            }
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
            return alpha == 255
                ? pixels[offset + channel]
                : (pixels[offset + channel] * alpha + 255 * (255 - alpha) + 127) / 255;
        }
    }
}
