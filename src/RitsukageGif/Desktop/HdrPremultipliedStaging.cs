using System.Buffers;
using System.Runtime.InteropServices;

namespace RitsukageGif.Desktop
{
    internal sealed class HdrPremultipliedStaging : IDisposable
    {
        private byte[]? _buffer;

        public void Dispose()
        {
            if (_buffer is null) return;
            ArrayPool<byte>.Shared.Return(_buffer);
            _buffer = null;
        }

        internal ReadOnlyMemory<byte> Convert(ReadOnlySpan<byte> source, CaptureAlphaMode alphaMode)
        {
            if (_buffer is null || _buffer.Length < source.Length)
            {
                var buffer = ArrayPool<byte>.Shared.Rent(source.Length);
                if (_buffer is not null) ArrayPool<byte>.Shared.Return(_buffer);
                _buffer = buffer;
            }

            var input = MemoryMarshal.Cast<byte, Half>(source);
            var output = MemoryMarshal.Cast<byte, Half>(_buffer.AsSpan(0, source.Length));
            for (var index = 0; index < input.Length; index += 4)
            {
                var alpha = alphaMode == CaptureAlphaMode.Opaque ? 1 : Math.Clamp((float)input[index + 3], 0, 1);
                output[index] = (Half)((float)input[index] * alpha);
                output[index + 1] = (Half)((float)input[index + 1] * alpha);
                output[index + 2] = (Half)((float)input[index + 2] * alpha);
                output[index + 3] = (Half)alpha;
            }

            return _buffer.AsMemory(0, source.Length);
        }
    }
}
