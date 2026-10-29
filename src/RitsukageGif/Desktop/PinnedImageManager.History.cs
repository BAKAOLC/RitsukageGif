using Avalonia.Threading;

namespace RitsukageGif.Desktop
{
    internal sealed partial class PinnedImageManager
    {
        private readonly List<(PinnedImageState State, ReadOnlyMemory<byte> Png, string ContentHash)>
            _closedImages = [];

        public bool CanRestoreClosed => _closedImages.Count != 0;

        private void RememberClosed(PinnedImageWindow window)
        {
            var png = window.OriginalPng;
            if (Preferences.HistoryLimit == 0) return;
            if (png.Length > 128 * 1024 * 1024) return;
            _closedImages.Add((window.CaptureState(), png, window.ContentHash));
            TrimClosedHistory();
        }

        private void TrimClosedHistory()
        {
            while (_closedImages.Count > Preferences.HistoryLimit ||
                   _closedImages.Sum(entry => (long)entry.Png.Length) > 128L * 1024 * 1024)
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
            var window = PinnedImageWindow.CreatePrepared(PinnedImageContent.Decode(entry.Png),
                () => CanRestoreHidden(), Preferences);
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
