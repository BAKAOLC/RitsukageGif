namespace RitsukageGif.Desktop
{
    internal sealed partial class SettingsWindow
    {
        internal Action<AppSettings>? AutomationBaselineChanged { private get; init; }

        internal void CommitAutomationDraft()
        {
            ApplyLiveSettings();
        }

        internal void RefreshAutomationSettings(AppSettings settings)
        {
            AutomationBaselineChanged?.Invoke(settings);
            BuildContent(CloneDraft(settings), _currentPage);
        }
    }
}
