using System.Buffers.Binary;

namespace RitsukageGif.Core
{
    internal sealed class HdrPngDataStream(Stream output, CancellationToken token) : Stream
    {
        private readonly byte[] _buffer = new byte[65536];
        private int _count;
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            Write(buffer.AsSpan(offset, count));
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            while (!buffer.IsEmpty)
            {
                token.ThrowIfCancellationRequested();
                var count = Math.Min(buffer.Length, _buffer.Length - _count);
                buffer[..count].CopyTo(_buffer.AsSpan(_count));
                _count += count;
                buffer = buffer[count..];
                if (_count == _buffer.Length) Flush();
            }
        }

        public override void Flush()
        {
            if (_count == 0) return;
            token.ThrowIfCancellationRequested();
            WriteChunk(output, "IDAT"u8, _buffer.AsSpan(0, _count));
            _count = 0;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !token.IsCancellationRequested) Flush();
            base.Dispose(disposing);
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        internal static void WriteChunk(Stream stream, ReadOnlySpan<byte> type, ReadOnlySpan<byte> payload)
        {
            Span<byte> header = stackalloc byte[8];
            BinaryPrimitives.WriteInt32BigEndian(header, payload.Length);
            type.CopyTo(header[4..]);
            stream.Write(header);
            stream.Write(payload);
            Span<byte> crc = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(crc, ~PngCrc32.Update(PngCrc32.Update(uint.MaxValue, type), payload));
            stream.Write(crc);
        }
    }
}
