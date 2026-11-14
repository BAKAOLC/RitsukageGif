using System.Buffers;
using Microsoft.Win32.SafeHandles;

namespace RitsukageGif.Core
{
    public sealed partial class ScrollCaptureSession
    {
        private readonly SortedList<long, long> _freeDiskRanges = [];
        private SafeFileHandle? _disk;
        private long _diskEnd;
        private (DiskStrip Strip, int Offset)? _diskHdrRowCache;
        private (DiskStrip Strip, int Offset)? _diskNextRowCache;
        private byte[]? _diskReadHdrRow;
        private byte[]? _diskReadNextRow;
        private byte[]? _diskReadRow;
        private (DiskStrip Strip, int Offset)? _diskRowCache;
        private bool _disposed;
        private bool _retainsCroppedDiskRange;

        // The normal working span includes the replaced boundary strip while the latest state is undoable.
        private long DiskWorkingLimit => DiskHeadroom(1);

        // A crop can retain the old span for undo and still needs room to accept its next frame.
        private long DiskAddressLimit => DiskHeadroom(2);
        private int DiskOrientations => _options.Axis == ScrollCaptureAxis.Vertical ? 1 : 2;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _disk?.Dispose();
            _disk = null;
        }

        private long DiskHeadroom(int viewports)
        {
            return _options.MaximumDiskBytes + Math.Min(long.MaxValue - _options.MaximumDiskBytes,
                (long)_axisLength * _storageRowBytes * DiskOrientations * viewports);
        }

        private bool HasFreeDiskRange(long length)
        {
            for (var i = 0; i < _freeDiskRanges.Count; i++)
                if (_freeDiskRanges.GetValueAtIndex(i) >= length)
                    return true;
            return false;
        }

        private bool TryAllocateDisk(int length, out long position)
        {
            for (var index = 0; index < _freeDiskRanges.Count; index++)
            {
                if (_freeDiskRanges.GetValueAtIndex(index) < length) continue;
                position = _freeDiskRanges.GetKeyAtIndex(index);
                var remaining = _freeDiskRanges.GetValueAtIndex(index) - length;
                _freeDiskRanges.RemoveAt(index);
                if (remaining != 0) _freeDiskRanges.Add(position + length, remaining);
                return true;
            }

            position = _diskEnd;
            if (length > DiskAddressLimit - position) return false;
            _diskEnd += length;
            return true;
        }

        private void FreeDisk(DiskStrip disk)
        {
            var position = disk.Position;
            long length = disk.FileLength;
            var low = 0;
            var high = _freeDiskRanges.Count;
            while (low < high)
            {
                var middle = low + (high - low) / 2;
                if (_freeDiskRanges.GetKeyAtIndex(middle) < position) low = middle + 1;
                else high = middle;
            }

            if (low < _freeDiskRanges.Count && _freeDiskRanges.GetKeyAtIndex(low) == position + length)
            {
                length += _freeDiskRanges.GetValueAtIndex(low);
                _freeDiskRanges.RemoveAt(low);
            }

            if (low > 0)
            {
                var previous = _freeDiskRanges.GetKeyAtIndex(low - 1);
                var previousLength = _freeDiskRanges.GetValueAtIndex(low - 1);
                if (previous + previousLength == position)
                {
                    position = previous;
                    length += previousLength;
                    _freeDiskRanges.RemoveAt(low - 1);
                }
            }

            if (position + length == _diskEnd) _diskEnd = position;
            else _freeDiskRanges.Add(position, length);
            if (_diskRowCache?.Strip == disk) _diskRowCache = null;
            if (_diskNextRowCache?.Strip == disk) _diskNextRowCache = null;
            if (_diskHdrRowCache?.Strip == disk) _diskHdrRowCache = null;
        }

        private SafeFileHandle DiskHandle()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_disk is not null) return _disk;
            var directory = Path.Combine(Path.GetTempPath(), "RitsukageGif");
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".scroll");
            var options = FileOptions.RandomAccess;
            if (OperatingSystem.IsWindows()) options |= FileOptions.DeleteOnClose;
            var handle = File.OpenHandle(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read, options);
            if (!OperatingSystem.IsWindows())
                try
                {
                    File.Delete(path);
                }
                catch
                {
                    handle.Dispose();
                    throw;
                }

            return _disk = handle;
        }

        private Strip StoreDiskStrip(int begin, int end, ReadOnlySpan<byte> sdr, ReadOnlySpan<byte> hdr,
            IReadOnlyList<FrameColorRegion>? regions, CancellationToken token)
        {
            var logicalLength = checked(sdr.Length + hdr.Length);
            var fileLength = checked(logicalLength * DiskOrientations);
            if (!TryAllocateDisk(fileLength, out var position))
                throw new IOException("Scrolling capture spool limit reached.");
            var disk = new DiskStrip(position, sdr.Length, hdr.Length, fileLength);
            try
            {
                var handle = DiskHandle();
                RandomAccess.Write(handle, sdr, position);
                if (!hdr.IsEmpty) RandomAccess.Write(handle, hdr, position + sdr.Length);
                if (_options.Axis == ScrollCaptureAxis.Horizontal)
                {
                    var transpose = ArrayPool<byte>.Shared.Rent(Math.Max(sdr.Length, hdr.Length));
                    try
                    {
                        WriteTransposed(sdr, 4, end - begin, transpose, position + logicalLength, token);
                        if (!hdr.IsEmpty)
                            WriteTransposed(hdr, 8, end - begin, transpose, position + logicalLength + sdr.Length,
                                token);
                    }
                    finally
                    {
                        ArrayPool<byte>.Shared.Return(transpose);
                    }
                }

                for (var i = 0; i < end - begin; i++)
                {
                    token.ThrowIfCancellationRequested();
                    var row = sdr.Slice(i * _rowBytes, _rowBytes);
                    if (SparseRowSignature(row) is { } signature) disk.Signatures.Add((i, signature));
                    var edge = SparseEdgeBytes;
                    if (edge > 0 && SparseRowSignature(row[edge..^edge]) is { } inner)
                        disk.InnerSignatures.Add((i, inner));
                }

                token.ThrowIfCancellationRequested();
                return new(begin, end, null, HdrRegions: regions, Disk: disk);
            }
            catch
            {
                FreeDisk(disk);
                throw;
            }
        }

        private void WriteTransposed(ReadOnlySpan<byte> input, int pixelBytes, int width, byte[] buffer,
            long position, CancellationToken token)
        {
            for (var y = 0; y < _crossLength; y++)
            {
                token.ThrowIfCancellationRequested();
                for (var x = 0; x < width; x++)
                    input.Slice((x * _crossLength + y) * pixelBytes, pixelBytes)
                        .CopyTo(buffer.AsSpan((y * width + x) * pixelBytes, pixelBytes));
            }

            RandomAccess.Write(DiskHandle(), buffer.AsSpan(0, input.Length), position);
        }

        private void ReadDisk(Span<byte> target, long position)
        {
            var handle = DiskHandle();
            while (!target.IsEmpty)
            {
                var count = RandomAccess.Read(handle, target, position);
                if (count == 0) throw new EndOfStreamException("Scrolling capture spool is incomplete.");
                target = target[count..];
                position += count;
            }
        }

        private ReadOnlySpan<byte> ReadRow(Strip strip, int position, bool hdr, bool next = false)
        {
            var rowLength = hdr ? _rowBytes * 2 : _rowBytes;
            var offset = strip.DataOffset * (hdr ? 2 : 1) + (position - strip.Start) * rowLength;
            if (strip.Disk is not { } disk)
                return (hdr ? strip.HdrPixels : strip.Pixels).AsSpan(offset, rowLength);
            if (hdr)
                return ReadDiskRow(disk, offset + disk.SdrLength, rowLength, ref _diskReadHdrRow, ref _diskHdrRowCache);
            return next
                ? ReadDiskRow(disk, offset, rowLength, ref _diskReadNextRow, ref _diskNextRowCache)
                : ReadDiskRow(disk, offset, rowLength, ref _diskReadRow, ref _diskRowCache);
        }

        private ReadOnlySpan<byte> ReadDiskRow(DiskStrip disk, int offset, int length, ref byte[]? buffer,
            ref (DiskStrip Strip, int Offset)? cache)
        {
            buffer ??= new byte[length];
            if (cache != (disk, offset))
            {
                ReadDisk(buffer, disk.Position + offset);
                cache = (disk, offset);
            }

            return buffer;
        }

        private void ReleaseStrip(Strip? strip)
        {
            if (strip?.Disk is { } disk) FreeDisk(disk);
        }

        private void ReleaseDiscarded(List<Strip>? old, List<Strip> current, List<Strip>? undo)
        {
            if (!_options.UseDiskStorage || old is null) return;
            var retained = new HashSet<DiskStrip>();
            foreach (var strip in current)
                if (strip.Disk is { } disk)
                    retained.Add(disk);
            if (undo is not null)
                foreach (var strip in undo)
                    if (strip.Disk is { } disk)
                        retained.Add(disk);
            foreach (var strip in old)
                if (strip.Disk is { } disk && retained.Add(disk))
                    FreeDisk(disk);
        }

        private sealed class DiskStrip(long position, int sdrLength, int hdrLength, int fileLength)
        {
            internal long Position { get; } = position;
            internal int SdrLength { get; } = sdrLength;
            internal int FileLength { get; } = fileLength;
            internal int LogicalLength { get; } = sdrLength + hdrLength;
            internal List<(int Row, ulong Hash)> Signatures { get; } = [];
            internal List<(int Row, ulong Hash)> InnerSignatures { get; } = [];
        }
    }
}
