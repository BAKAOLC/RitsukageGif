namespace RitsukageGif.Core
{
    public sealed partial class ScrollCaptureSession
    {
        private readonly long _workspaceBytes;
        private long _storedPayloadBytes;
        private long _undoExtraPayloadBytes;

        private bool CanAllocateStrip(long inputBytes, long payload, int nextLength)
        {
            var allocationPeak = _workspaceBytes + inputBytes + _storedPayloadBytes +
                                 _undoExtraPayloadBytes + payload;
            var exportPeak = _workspaceBytes + _storedPayloadBytes + payload +
                             (long)nextLength * _storageRowBytes;
            return Math.Max(allocationPeak, exportPeak) <= _options.MaximumBytes;
        }

        private static long PayloadBytes(IEnumerable<Strip> strips)
        {
            var total = 0L;
            foreach (var strip in strips) total += strip.Pixels.LongLength + (strip.HdrPixels?.LongLength ?? 0);
            return total;
        }
    }
}
