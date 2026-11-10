using System.Buffers.Binary;

namespace RitsukageGif.Desktop
{
    internal readonly struct PinnedViewportPngSource : IPinnedViewportSource
    {
        private readonly PngRaster _source;
        private readonly int _channels;
        private readonly double _maximum;
        private readonly ReadOnlyMemory<byte> _palette;
        private readonly ReadOnlyMemory<byte> _transparency;

        internal PinnedViewportPngSource(PngRaster source)
        {
            _source = source;
            _channels = source.ColorType switch
            {
                0 or 3 => 1, 2 => 3, 4 => 2, 6 => 4, _ => throw new InvalidDataException(),
            };
            _maximum = (1 << source.Depth) - 1;
            _palette = source.Metadata.FirstOrDefault(static chunk => chunk.Type == "PLTE").Data;
            _transparency = source.Metadata.FirstOrDefault(static chunk => chunk.Type == "tRNS").Data;
        }

        public int Width => _source.Width;
        public int Height => _source.Height;

        public PinnedViewportSample Read(int x, int y)
        {
            var offset = (y * Width + x) * _channels;
            var alpha = _source.ColorType switch
            {
                4 or 6 => Sample(offset + _channels - 1) / _maximum,
                3 => _source.Samples[offset] < _transparency.Length
                    ? _transparency.Span[_source.Samples[offset]] / 255d
                    : 1,
                0 when _transparency.Length == 2 => Sample(offset) ==
                                                    BinaryPrimitives.ReadUInt16BigEndian(_transparency.Span)
                    ? 0
                    : 1,
                2 when _transparency.Length == 6 &&
                       Sample(offset) == BinaryPrimitives.ReadUInt16BigEndian(_transparency.Span) &&
                       Sample(offset + 1) == BinaryPrimitives.ReadUInt16BigEndian(_transparency.Span[2..]) &&
                       Sample(offset + 2) == BinaryPrimitives.ReadUInt16BigEndian(_transparency.Span[4..]) => 0,
                _ => 1,
            };
            if (alpha == 0) return default;
            if (_source.ColorType == 3)
            {
                var index = _source.Samples[offset] * 3;
                if (index + 2 >= _palette.Length) throw new InvalidDataException();
                return new(_palette.Span[index] * alpha / 255, _palette.Span[index + 1] * alpha / 255,
                    _palette.Span[index + 2] * alpha / 255, alpha);
            }

            var red = Sample(offset) * alpha / _maximum;
            return _source.ColorType is 0 or 4
                ? new(red, red, red, alpha)
                : new(red, Sample(offset + 1) * alpha / _maximum, Sample(offset + 2) * alpha / _maximum, alpha);
        }

        private int Sample(int component)
        {
            return _source.Depth == 16
                ? BinaryPrimitives.ReadUInt16BigEndian(_source.Samples.AsSpan(component * 2))
                : _source.Samples[component];
        }
    }
}
