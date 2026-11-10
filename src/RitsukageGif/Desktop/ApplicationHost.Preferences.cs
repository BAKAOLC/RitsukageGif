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
            ConfigureCaptureHistoryBudget();
            selection.InitializeAnnotationPreferences(Settings.Annotation);
            if (selection.Annotations is { } annotations)
                annotations.ToolbarPreferencesProvider = () => Settings.Annotation.Toolbar;
            selection.AnnotationToolbarChanged += (preferences, tools) =>
                PersistAnnotationPreferences(Settings.Annotation with
                {
                    Toolbar = Settings.Annotation.Toolbar.Merge(preferences, tools),
                });
            selection.ScrollCapturePreferences = Settings.ScrollCapture;
            selection.ScrollAutoCropChanged = PersistScrollAutoCrop;
            selection.ScrollClearConfirmationChanged = PersistScrollClearConfirmation;
            selection.InitializePreferences(Settings.SelectionDetectControls, Settings.SelectionSnappingMode,
                Settings.SelectionHints, Settings.CapturePreferences);
            selection.InitializeAppearancePreferences(Settings.Appearance);
            var capturePreferences = Settings.CapturePreferences.Normalize();
            selection.KeepDecorationInitial = capturePreferences.KeepDecoration;
            selection.InitialDecoration = capturePreferences.FrameDecoration;
            selection.FrameRoundInitialEnabled = capturePreferences.FrameRoundEnabled;
            selection.FrameRoundInitialRadius = capturePreferences.FrameRoundRadius;
            selection.FramePreferencesChanged += () => RememberFrameDecorationPreferences(selection);
            selection.RecognitionPreferences = Settings.Recognition;
            selection.RecognitionImageSaveQuality = () => Settings.ScreenshotSave.Quality;
            selection.RecognitionDialogPreferencesChanged = PersistRecognitionDialog;
            selection.DirectRecognitionCopyRequested = DisableRecognitionConfirmation;
            selection.FormulaCopyFormatChanged = PersistFormulaCopyFormat;
            selection.RecognitionOriginalImageVisibilityChanged = PersistRecognitionOriginalImage;
            selection.FormulaLatexFormatChanged = PersistFormulaLatexFormat;
            selection.TranslationPreferences = Settings.Translation;
            selection.ConfigureHistory(_captureHistory);
            selection.PreferencesChanged += () =>
            {
                Settings.CaptureSource = selection.WindowSource ? CaptureSourceKind.Window : CaptureSourceKind.Screen;
                if (selection.CanDetectControls) Settings.SelectionDetectControls = selection.Controls;
                Settings.CapturePreferences = Settings.CapturePreferences with
                {
                    DetectionMode = selection.DetectionMode,
                    RelativeCoordinates = selection.RelativeCoordinates,
                    CursorPolicy = selection.Snapshot.Cursor ? CaptureCursorPolicy.Visible : CaptureCursorPolicy.Hidden,
                    InformationUnit = selection.CapturePreferences.InformationUnit,
                    InformationItems = selection.CapturePreferences.InformationItems,
                };
                Settings.SelectionSnappingMode = selection.SnappingMode;
                Settings.SelectionHints = selection.HintMode;
                SaveCapturePreferences();
            };
        }

        private void PersistScrollAutoCrop(bool enabled)
        {
            if (IsClosing || Settings.ScrollCapture.AutoCropOnReverse == enabled) return;
            Settings.ScrollCapture = Settings.ScrollCapture with { AutoCropOnReverse = enabled };
            SaveCapturePreferences();
        }

        private void PersistScrollClearConfirmation(bool skip)
        {
            if (IsClosing || Settings.ScrollCapture.SkipClearConfirmation == skip) return;
            Settings.ScrollCapture = Settings.ScrollCapture with { SkipClearConfirmation = skip };
            SaveCapturePreferences();
        }

        private void PersistRecognitionOriginalImage(bool show)
        {
            if (IsClosing || Settings.Recognition.ShowOriginalImage == show) return;
            Settings.Recognition = Settings.Recognition with { ShowOriginalImage = show };
            QueueSettingsSave(Settings);
        }

        private void DisableRecognitionConfirmation(RecognitionConfirmationKind kind)
        {
            if (IsClosing) return;
            var preferences = kind switch
            {
                RecognitionConfirmationKind.Screenshot => Settings.Recognition with { ShowDialogOnRecognition = false },
                RecognitionConfirmationKind.PinSelectedText => Settings.Recognition with
                {
                    ShowDialogOnCopySelected = false,
                },
                RecognitionConfirmationKind.PinAllText => Settings.Recognition with { ShowDialogOnCopyAll = false },
                _ => throw new ArgumentOutOfRangeException(nameof(kind)),
            };
            if (preferences == Settings.Recognition) return;
            Settings.Recognition = preferences;
            if (_screenshotSelection is { } selection) selection.RecognitionPreferences = preferences;
            SaveCapturePreferences();
        }

        private void PersistRecognitionDialog(RecognitionDialogPreferences baseline,
            RecognitionDialogPreferences edited)
        {
            if (IsClosing) return;
            var merged = Settings.Recognition.Dialog.MergeChanges(baseline, edited);
            if (merged == Settings.Recognition.Dialog) return;
            Settings.Recognition = Settings.Recognition with { Dialog = merged };
            if (_screenshotSelection is { } selection) selection.RecognitionPreferences = Settings.Recognition;
            SaveCapturePreferences();
        }

        private void PersistFormulaLatexFormat(FormulaLatexFormat format)
        {
            if (IsClosing || Settings.Recognition.LatexFormat == format) return;
            Settings.Recognition = Settings.Recognition with { LatexFormat = format };
            QueueSettingsSave(Settings);
        }

        private void PersistFormulaCopyFormat(FormulaCopyFormat format)
        {
            if (IsClosing || Settings.Recognition.CopyFormat == format) return;
            Settings.Recognition = Settings.Recognition with { CopyFormat = format };
            QueueSettingsSave(Settings);
        }

        private void RememberAnnotationPreferences(SelectionCoordinator selection)
        {
            var preferences = selection.CaptureAnnotationPreferences(Settings.Annotation);
            if (AppSettings.Same(preferences, Settings.Annotation)) return;
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
                FrameDecoration = selection.FrameDecorationPreferences,
                FrameRoundEnabled = selection.FrameRoundEnabled,
                FrameRoundRadius = selection.FrameCornerRadius,
            }).Normalize();
            if (AppSettings.Same(preferences, Settings.CapturePreferences)) return;
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
