using Avalonia.Threading;

namespace RitsukageGif.Desktop
{
    internal sealed partial class PinnedImageManager
    {
        public async Task ClearRecordDataAsync(bool history, bool onScreen)
        {
            Dispatcher.UIThread.VerifyAccess();
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!history && !onScreen) return;
            var token = _historyCancellation.Token;
            await _historyRestoreGate.WaitAsync(token);
            try
            {
                await _sessionGate.WaitAsync(token);
                try
                {
                    ObjectDisposedException.ThrowIf(_disposed, this);
                    if (onScreen)
                    {
                        foreach (var window in AllWindows.ToArray()) window.DestroyForGroup();
                        _unrestoredStates.Clear();
                        _unrestoredDocuments.Clear();
                    }

                    if (history)
                    {
                        foreach (var entry in _closedImages) entry.Dispose();
                        _closedImages.Clear();
                        _closedDocuments.Clear();
                    }

                    if (onScreen)
                    {
                        _saveTimer.Stop();
                        _sessionTouched = true;
                        if (_sessionDirectory is { } directory) await WriteSessionCoreAsync(directory, token);
                    }

                    PinLogger.ZLogInformation($"Pinned records cleared. History={history} OnScreen={onScreen}");
                }
                finally
                {
                    _sessionGate.Release();
                }
            }
            finally
            {
                _historyRestoreGate.Release();
            }

            GroupsChanged?.Invoke();
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}
