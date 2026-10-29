namespace RitsukageGif.Desktop
{
    public sealed partial class ApplicationHost
    {
        // ReSharper disable once AutoPropertyCanBeMadeGetOnly.Global, MemberCanBePrivate.Global
        internal Action<AppSettings> SettingsWriter { get; init; } = settings => settings.Save();

        internal void SetCaptureCursor(bool enabled)
        {
            if (Settings.Cursor == enabled) return;
            Settings.Cursor = enabled;
            Settings.CapturePreferences = Settings.CapturePreferences with
            {
                CursorPolicy = enabled ? CaptureCursorPolicy.Visible : CaptureCursorPolicy.Hidden,
            };
            SaveCapturePreferences();
        }

        private void SaveCapturePreferences()
        {
            try
            {
                QueueSettingsSave(Settings);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                SetStatus(error.Message);
            }

            Notify();
        }

        private void ConfigureSelectionPreferences(SelectionCoordinator selection)
        {
            _captureHistory.Configure(Settings.CapturePreferences.RegionHistoryCapacity,
                Settings.CapturePreferences.ScreenshotHistoryCapacity,
                (long)Settings.CapturePreferences.ScreenshotHistoryMaximumMiB * 1024 * 1024);
            selection.InitializeAnnotationPreferences(Settings.Annotation);
            if (selection.Annotations is { } annotations)
                annotations.ToolbarPreferencesProvider = () => Settings.Annotation.Toolbar;
            selection.AnnotationToolbarChanged += (preferences, tools) =>
                PersistAnnotationPreferences(Settings.Annotation with
                {
                    Toolbar = Settings.Annotation.Toolbar.Merge(preferences, tools),
                });
            selection.ScrollCapturePreferences = Settings.ScrollCapture;
            selection.InitializePreferences(Settings.SelectionDetectControls, Settings.SelectionSnappingMode,
                Settings.SelectionHints, Settings.CapturePreferences);
            selection.InitializeAppearancePreferences(Settings.Appearance);
            var capturePreferences = Settings.CapturePreferences.Normalize();
            selection.InitialDecoration = capturePreferences.KeepDecoration
                ? capturePreferences.RememberedDecoration
                : CaptureDecoration.None;
            selection.KeepDecorationInitial = capturePreferences.KeepDecoration;
            selection.FrameRoundInitialEnabled = capturePreferences.FrameRoundEnabled;
            selection.FrameRoundInitialRadius = capturePreferences.FrameRoundRadius;
            selection.RecognitionPreferences = Settings.Recognition;
            selection.ConfigureHistory(_captureHistory);
            selection.PreferencesChanged += () =>
            {
                Settings.Cursor = selection.Snapshot.Cursor;
                Settings.CaptureSource = selection.WindowSource ? CaptureSourceKind.Window : CaptureSourceKind.Screen;
                if (selection.CanDetectControls) Settings.SelectionDetectControls = selection.Controls;
                Settings.CapturePreferences = Settings.CapturePreferences with
                {
                    DetectionMode = selection.DetectionMode,
                };
                Settings.SelectionSnappingMode = selection.SnappingMode;
                Settings.SelectionHints = selection.HintMode;
                SaveCapturePreferences();
            };
        }

        private void RememberAnnotationPreferences(SelectionCoordinator selection)
        {
            var preferences = selection.CaptureAnnotationPreferences(Settings.Annotation);
            if (preferences == Settings.Annotation) return;
            try
            {
                PersistAnnotationPreferences(preferences);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                SetStatus(error.Message);
            }
        }

        private void RememberFrameDecorationPreferences(SelectionCoordinator selection)
        {
            var preferences = (Settings.CapturePreferences with
            {
                KeepDecoration = selection.KeepDecoration,
                RememberedDecoration = selection.RememberedDecoration,
                FrameRoundEnabled = selection.FrameRoundEnabled,
                FrameRoundRadius = selection.FrameCornerRadius,
            }).Normalize();
            if (preferences == Settings.CapturePreferences) return;
            try
            {
                var updated = Settings with { CapturePreferences = preferences };
                QueueSettingsSave(updated);
                Settings = updated;
                Notify();
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                SetStatus(error.Message);
            }
        }

        private void PersistAnnotationPreferences(AnnotationPreferences preferences)
        {
            var updated = Settings with { Annotation = preferences };
            QueueSettingsSave(updated);
            Settings = updated;
            _pinnedImages.AnnotationPreferences = preferences;
            Notify();
        }
    }
}
