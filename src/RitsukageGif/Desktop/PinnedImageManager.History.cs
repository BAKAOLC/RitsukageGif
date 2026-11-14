using ImageMagick;

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

        private async Task<int> ReadClosedImagesAsync(PinnedImageState[] states, CancellationToken token)
        {
            var rejected = 0;
            var history = states.Where(state => state.ClosedAtUtcTicks > 0).OrderBy(state => state.ClosedAtUtcTicks)
                .TakeLast(Preferences.HistoryLimit).ToArray();
            if (states.Count(state => state.ClosedAtUtcTicks > 0) != history.Length) ScheduleSave();
            foreach (var state in history)
                try
                {
                    var png = await ReadOriginalBytesAsync(ImagePath(state.ImageId), token);
                    var content = await Task.Run(() => PinnedImageContent.Decode(png, token), token);
                    var original = await ReadOriginalAsync(state, content, token);
                    state.Annotations = await ReadAnnotationImagesAsync(state.Annotations, token);
                    state.Translation = await ReadTranslationImageAsync(state.Translation, content.Png, token);
                    token.ThrowIfCancellationRequested();
                    ObjectDisposedException.ThrowIf(_disposed, this);
                    var entry = new PinnedClosedStaticImage(state, content.Png, content.ContentHash, original,
                        state.AnimationActive, state.ClosedAtUtcTicks);
                    if (entry.EstimatedBytes > 128L * 1024 * 1024)
                    {
                        rejected++;
                        continue;
                    }

                    _closedImages.Add(entry);
                    _sessionImageReferences[state.Id] = (entry.Png, original, state.ImageId);
                    TrimClosedHistory();
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or
                                                  MagickException or ArgumentException)
                {
                    PinLogger.ZLogWarning(error, $"Closed pinned image could not be loaded.");
                    rejected++;
                }

            return rejected;
        }

        private void DropClosedImage(int index)
        {
            var entry = _closedImages[index];
            _closedImages.RemoveAt(index);
            _sessionImageReferences.Remove(entry.State.Id);
            entry.Dispose();
        }

        private void TrimClosedHistory()
        {
            var count = _closedImages.Count + _closedDocuments.Count;
            while (_closedImages.Count > Preferences.HistoryLimit ||
                   _closedImages.Sum(entry => entry.EstimatedBytes) >
                   128L * 1024 * 1024)
                DropClosedImage(0);
            TrimMixedHistory();
            if (count != _closedImages.Count + _closedDocuments.Count) ScheduleSave();
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
                    var image = _closedImages[candidate];
                    if (image.State.Group != Preferences.ActiveGroup ||
                        image.State.LiveSource?.MatchesSource(descriptor) != true) continue;
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
            if (entry is PinnedClosedStaticImage original)
                window.RestoreOriginal(original.Original, !entry.State.HasOriginalImage &&
                                                          original.Png.Equals(
                                                              (ReadOnlyMemory<byte>)original.Original.Png));
            var state = entry.State.Copy();
            state.ClosedAtUtcTicks = 0;
            state.Visible = true;
            AttachWindow(window);
            try
            {
                if (AllPinsHidden)
                {
                    state.VisibleInGroup = true;
                    MaskRestoredWindow(window, state);
                    window.RestoreHiddenState(state);
                    _pendingRestore.Add(window, state);
                    window.InitializeTextRecognition();
                }
                else
                {
                    window.Show();
                    window.RestoreState(state);
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
