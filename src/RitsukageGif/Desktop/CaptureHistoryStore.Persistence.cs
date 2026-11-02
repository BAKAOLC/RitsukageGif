namespace RitsukageGif.Desktop
{
    internal sealed partial class CaptureHistoryStore
    {
        private readonly SemaphoreSlim _persistenceConfiguration = new(1);
        private long _clearRevision;
        private Task? _disposeWork;
        private bool _disposeRequested;
        private CaptureHistoryPersistence? _persistence;
        private bool _persistenceEnabled;

        public ValueTask DisposeAsync()
        {
            lock (_gate) return new(_disposeWork ??= DisposePersistenceAsync());
        }

        internal async Task ConfigurePersistenceAsync(string directory, bool enabled,
            CancellationToken token = default)
        {
            await _persistenceConfiguration.WaitAsync(token).ConfigureAwait(false);
            try
            {
                ObjectDisposedException.ThrowIf(_disposeRequested, this);
                CaptureHistoryPersistence? persistence;
                CaptureHistorySnapshot limits;
                long clearRevision;
                long revision;
                lock (_gate)
                {
                    persistence = _persistence;
                    limits = SnapshotLocked(enabled);
                    clearRevision = _clearRevision;
                    revision = _revision;
                }

                if (persistence is null)
                {
                    persistence = new(directory);
                    try
                    {
                        var restored = enabled
                            ? await persistence.LoadAsync(limits, token).ConfigureAwait(false)
                            : null;
                        lock (_gate)
                        {
                            if (restored is not null && clearRevision == _clearRevision)
                            {
                                var imageIds = _images.Select(entry => entry.Id).ToHashSet();
                                _images.AddRange(restored.Images.Where(entry => imageIds.Add(entry.Id)));
                                var regionIds = _regions.Select(entry => entry.Id).ToHashSet();
                                _regions.AddRange(restored.Regions.Where(entry => regionIds.Add(entry.Id)));
                                _imageBytes = _images.Sum(entry =>
                                    (long)entry.Png.Length + (entry.Hdr?.Pixels.Length ?? 0));
                                Trim();
                            }

                            _persistence = persistence;
                            _persistenceEnabled = enabled;
                            if (!persistence.DeferInitialWrite || revision != _revision) QueuePersistenceLocked(true);
                        }
                    }
                    catch
                    {
                        await persistence.DisposeAsync().ConfigureAwait(false);
                        throw;
                    }
                }
                else
                {
                    lock (_gate)
                    {
                        _persistenceEnabled = enabled;
                        QueuePersistenceLocked(true);
                    }
                }

                await persistence.FlushAsync(token).ConfigureAwait(false);
            }
            finally
            {
                _persistenceConfiguration.Release();
            }
        }

        private async Task FlushAsync(CancellationToken token = default)
        {
            await _persistenceConfiguration.WaitAsync(token).ConfigureAwait(false);
            try
            {
                CaptureHistoryPersistence? persistence;
                lock (_gate) persistence = _persistence;
                if (persistence is not null) await persistence.FlushAsync(token).ConfigureAwait(false);
            }
            finally
            {
                _persistenceConfiguration.Release();
            }
        }

        internal Task ClearAsync(bool screenshots, bool regions, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            if (!screenshots && !regions) return Task.CompletedTask;
            Clear(screenshots, regions);
            return FlushAsync(token);
        }

        private async Task DisposePersistenceAsync()
        {
            await _persistenceConfiguration.WaitAsync().ConfigureAwait(false);
            try
            {
                CaptureHistoryPersistence? persistence;
                lock (_gate)
                {
                    _disposeRequested = true;
                    persistence = _persistence;
                }

                if (persistence is not null) await persistence.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                _persistenceConfiguration.Release();
            }
        }

        private CaptureHistorySnapshot SnapshotLocked(bool enabled)
        {
            return new(enabled, _regionCapacity, _imageCapacity, _maximumBytes,
                enabled ? [.. _regions] : [], enabled ? [.. _images] : [], _clearRevision);
        }

        private void QueuePersistenceLocked(bool force = false)
        {
            if (_persistenceEnabled || force) _persistence?.Request(SnapshotLocked(_persistenceEnabled));
        }
    }
}
