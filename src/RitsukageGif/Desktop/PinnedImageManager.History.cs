using Avalonia.Threading;

namespace RitsukageGif.Desktop
{
    internal sealed partial class PinnedImageManager
    {
        private readonly List<(PinnedImageState State, ReadOnlyMemory<byte> Png, string ContentHash,
                PinnedImageOriginal Original, bool AnimationActive)>
            _closedImages = [];

        public bool CanRestoreClosed => _closedImages.Count != 0;

        private void RememberClosed(PinnedImageWindow window)
        {
            var png = window.CurrentPng;
            if (Preferences.HistoryLimit == 0) return;
            if ((long)png.Length + window.OriginalContent.Png.Length +
                (window.OriginalContent.SdrPng?.Length ?? 0) + (window.OriginalContent.Hdr?.Pixels.Length ?? 0) +
                (window.OriginalContent.Encoded?.Length ?? 0) >
                128L * 1024 * 1024) return;
            _closedImages.Add((window.CaptureState(), png, window.ContentHash, window.OriginalContent,
                window.StoredAnimationActive));
            TrimClosedHistory();
        }

        private void TrimClosedHistory()
        {
            while (_closedImages.Count > Preferences.HistoryLimit ||
                   _closedImages.Sum(entry => (long)entry.Png.Length + entry.Original.Png.Length +
                                              (entry.Original.SdrPng?.Length ?? 0) +
                                              (entry.Original.Hdr?.Pixels.Length ?? 0) +
                                              (entry.Original.Encoded?.Length ?? 0)) >
                   128L * 1024 * 1024)
                _closedImages.RemoveAt(0);
        }

        // ReSharper disable once UnusedMethodReturnValue.Global
        public bool RestoreLastClosed()
        {
            Dispatcher.UIThread.VerifyAccess();
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_closedImages.Count == 0) return false;
            var index = _closedImages.Count - 1;
            if (Preferences.RestoreOrder == PinnedRestoreOrder.CreatedTime)
                for (var candidate = 0; candidate < _closedImages.Count; candidate++)
                    if (_closedImages[candidate].State.CreatedAtUtcTicks > _closedImages[index].State.CreatedAtUtcTicks)
                        index = candidate;
            return RestoreClosed(index);
        }

        private bool RestoreMatchingClosed(string contentHash)
        {
            var index = -1;
            for (var candidate = 0; candidate < _closedImages.Count; candidate++)
            {
                if (_closedImages[candidate].ContentHash != contentHash) continue;
                if (index < 0 || Preferences.RestoreOrder == PinnedRestoreOrder.ClosedTime ||
                    _closedImages[candidate].State.CreatedAtUtcTicks > _closedImages[index].State.CreatedAtUtcTicks)
                    index = candidate;
            }

            return index >= 0 && RestoreClosed(index);
        }

        private bool RestoreClosed(int index)
        {
            var entry = _closedImages[index];
            var content = PinnedImageContent.Decode(entry.Png) with
            {
                OriginalEncoded = entry.Original.Encoded, Animation = entry.Original.Animation,
                AnimationActive = entry.AnimationActive,
            };
            var window = PinnedImageWindow.CreatePrepared(content,
                () => CanRestoreHidden(), Preferences);
            window.RestoreOriginal(entry.Original);
            AttachWindow(window);
            try
            {
                window.Show();
                entry.State.Visible = true;
                window.RestoreState(entry.State);
                window.RevealImage();
            }
            catch (Exception error)
            {
                PinLogger.ZLogError(error, $"Closed pinned image restoration failed.");
                _windows.Remove(window);
                window.Dispose();
                throw;
            }

            _closedImages.RemoveAt(index);
            PinLogger.ZLogInformation(
                $"Closed pinned image restored. WindowCount={_windows.Count} ClosedHistoryCount={_closedImages.Count}");
            ScheduleSave();
            Changed?.Invoke(this, EventArgs.Empty);
            return true;
        }
    }
}
