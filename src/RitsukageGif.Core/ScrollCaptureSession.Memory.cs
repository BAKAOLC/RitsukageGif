namespace RitsukageGif.Core
{
    public sealed partial class ScrollCaptureSession
    {
        private readonly long _workspaceBytes;
        private long _storedPayloadBytes;
        private long _undoExtraPayloadBytes;

        private bool CanAllocateStrip(long inputBytes, long payload, int nextLength)
        {
            if (_options.UseDiskStorage)
            {
                var horizontal = _options.Axis == ScrollCaptureAxis.Horizontal;
                var fileLength = payload * DiskOrientations;
                var stripWorkingBytes = (_options.RetainHdr ? payload : 0) + (horizontal ? payload : 0);
                var diskEnd = _retainsCroppedDiskRange ? DiskAddressLimit : DiskWorkingLimit;
                return _workspaceBytes + inputBytes + stripWorkingBytes <= _options.MaximumBytes &&
                       (long)nextLength * _storageRowBytes * DiskOrientations <= _options.MaximumDiskBytes &&
                       (fileLength <= diskEnd - _diskEnd || HasFreeDiskRange(fileLength));
            }

            var allocationPeak = _workspaceBytes + inputBytes + _storedPayloadBytes +
                                 _undoExtraPayloadBytes + payload;
            var exportPeak = _workspaceBytes + _storedPayloadBytes + payload +
                             (long)nextLength * _storageRowBytes;
            return Math.Max(allocationPeak, exportPeak) <= _options.MaximumBytes;
        }

        private static long PayloadBytes(IEnumerable<Strip> strips)
        {
            var total = 0L;
            foreach (var strip in strips) total += strip.StorageBytes;
            return total;
        }
    }
}
