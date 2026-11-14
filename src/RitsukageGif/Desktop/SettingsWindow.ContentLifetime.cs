namespace RitsukageGif.Desktop
{
    internal sealed partial class SettingsWindow
    {
        private readonly List<Action> _contentCleanup = [];
        private CancellationTokenSource? _contentLifetime;
        private string _currentPage = "GeneralHeading";
        private AppSettings? _draft;
        private Action? _readDraft;

        private CancellationToken ContentCancellationToken => _contentLifetime?.Token ?? new CancellationToken(true);

        private void SubscribeContentLanguage(Action refresh)
        {
            UiText.LanguageChanged += refresh;
            RegisterContentCleanup(() => UiText.LanguageChanged -= refresh);
        }

        private void RegisterContentCleanup(Action cleanup)
        {
            _contentCleanup.Add(cleanup);
        }

        private void DisposeContent()
        {
            _readDraft = null;
            _liveApplyQueued = false;
            _liveChangesPending = false;
            _explicitLiveChangePending = false;
            _liveInputChanges.Clear();
            _contentLifetime?.Cancel();
            foreach (var cleanup in _contentCleanup) cleanup();
            _contentCleanup.Clear();
            _contentLifetime?.Dispose();
            _contentLifetime = null;
            Content = null;
        }

        private async Task ResetCurrentSettingsAsync()
        {
            if (_draft is null || _readDraft is null || !SettingsDefaults.CanResetPage(_currentPage)) return;
            var token = ContentCancellationToken;
            var page = _currentPage;
            try
            {
                if (!await ConfirmSettingsChangeAsync("SettingsResetConfirm", "SettingsResetCurrentQuestion") ||
                    token.IsCancellationRequested) return;
                ApplyLiveSettings();
                await ReplaceLiveSettingsAsync(SettingsDefaults.ResetPage(CloneDraft(_appliedSettings ?? _draft),
                    page));
            }
            catch (Exception error)
            {
                _error.Text = error.Message;
            }
        }

        private async Task ResetAllSettingsAsync()
        {
            var token = ContentCancellationToken;
            try
            {
                if (!await ConfirmSettingsChangeAsync("SettingsResetConfirm", "SettingsResetAllQuestion") ||
                    token.IsCancellationRequested) return;
                await ReplaceLiveSettingsAsync(new());
            }
            catch (Exception error)
            {
                NetworkLogger.ZLogWarning($"Settings reset failed. ExceptionType={error.GetType().Name}");
                if (!ContentCancellationToken.IsCancellationRequested)
                    _error.Text = UiText.Get("NetworkProxySaveFailed");
            }
        }

        private static AppSettings CloneDraft(AppSettings settings)
        {
            return settings.Snapshot();
        }
    }
}
