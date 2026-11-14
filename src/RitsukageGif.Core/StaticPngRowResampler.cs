using System.Buffers;
using System.Buffers.Binary;
using System.Numerics;
using System.Runtime.InteropServices;

namespace RitsukageGif.Core
{
    public sealed class StaticPngRowResampler : IDisposable
    {
        private readonly int _outputHeight;
        private readonly int _outputWidth;
        private readonly int _pixelBytes;
        private readonly StaticPngRowReader _source;
        private readonly int _sourceHeight;
        private readonly int _sourceWidth;
        private int _firstRow = -1;
        private Vector4[]? _rows;
        private int _secondRow = -1;
        private byte[]? _sourceRow;

        public StaticPngRowResampler(int sourceWidth, int sourceHeight, int outputWidth, int outputHeight,
            StaticPngRowReader source, bool hdr)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sourceWidth);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sourceHeight);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(outputWidth);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(outputHeight);
            ArgumentNullException.ThrowIfNull(source);
            _sourceWidth = sourceWidth;
            _sourceHeight = sourceHeight;
            _outputHeight = outputHeight;
            _outputWidth = outputWidth;
            _source = source;
            _pixelBytes = hdr ? 8 : 4;
        }

        public void Dispose()
        {
            if (_sourceRow is { } input)
            {
                ArrayPool<byte>.Shared.Return(input);
                _sourceRow = null;
            }

            if (_rows is { } rows)
            {
                ArrayPool<Vector4>.Shared.Return(rows);
                _rows = null;
            }

            _firstRow = _secondRow = -1;
        }

        public void ReadRow(int row, Span<byte> pixels, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            ArgumentOutOfRangeException.ThrowIfNegative(row);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(row, _outputHeight);
            if (pixels.Length != checked(_outputWidth * _pixelBytes))
                throw new ArgumentException(null, nameof(pixels));
            if (_sourceWidth == _outputWidth && _sourceHeight == _outputHeight)
            {
                _source(row, pixels, token);
                return;
            }

            var inputLength = checked(_sourceWidth * _pixelBytes);
            _sourceRow ??= ArrayPool<byte>.Shared.Rent(inputLength);
            _rows ??= ArrayPool<Vector4>.Shared.Rent(checked(_outputWidth * 4));
            var input = _sourceRow.AsSpan(0, inputLength);
            var sums = _rows.AsSpan(_outputWidth * 2, _outputWidth);
            var errors = _rows.AsSpan(_outputWidth * 3, _outputWidth);
            sums.Clear();
            errors.Clear();

            var (top, bottom, unit) = PixelBounds(row, _sourceHeight, _outputHeight);
            var last = (int)((bottom - 1) / unit);
            for (var y = (int)(top / unit); y <= last; y++)
            {
                token.ThrowIfCancellationRequested();
                var scratch = GetOrRead(y, input, token);
                var weight = Weight(y, top, bottom, unit);
                for (var x = 0; x < sums.Length; x++)
                    Add(ref sums[x], ref errors[x], scratch[x] * weight);
            }

            WriteRow(sums, pixels);
        }

        private ReadOnlySpan<Vector4> GetOrRead(int y, Span<byte> input, CancellationToken token)
        {
            var first = y == _firstRow || (y != _secondRow && _firstRow <= _secondRow);
            var scratch = _rows.AsSpan(first ? 0 : _outputWidth, _outputWidth);
            ref var cached = ref first ? ref _firstRow : ref _secondRow;
            if (cached == y) return scratch;
            cached = -1;
            _source(y, input, token);
            ResampleRow(input, scratch, token);
            cached = y;
            return scratch;
        }

        private void ResampleRow(ReadOnlySpan<byte> input, Span<Vector4> target, CancellationToken token)
        {
            for (var x = 0; x < target.Length; x++)
            {
                if ((x & 4095) == 0) token.ThrowIfCancellationRequested();
                var (left, right, unit) = PixelBounds(x, _sourceWidth, _outputWidth);
                var last = (int)((right - 1) / unit);
                var sum = Vector4.Zero;
                var error = Vector4.Zero;
                for (var sx = (int)(left / unit); sx <= last; sx++)
                {
                    if ((sx & 4095) == 0) token.ThrowIfCancellationRequested();
                    var pixel = input[(sx * _pixelBytes)..];
                    var color = _pixelBytes == 8
                        ? new Vector4(ReadHalf(pixel), ReadHalf(pixel[2..]), ReadHalf(pixel[4..]), ReadHalf(pixel[6..]))
                        : new Vector4(pixel[0] * pixel[3], pixel[1] * pixel[3], pixel[2] * pixel[3], pixel[3]);
                    Add(ref sum, ref error, color * Weight(sx, left, right, unit));
                }

                target[x] = sum;
            }
        }

        private void WriteRow(ReadOnlySpan<Vector4> sums, Span<byte> pixels)
        {
            if (_pixelBytes == 8)
            {
                var rgba = MemoryMarshal.Cast<Vector4, float>(sums);
                for (var i = 0; i < rgba.Length; i++)
                    BinaryPrimitives.WriteHalfLittleEndian(pixels[(i * 2)..], (Half)rgba[i]);
                return;
            }

            for (var x = 0; x < sums.Length; x++)
            {
                var color = sums[x];
                if (color.W > 0)
                {
                    var inverse = 1 / color.W;
                    color.X *= inverse;
                    color.Y *= inverse;
                    color.Z *= inverse;
                }

                var offset = x * 4;
                pixels[offset] = ToByte(color.X);
                pixels[offset + 1] = ToByte(color.Y);
                pixels[offset + 2] = ToByte(color.Z);
                pixels[offset + 3] = ToByte(color.W);
            }
        }

        private static (long From, long To, long Unit) PixelBounds(int coordinate, int source, int target)
        {
            // Normalize exact intervals for both area reduction and center-aligned interpolation.
            var center = (coordinate * 2L + 1) * source;
            var radius = Math.Max(source, target);
            var unit = target * 2L;
            return (Math.Max(0, center - radius), Math.Min(source * unit, center + radius), unit);
        }

        private static float Weight(int pixel, long from, long to, long unit)
        {
            return (float)(Math.Min((pixel + 1L) * unit, to) - Math.Max(pixel * unit, from)) / (to - from);
        }

        private static void Add(ref Vector4 sum, ref Vector4 error, Vector4 value)
        {
            // Compensate for rounding when millions of samples contribute to an output pixel.
            var corrected = value - error;
            var next = sum + corrected;
            error = next - sum - corrected;
            sum = next;
        }

        private static float ReadHalf(ReadOnlySpan<byte> bytes)
        {
            var value = (float)BinaryPrimitives.ReadHalfLittleEndian(bytes);
            return float.IsFinite(value) ? value : 0;
        }

        private static byte ToByte(float value)
        {
            return (byte)Math.Clamp(MathF.Round(value), 0, 255);
        }
    }
}
