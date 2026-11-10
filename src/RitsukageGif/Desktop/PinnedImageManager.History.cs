namespace RitsukageGif.Desktop
{
    internal sealed partial class PinnedImageManager
    {
        private readonly List<PinnedClosedImage> _closedImages = [];
        private readonly CancellationTokenSource _historyCancellation = new();
        private readonly HashSet<Task> _historyOperations = [];
        private readonly SemaphoreSlim _historyRestoreGate = new(1);

        public bool CanRestoreClosed => _closedImages.Any(entry => entry.State.Group == Preferences.ActiveGroup) ||
                                        _closedDocuments.Any(entry =>
                                            entry.State.Window.Group == Preferences.ActiveGroup);

        private void RememberClosed(PinnedImageWindow window)
        {
            var png = window.CurrentPng;
            if (Preferences.HistoryLimit == 0) return;
            var snapshot = window.CaptureLiveSnapshot();
            var state = window.CaptureState();
            if (snapshot is not null) state.LiveSource = snapshot.Descriptor;
            var closedAt = DateTimeOffset.UtcNow.UtcTicks;
            PinnedClosedImage entry = snapshot is not null
                ? new PinnedClosedLiveImage(state, snapshot, closedAt)
                : new PinnedClosedStaticImage(state, png, window.ContentHash, window.OriginalContent,
                    window.StoredAnimationActive, closedAt);
            if (entry.EstimatedBytes > 128L * 1024 * 1024)
            {
                entry.Dispose();
                return;
            }

            _closedImages.Add(entry);
            TrimClosedHistory();
        }

        private void DropClosedImage(int index)
        {
            var entry = _closedImages[index];
            _closedImages.RemoveAt(index);
            entry.Dispose();
        }

        private void TrimClosedHistory()
        {
            while (_closedImages.Count > Preferences.HistoryLimit ||
                   _closedImages.Sum(entry => entry.EstimatedBytes) >
                   128L * 1024 * 1024)
                DropClosedImage(0);
            TrimMixedHistory();
        }

        private async Task<bool> RestoreLastClosedCoreAsync(CancellationToken token)
        {
            await _historyRestoreGate.WaitAsync(token);
            try
            {
                var documentIndex = -1;
                var imageIndex = -1;
                long documentTime = -1, imageTime = -1;
                for (var index = 0; index < _closedDocuments.Count; index++)
                {
                    var entry = _closedDocuments[index];
                    if (entry.State.Window.Group != Preferences.ActiveGroup) continue;
                    var time = Preferences.RestoreOrder == PinnedRestoreOrder.CreatedTime
                        ? entry.State.Window.CreatedAtUtcTicks
                        : entry.ClosedAt;
                    if (time < documentTime) continue;
                    documentIndex = index;
                    documentTime = time;
                }

                for (var index = 0; index < _closedImages.Count; index++)
                {
                    var entry = _closedImages[index];
                    if (entry.State.Group != Preferences.ActiveGroup) continue;
                    var time = Preferences.RestoreOrder == PinnedRestoreOrder.CreatedTime
                        ? entry.State.CreatedAtUtcTicks
                        : entry.ClosedAt;
                    if (time < imageTime) continue;
                    imageIndex = index;
                    imageTime = time;
                }

                if (documentIndex >= 0 && documentTime >= imageTime) return RestoreClosedDocument(documentIndex);
                return imageIndex >= 0 && await RestoreClosedAsync(_closedImages[imageIndex], token);
            }
            finally
            {
                _historyRestoreGate.Release();
            }
        }

        private async Task<bool> RestoreMatchingLiveCoreAsync(WindowRoiPinDescriptor descriptor,
            CancellationToken token)
        {
            await _historyRestoreGate.WaitAsync(token);
            try
            {
                var index = -1;
                for (var candidate = 0; candidate < _closedImages.Count; candidate++)
                {
                    if (_closedImages[candidate] is not PinnedClosedLiveImage image ||
                        image.State.Group != Preferences.ActiveGroup ||
                        !image.Snapshot.Descriptor.MatchesSource(descriptor)) continue;
                    if (index < 0 || Preferences.RestoreOrder == PinnedRestoreOrder.ClosedTime ||
                        image.State.CreatedAtUtcTicks > _closedImages[index].State.CreatedAtUtcTicks)
                        index = candidate;
                }

                return index >= 0 && await RestoreClosedAsync(_closedImages[index], token);
            }
            finally
            {
                _historyRestoreGate.Release();
            }
        }

        private bool RestoreMatchingClosed(PinnedImageContent content, string group)
        {
            var index = -1;
            for (var candidate = 0; candidate < _closedImages.Count; candidate++)
            {
                if (_closedImages[candidate] is not PinnedClosedStaticImage image || image.State.Group != group ||
                    image.State.LiveSource is not null || image.ContentHash != content.ContentHash) continue;
                if (index < 0 || Preferences.RestoreOrder == PinnedRestoreOrder.ClosedTime ||
                    _closedImages[candidate].State.CreatedAtUtcTicks > _closedImages[index].State.CreatedAtUtcTicks)
                    index = candidate;
            }

            return index >= 0 && RestoreClosedPrepared(_closedImages[index], content);
        }

        private async Task<bool> RestoreClosedAsync(PinnedClosedImage entry, CancellationToken token)
        {
            using var live = (entry as PinnedClosedLiveImage)?.Snapshot.Retain();
            var content = live is not null
                ? await live.Frame.PrepareContentAsync(token)
                : await Task.Run(() => PinnedImageContent.Decode(((PinnedClosedStaticImage)entry).Png, token), token);
            token.ThrowIfCancellationRequested();
            if (_disposed || !_closedImages.Contains(entry)) return false;
            return RestoreClosedPrepared(entry, content);
        }

        private bool RestoreClosedPrepared(PinnedClosedImage entry, PinnedImageContent content)
        {
            if (entry is PinnedClosedStaticImage image)
                content = content with
                {
                    OriginalEncoded = image.Original.Encoded, Animation = image.Original.Animation,
                    AnimationActive = image.AnimationActive,
                };
            var window = PinnedImageWindow.CreatePrepared(content, () => CanRestoreHidden(), Preferences);
            if (entry is PinnedClosedStaticImage original) window.RestoreOriginal(original.Original);
            AttachWindow(window);
            try
            {
                if (AllPinsHidden)
                {
                    entry.State.Visible = true;
                    entry.State.VisibleInGroup = true;
                    MaskRestoredWindow(window, entry.State);
                    window.RestoreHiddenState(entry.State);
                    _pendingRestore.Add(window, entry.State);
                    window.InitializeTextRecognition();
                }
                else
                {
                    window.Show();
                    entry.State.Visible = true;
                    window.RestoreState(entry.State);
                    window.RevealImage();
                }
            }
            catch (Exception error)
            {
                PinLogger.ZLogError(error, $"Closed pinned image restoration failed.");
                _windows.Remove(window);
                window.Dispose();
                throw;
            }

            _closedImages.Remove(entry);
            entry.Dispose();
            PinLogger.ZLogInformation(
                $"Closed pinned image restored. WindowCount={_windows.Count} ClosedHistoryCount={_closedImages.Count}");
            ScheduleSave();
            Changed?.Invoke(this, EventArgs.Empty);
            return true;
        }
    }
}
