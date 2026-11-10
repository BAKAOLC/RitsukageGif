namespace RitsukageGif.Desktop
{
    internal sealed partial class CaptureHistoryStore : IAsyncDisposable
    {
        private readonly Lock _gate = new();
        private readonly List<CaptureImageHistoryEntry> _images = [];
        private readonly List<CaptureRegionHistoryEntry> _regions = [];
        private long _imageBytes;
        private long _revision;
        private int _imageCapacity;
        private long _maximumBytes;
        private int _regionCapacity;

        internal CaptureHistoryStore(int regionCapacity = 100, int imageCapacity = 100,
            long maximumBytes = 128L * 1024 * 1024)
        {
            Configure(regionCapacity, imageCapacity, maximumBytes);
        }

        internal void Configure(int regionCapacity, int imageCapacity, long maximumBytes)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(regionCapacity);
            ArgumentOutOfRangeException.ThrowIfNegative(imageCapacity);
            ArgumentOutOfRangeException.ThrowIfNegative(maximumBytes);
            lock (_gate)
            {
                regionCapacity = Math.Min(regionCapacity, 99999);
                imageCapacity = Math.Min(imageCapacity, 99999);
                maximumBytes = Math.Min(maximumBytes, 512L * 1024 * 1024);
                if (_regionCapacity == regionCapacity && _imageCapacity == imageCapacity &&
                    _maximumBytes == maximumBytes) return;
                _regionCapacity = regionCapacity;
                _imageCapacity = imageCapacity;
                _maximumBytes = maximumBytes;
                Trim();
                _revision++;
                QueuePersistenceLocked();
            }
        }

        internal void RecordRegion(SelectionResult selection)
        {
            if (selection.Region.IsEmpty) return;
            lock (_gate)
            {
                if (_regionCapacity == 0) return;
                _regions.RemoveAll(item => item.Selection.Region == selection.Region &&
                                           item.Selection.WindowSource == selection.WindowSource &&
                                           item.Selection.Target == selection.Target &&
                                           item.Selection.Shape == selection.Shape &&
                                           (item.Selection.Windows ?? []).SequenceEqual(selection.Windows ?? []));
                _regions.Insert(0, new(selection, DateTimeOffset.UtcNow));
                Trim();
                _revision++;
                QueuePersistenceLocked();
            }
        }

        // ReSharper disable once UnusedMethodReturnValue.Global
        internal bool RecordScreenshot(ReadOnlySpan<byte> png, PixelRect region, CaptureSourceKind source,
            CapturedFrame? hdr = null, double captureScaling = 1, CaptureDecoration? decoration = null,
            PixelPoint? outputOrigin = null)
        {
            decoration ??= CaptureDecoration.None;
            decoration.Validate();
            if (!double.IsFinite(captureScaling) || captureScaling is < 1 or > 16)
                throw new ArgumentOutOfRangeException(nameof(captureScaling));
            if (region.IsEmpty || png.IsEmpty) return false;
            lock (_gate)
            {
                var size = (long)png.Length + (hdr?.Pixels.Length ?? 0);
                if (_imageCapacity == 0 || size > _maximumBytes) return false;
                var owned = png.ToArray();
                if (hdr is not null)
                {
                    var dimensions = PngImageCodec.ReadDimensions(owned);
                    if (hdr.Width != dimensions.Width || hdr.Height != dimensions.Height)
                        throw new ArgumentException(null, nameof(hdr));
                }

                var ownedHdr = hdr is null
                    ? null
                    : hdr with
                    {
                        Pixels = hdr.Pixels.ToArray(), ColorRegions = Array.AsReadOnly([.. hdr.ColorRegions]),
                    };
                _images.Insert(0,
                    new(owned, region, source, DateTimeOffset.UtcNow, ownedHdr)
                        { CaptureScaling = captureScaling, Decoration = decoration, OutputOrigin = outputOrigin });
                _imageBytes += size;
                Trim();
                _revision++;
                QueuePersistenceLocked();
                return true;
            }
        }

        internal CaptureRegionHistoryEntry[] Regions()
        {
            lock (_gate) return [.. _regions];
        }

        internal CaptureImageHistoryEntry[] Images()
        {
            lock (_gate) return [.. _images];
        }

        private void Clear(bool screenshots, bool regions)
        {
            lock (_gate)
            {
                _clearRevision++;
                _revision++;
                if (regions) _regions.Clear();
                if (screenshots)
                {
                    _images.Clear();
                    _imageBytes = 0;
                }

                QueuePersistenceLocked(true);
            }
        }

        private void Trim()
        {
            if (_regions.Count > _regionCapacity)
                _regions.RemoveRange(_regionCapacity, _regions.Count - _regionCapacity);
            while (_images.Count > _imageCapacity || _imageBytes > _maximumBytes)
            {
                _imageBytes -= (long)_images[^1].Png.Length + (_images[^1].Hdr?.Pixels.Length ?? 0);
                _images.RemoveAt(_images.Count - 1);
            }
        }
    }
}
