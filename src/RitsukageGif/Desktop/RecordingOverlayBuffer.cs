using System.Buffers;

namespace RitsukageGif.Desktop
{
    internal sealed class RecordingOverlayBuffer(PreviewFrame source) : IDisposable
    {
        private readonly PreviewFrame _source = source.Retain();
        private PreviewFrame? _result;

        internal PreviewFrame Current => _result ?? _source;

        public void Dispose()
        {
            _result?.Dispose();
            _result = null;
            _source.Dispose();
        }

        internal PreviewFrame Writable()
        {
            if (_result is not null) return _result;
            var length = checked(_source.Width * _source.Height * 4);
            var pixels = ArrayPool<byte>.Shared.Rent(length);
            byte[]? hdr = null;
            try
            {
                _source.Pixels.AsSpan(0, length).CopyTo(pixels);
                if (_source.HdrPixels is { } original)
                {
                    hdr = ArrayPool<byte>.Shared.Rent(checked(length * 2));
                    original.AsSpan(0, length * 2).CopyTo(hdr);
                }

                return _result = new(pixels, _source.Width, _source.Height, _source.Delay, hdr,
                    _source.HdrReferenceWhiteLevel, _source.HdrAlphaMode);
            }
            catch
            {
                ArrayPool<byte>.Shared.Return(pixels);
                if (hdr is not null) ArrayPool<byte>.Shared.Return(hdr);
                throw;
            }
        }

        internal PreviewFrame TakeResult()
        {
            if (_result is not { } result) return _source.Retain();
            _result = null;
            return result;
        }
    }
}
