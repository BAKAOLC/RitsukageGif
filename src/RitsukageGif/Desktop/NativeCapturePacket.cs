using System.Buffers;

namespace RitsukageGif.Desktop
{
    internal sealed class NativeCapturePacket(
        NativeCapturePacketKind kind,
        ulong sequence,
        NativeCaptureMetadata metadata,
        byte[]? buffer,
        int sdrLength,
        int hdrLength,
        int cursorLength) : IDisposable
    {
        private byte[]? _buffer = buffer;

        public NativeCapturePacketKind Kind { get; } = kind;
        public ulong Sequence { get; } = sequence;
        public NativeCaptureMetadata Metadata { get; } = metadata;
        public ReadOnlyMemory<byte> Sdr => Memory(0, sdrLength);
        public ReadOnlyMemory<byte> Hdr => Memory(sdrLength, hdrLength);
        public ReadOnlyMemory<byte> Cursor => Memory(sdrLength + hdrLength, cursorLength);

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _buffer, null) is { } data) ArrayPool<byte>.Shared.Return(data);
        }

        private ReadOnlyMemory<byte> Memory(int start, int length)
        {
            if (length == 0) return ReadOnlyMemory<byte>.Empty;
            return (_buffer ?? throw new ObjectDisposedException(nameof(NativeCapturePacket))).AsMemory(start, length);
        }
    }
}
