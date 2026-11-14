using System.Buffers.Binary;
using System.Numerics;

namespace RitsukageGif.Core
{
    public static partial class CaptureDecorationProcessor
    {
        public static DecorationGeometry MeasureStreaming(int width, int height, CaptureDecoration decoration)
        {
            return MeasureGeometry(width, height, decoration);
        }

        public static StreamingRows CreateStreamingRows(int width, int height, CaptureDecoration decoration,
            StaticPngRowReader source, bool hdr, float whiteScale = 1)
        {
            return new(width, height, decoration, source, hdr, whiteScale);
        }

        public sealed partial class StreamingRows : IDisposable
        {
            private static readonly float[] LinearSamples =
            [
                .. Enumerable.Range(0, 256).Select(value =>
                {
                    var encoded = value / 255f;
                    return encoded <= .04045f ? encoded / 12.92f : MathF.Pow((encoded + .055f) / 1.055f, 2.4f);
                }),
            ];

            private readonly int _border;
            private readonly double _corner;
            private readonly CaptureDecoration _decoration;
            private readonly bool _hdr;
            private readonly byte[] _outputRow;
            private readonly Vector4 _paint;
            private readonly ShadowRows? _shadow;
            private readonly StaticPngRowReader _source;
            private readonly byte[] _sourceRow;

            private readonly int _width, _height, _bytesPerPixel;
            private bool _disposed;
            private int _rendered = -1;

            internal StreamingRows(int width, int height, CaptureDecoration decoration, StaticPngRowReader source,
                bool hdr, float whiteScale)
            {
                ArgumentNullException.ThrowIfNull(source);
                Geometry = MeasureStreaming(width, height, decoration);
                _width = width;
                _height = height;
                _source = source;
                _hdr = hdr;
                _decoration = decoration;
                _bytesPerPixel = hdr ? 8 : 4;
                _sourceRow = new byte[checked(width * _bytesPerPixel)];
                _outputRow = new byte[checked(Geometry.Width * _bytesPerPixel)];
                _corner = Math.Min(decoration.CornerRadius, Math.Min(width, height) / 2d);
                _border = decoration.Kind == CaptureDecorationKind.Border ? decoration.Strength : 0;
                _paint = Paint(decoration.Color, hdr ? whiteScale : 1);
                if (decoration is { Kind: CaptureDecorationKind.Shadow, Strength: > 0 } && _paint.W > 0)
                    _shadow = new(this, decoration.Strength);
            }

            public DecorationGeometry Geometry { get; }

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                _shadow?.Dispose();
            }

            public void ReadRow(int y, Span<byte> pixels, CancellationToken token)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                ArgumentOutOfRangeException.ThrowIfNegative(y);
                ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(y, Geometry.Height);
                ArgumentOutOfRangeException.ThrowIfLessThan(y, _rendered);
                token.ThrowIfCancellationRequested();
                if (y != _rendered)
                {
                    if (_decoration.IsEmpty) _source(y, _outputRow, token);
                    else Render(y, token);
                    _rendered = y;
                }

                _outputRow.CopyTo(pixels);
            }

            private void Render(int y, CancellationToken token)
            {
                var left = Geometry.ContentOffset.X;
                var sourceY = y - Geometry.ContentOffset.Y;
                var containsRow = (uint)sourceY < (uint)_height;
                ReadOnlySpan<byte> source = default;
                ReadOnlySpan<float> shadow = default;
                // ReSharper disable once InlineTemporaryVariable
                if (_shadow is { } filter)
                {
                    shadow = filter.Read(y, token);
                    if (containsRow) source = filter.SourceRow(sourceY);
                }
                else if (containsRow)
                {
                    _source(sourceY, _sourceRow, token);
                    source = _sourceRow;
                }

                for (var x = 0; x < Geometry.Width; x++)
                {
                    if ((x & 4095) == 0) token.ThrowIfCancellationRequested();
                    var sourceX = x - left;
                    var containsPixel = containsRow && (uint)sourceX < (uint)_width;
                    var coverage = Coverage(sourceX + .5, sourceY + .5, 0, 0, _width, _height, _corner);
                    var output = _outputRow.AsSpan(x * _bytesPerPixel, _bytesPerPixel);
                    var input = containsPixel ? source.Slice(sourceX * _bytesPerPixel, _bytesPerPixel) : default;
                    if (containsPixel && coverage >= 1 && ReadAlpha(input) >= 1)
                    {
                        input.CopyTo(output);
                        continue;
                    }

                    var foreground = containsPixel ? ReadColor(input) * (float)coverage : Vector4.Zero;
                    if (_border > 0)
                    {
                        var ring = Math.Max(0, Coverage(sourceX + .5, sourceY + .5, -_border, -_border,
                            _width + _border, _height + _border, _corner + _border) - coverage);
                        foreground += _paint with { W = 1 } * ((float)ring * _paint.W);
                    }

                    if (!shadow.IsEmpty)
                        foreground += _paint with { W = 1 } * (shadow[x] * _paint.W * (1 - foreground.W));
                    WriteColor(output, foreground);
                }
            }

            private float ReadAlpha(ReadOnlySpan<byte> input)
            {
                return _hdr
                    ? Math.Clamp((float)BinaryPrimitives.ReadHalfLittleEndian(input[6..]), 0, 1)
                    : input[3] / 255f;
            }

            private Vector4 ReadColor(ReadOnlySpan<byte> input)
            {
                var alpha = ReadAlpha(input);
                if (_hdr)
                    return new((float)BinaryPrimitives.ReadHalfLittleEndian(input),
                        (float)BinaryPrimitives.ReadHalfLittleEndian(input[2..]),
                        (float)BinaryPrimitives.ReadHalfLittleEndian(input[4..]), alpha);

                return new(LinearSamples[input[2]] * alpha, LinearSamples[input[1]] * alpha,
                    LinearSamples[input[0]] * alpha, alpha);
            }

            private void WriteColor(Span<byte> output, Vector4 color)
            {
                if (_hdr)
                {
                    BinaryPrimitives.WriteHalfLittleEndian(output, (Half)color.X);
                    BinaryPrimitives.WriteHalfLittleEndian(output[2..], (Half)color.Y);
                    BinaryPrimitives.WriteHalfLittleEndian(output[4..], (Half)color.Z);
                    BinaryPrimitives.WriteHalfLittleEndian(output[6..], (Half)color.W);
                }
                else if (color.W > 0)
                {
                    output[0] = Encode(color.Z / color.W);
                    output[1] = Encode(color.Y / color.W);
                    output[2] = Encode(color.X / color.W);
                    output[3] = ToByte(color.W);
                }
                else
                {
                    output.Clear();
                }
            }

            private static byte Encode(float linear)
            {
                return ToByte(linear <= .0031308f ? linear * 12.92f : 1.055f * MathF.Pow(linear, 1 / 2.4f) - .055f);
            }

            private static byte ToByte(float value)
            {
                return (byte)Math.Clamp(MathF.Round(value * 255), 0, 255);
            }
        }
    }
}
