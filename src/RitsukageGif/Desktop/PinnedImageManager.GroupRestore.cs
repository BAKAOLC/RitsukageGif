namespace RitsukageGif.Desktop
{
    internal sealed partial class PinnedImageManager
    {
        private bool _groupRestoreRunning;
        private string? _requestedGroupRestore;
        private string? _requestedGroupAttention;

        private void FlashRequestedGroupAttention()
        {
            if (_requestedGroupAttention != Preferences.ActiveGroup) return;
            _requestedGroupAttention = null;
            if (!Preferences.FlashAfterGroupSwitch) return;
            foreach (var window in AllWindows)
                if (window.Group == Preferences.ActiveGroup && window.IsVisible)
                    window.Presentation.Attention.Flash();
        }

        private bool RequestGroupRestore()
        {
            if (_disposed || _sessionDirectory is null) return false;
            var group = Preferences.ActiveGroup;
            if (_unrestoredStates.Values.All(state => state.Group != group) &&
                _unrestoredDocuments.Values.All(state => state.Window.Group != group)) return false;
            _requestedGroupRestore = group;
            ResumeGroupRestore();
            return true;
        }

        private void ResumeGroupRestore()
        {
            if (_groupRestoreRunning || _requestedGroupRestore is null || _disposed ||
                _historyCancellation.IsCancellationRequested || !CanRestoreNow()) return;
            _groupRestoreRunning = true;
            var operation = RestoreRequestedGroupAsync();
            _historyOperations.Add(operation);
            _ = ObserveHistoryAsync(operation);
        }

        private async Task RestoreRequestedGroupAsync()
        {
            var token = _historyCancellation.Token;
            try
            {
                while (_requestedGroupRestore is { } group && !_disposed && CanRestoreNow())
                {
                    _requestedGroupRestore = null;
                    await FlushSessionAsync(token);
                    if (_disposed || group != Preferences.ActiveGroup) continue;
                    await RestoreSessionCoreAsync(group, token);
                    FlashRequestedGroupAttention();
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                PinLogger.ZLogDebug($"Pin group restoration canceled during shutdown.");
            }
            catch (Exception error)
            {
                PinLogger.ZLogError(error, $"Pin group restoration failed.");
                SessionError?.Invoke(error);
            }
            finally
            {
                _groupRestoreRunning = false;
            }
        }
    }
}
