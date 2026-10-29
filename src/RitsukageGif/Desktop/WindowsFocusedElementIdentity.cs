namespace RitsukageGif.Desktop
{
    internal sealed class WindowsFocusedElementIdentity(uint processId, ReadOnlySpan<int> runtimeId)
    {
        private readonly int[] _runtimeId = [.. runtimeId];

        private uint ProcessId { get; } = processId;

        internal bool Matches(WindowsFocusedElementIdentity? other)
        {
            return other is not null && ProcessId == other.ProcessId &&
                   _runtimeId.AsSpan().SequenceEqual(other._runtimeId);
        }
    }
}
