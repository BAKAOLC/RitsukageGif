using System.Buffers;

namespace RitsukageGif.Core
{
    internal sealed class PngDataChunkStream(StreamingPngWriter writer, bool first, CancellationToken token) : Stream
    {
        private byte[]? _buffer = ArrayPool<byte>.Shared.Rent(65536);
        private int _count;

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => _buffer is not null;
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
            ObjectDisposedException.ThrowIf(_buffer is null, this);
            while (!buffer.IsEmpty)
            {
                token.ThrowIfCancellationRequested();
                var amount = Math.Min(buffer.Length, _buffer.Length - _count);
                buffer[..amount].CopyTo(_buffer.AsSpan(_count));
                _count += amount;
                buffer = buffer[amount..];
                if (_count == _buffer.Length) Flush();
            }
        }

        public override void Flush()
        {
            ObjectDisposedException.ThrowIf(_buffer is null, this);
            if (_count == 0) return;
            writer.WriteData(first, _buffer.AsSpan(0, _count));
            _count = 0;
        }

        protected override void Dispose(bool disposing)
        {
            if (_buffer is null) return;
            try
            {
                if (disposing && !token.IsCancellationRequested) Flush();
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(_buffer);
                _buffer = null;
                base.Dispose(disposing);
            }
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
    }
}
