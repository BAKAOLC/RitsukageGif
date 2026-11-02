namespace RitsukageGif.Desktop
{
    internal sealed class CaptureHistoryCountingStream : Stream
    {
        internal long Count { get; private set; }
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => Count;

        public override long Position
        {
            get => Count;
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
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

        public override void Write(byte[] buffer, int offset, int count)
        {
            Count += count;
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            Count += buffer.Length;
        }

        internal void Reset()
        {
            Count = 0;
        }
    }
}
