namespace RitsukageGif.Desktop
{
    internal sealed partial class ScreenshotRecordingController
    {
        internal RecordingToolbarCaptureMode ToolbarCaptureMode =>
            _host.Settings.ScreenshotRecording.ToolbarCaptureMode;

        internal void ConfigureToolbarCaptureMode(RecordingToolbarCaptureMode mode)
        {
            _preferences = _host.Settings.ScreenshotRecording with { ToolbarCaptureMode = mode };
            _host.UpdateScreenshotRecordingPreferences(_preferences);
        }

        internal void ConfigureIncludedCaptureWindowTitle(string? window)
        {
            Selection = Selection with { IncludedCaptureWindowTitle = window };
        }

        internal void ConfigureKeyboardActions(bool enabled)
        {
            RecordKeyboardActions = enabled;
            _preferences = _host.Settings.ScreenshotRecording with { RecordKeyboardActions = RecordKeyboardActions };
            _host.UpdateScreenshotRecordingPreferences(_preferences);
            Notify();
        }

        internal void ConfigureMouseActions(bool enabled)
        {
            RecordMouseActions = enabled;
            _preferences = _host.Settings.ScreenshotRecording with { RecordMouseActions = RecordMouseActions };
            _host.UpdateScreenshotRecordingPreferences(_preferences);
            Notify();
        }

        private RecordingInputEffects CaptureInputEffects(bool cursor)
        {
            return _host.Settings.ScreenshotRecording.InputEffects with { ShowCursor = cursor };
        }

        private void ConfigureInputEffects(RecordingInputEffects effects)
        {
            _preferences = _host.Settings.ScreenshotRecording with { InputEffects = effects.Normalize() };
            _host.UpdateScreenshotRecordingPreferences(_preferences);
        }

        private void ConfigureProgressEffects(RecordingProgressEffects effects)
        {
            _preferences = _host.Settings.ScreenshotRecording with { ProgressEffects = effects.Normalize() };
            _host.UpdateScreenshotRecordingPreferences(_preferences);
        }
    }
}
