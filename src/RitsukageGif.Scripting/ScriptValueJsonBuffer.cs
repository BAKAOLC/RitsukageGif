using System.Buffers;

namespace RitsukageGif.Scripting
{
    internal sealed class ScriptValueJsonBuffer(int maximum) : IBufferWriter<byte>
    {
        private readonly int _reservationLimit = checked(maximum * 6 + 4096);
        private byte[] _buffer = [];
        private int _length;
        internal ReadOnlyMemory<byte> Written => _buffer.AsMemory(0, _length);

        public void Advance(int count)
        {
            if (count < 0 || count > _buffer.Length - _length) throw new ArgumentOutOfRangeException(nameof(count));
            if (count > maximum - _length) throw ScriptValueJson.Limit();
            _length += count;
        }

        public Memory<byte> GetMemory(int sizeHint = 0)
        {
            sizeHint = Math.Max(1, sizeHint);
            if (sizeHint > _reservationLimit - _length) throw ScriptValueJson.Limit();
            if (_buffer.Length - _length < sizeHint)
                Array.Resize(ref _buffer, Math.Min(_reservationLimit,
                    Math.Max(_length + sizeHint, Math.Max(256, _buffer.Length * 2))));
            return _buffer.AsMemory(_length);
        }

        public Span<byte> GetSpan(int sizeHint = 0)
        {
            return GetMemory(sizeHint).Span;
        }
    }
}
