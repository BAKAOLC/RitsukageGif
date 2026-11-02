namespace RitsukageGif.Desktop
{
    internal sealed class PinnedRichResourceAsset
    {
        private readonly PinnedRichResourceStore _owner;
        private string? _file;
        private byte[]? _memory;

        internal PinnedRichResourceAsset(PinnedRichResourceStore owner, byte[] memory, int length)
        {
            _owner = owner;
            _memory = memory;
            Length = length;
        }

        internal PinnedRichResourceAsset(PinnedRichResourceStore owner, string file, long length)
        {
            _owner = owner;
            _file = file;
            Length = length;
        }

        private long Length { get; }

        internal ValueTask<ReadOnlyMemory<byte>> ReadAsync(CancellationToken token)
        {
            return _owner.ReadAsync(this, token);
        }

        internal async ValueTask<ReadOnlyMemory<byte>> ReadStoredAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (_memory is { } memory) return memory.AsMemory(0, (int)Length);
            return await File.ReadAllBytesAsync(_file!, token).ConfigureAwait(false);
        }

        internal void ReleaseSource()
        {
            _memory = null;
            _file = null;
        }
    }
}
