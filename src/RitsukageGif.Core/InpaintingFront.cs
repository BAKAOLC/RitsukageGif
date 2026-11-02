using System.Numerics;

namespace RitsukageGif.Core
{
    internal sealed class InpaintingFront(
        Vector4[] pixels,
        ushort[] mask,
        int width,
        int height,
        int radius,
        CancellationToken token)
    {
        public void Restore(int missing)
        {
            var order = new List<int>(missing);
            var inside = March(true, float.PositiveInfinity, order);
            var outside = March(false, radius + 2, null);
            for (var index = 0; index < inside.Length; index++)
                if (mask[index] == 0)
                    inside[index] = -Math.Min(outside[index], radius + 2);
            var distance = Smooth(inside);
            var known = new bool[mask.Length];
            for (var index = 0; index < known.Length; index++) known[index] = mask[index] == 0;
            foreach (var index in order)
            {
                token.ThrowIfCancellationRequested();
                var value = Estimate(index, distance, known);
                value.W = Math.Clamp(value.W, 0, 1);
                pixels[index] = value;
                known[index] = true;
            }
        }

        private float[] March(bool interior, float limit, List<int>? order)
        {
            var distance = new float[mask.Length];
            Array.Fill(distance, float.PositiveInfinity);
            var known = new bool[mask.Length];
            for (var index = 0; index < known.Length; index++)
            {
                known[index] = mask[index] == 0 == interior;
                if (known[index]) distance[index] = 0;
            }

            var front = new PriorityQueue<int, (float Distance, int Index)>();
            for (var index = 0; index < distance.Length; index++)
            {
                if ((index & 4095) == 0) token.ThrowIfCancellationRequested();
                if (known[index]) continue;
                var estimate = Arrival(index);
                if (!float.IsFinite(estimate)) continue;
                distance[index] = estimate;
                front.Enqueue(index, (estimate, index));
            }

            while (front.TryDequeue(out var index, out var priority))
            {
                token.ThrowIfCancellationRequested();
                if (known[index] || priority.Distance > distance[index]) continue;
                if (priority.Distance > limit) break;
                known[index] = true;
                order?.Add(index);
                var x = index % width;
                var y = index / width;
                if (x > 0) Visit(index - 1);
                if (x + 1 < width) Visit(index + 1);
                if (y > 0) Visit(index - width);
                if (y + 1 < height) Visit(index + width);
            }

            return distance;

            void Visit(int index)
            {
                if (known[index]) return;
                var estimate = Arrival(index);
                if (estimate >= distance[index]) return;
                distance[index] = estimate;
                front.Enqueue(index, (estimate, index));
            }

            float Arrival(int index)
            {
                var x = index % width;
                var y = index / width;
                var horizontal = Math.Min(x > 0 && known[index - 1] ? distance[index - 1] : float.PositiveInfinity,
                    x + 1 < width && known[index + 1] ? distance[index + 1] : float.PositiveInfinity);
                var vertical = Math.Min(
                    y > 0 && known[index - width] ? distance[index - width] : float.PositiveInfinity,
                    y + 1 < height && known[index + width] ? distance[index + width] : float.PositiveInfinity);
                if (!float.IsFinite(horizontal)) return vertical + 1;
                if (!float.IsFinite(vertical)) return horizontal + 1;
                var difference = Math.Abs(horizontal - vertical);
                return difference >= 1
                    ? Math.Min(horizontal, vertical) + 1
                    : (horizontal + vertical + MathF.Sqrt(2 - difference * difference)) / 2;
            }
        }

        private float[] Smooth(float[] distance)
        {
            var smoothed = new float[distance.Length];
            for (var y = 0; y < height; y++)
            {
                token.ThrowIfCancellationRequested();
                for (var x = 0; x < width; x++)
                {
                    var sum = 0f;
                    for (var dy = -1; dy <= 1; dy++)
                    for (var dx = -1; dx <= 1; dx++)
                        sum +=
                            distance[Math.Clamp(y + dy, 0, height - 1) * width + Math.Clamp(x + dx, 0, width - 1)] *
                            (dx == 0 ? 2 : 1) * (dy == 0 ? 2 : 1);
                    smoothed[y * width + x] = sum / 16;
                }
            }

            return smoothed;
        }

        private Vector4 Estimate(int index, float[] distance, bool[] known)
        {
            var x = index % width;
            var y = index / width;
            var normal = new Vector2(
                distance[y * width + Math.Min(x + 1, width - 1)] - distance[y * width + Math.Max(x - 1, 0)],
                distance[Math.Min(y + 1, height - 1) * width + x] - distance[Math.Max(y - 1, 0) * width + x]);
            if (normal.LengthSquared() > 1e-10f) normal = Vector2.Normalize(normal);
            var sum = Vector4.Zero;
            var weights = 0f;
            for (var dy = -radius; dy <= radius; dy++)
            for (var dx = -radius; dx <= radius; dx++)
            {
                var lengthSquared = dx * dx + dy * dy;
                if (lengthSquared == 0 || lengthSquared > radius * radius) continue;
                var px = x + dx;
                var py = y + dy;
                if (px < 0 || px >= width || py < 0 || py >= height) continue;
                var neighbor = py * width + px;
                if (!known[neighbor]) continue;
                var direction = Math.Abs(Vector2.Dot(new(dx, dy), normal)) / MathF.Sqrt(lengthSquared);
                var weight = Math.Max(direction, 1e-5f) /
                             (lengthSquared * (1 + Math.Abs(distance[index] - distance[neighbor])));
                var horizontal = Gradient(neighbor, px > 0 ? neighbor - 1 : -1,
                    px + 1 < width ? neighbor + 1 : -1, known);
                var vertical = Gradient(neighbor, py > 0 ? neighbor - width : -1,
                    py + 1 < height ? neighbor + width : -1, known);
                sum += (pixels[neighbor] - horizontal * dx - vertical * dy) * weight;
                weights += weight;
            }

            if (weights == 0) throw new InvalidOperationException("The inpainting area has no usable boundary pixels.");
            return sum / weights;
        }

        private Vector4 Gradient(int index, int before, int after, bool[] known)
        {
            var left = before >= 0 && known[before];
            var right = after >= 0 && known[after];
            if (left && right)
            {
                var previous = pixels[index] - pixels[before];
                var next = pixels[after] - pixels[index];
                return new(Limited(previous.X, next.X), Limited(previous.Y, next.Y),
                    Limited(previous.Z, next.Z), Limited(previous.W, next.W));
            }

            if (right) return pixels[after] - pixels[index];
            return left ? pixels[index] - pixels[before] : Vector4.Zero;
        }

        private static float Limited(float before, float after)
        {
            return before * after <= 0 ? 0 : MathF.CopySign(Math.Min(Math.Abs(before), Math.Abs(after)), before);
        }
    }
}
