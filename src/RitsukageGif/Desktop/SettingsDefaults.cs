namespace RitsukageGif.Desktop
{
    internal static class SettingsDefaults
    {
        internal static bool CanResetPage(string key)
        {
            return key is "GeneralHeading" or "AppearanceHeading" or "ScreenshotHeading" or "SettingsAnnotationHeading"
                or "SettingsPinsHeading" or "ScreenshotSaveHeading" or "GlobalActionsHeading" or "SettingsLocalInput"
                or "MouseShortcutsHeading" or "GlobalMouseHeading";
        }

        internal static AppSettings ResetPage(AppSettings value, string key)
        {
            var defaults = new AppSettings();
            return key switch
            {
                "GeneralHeading" => value with
                {
                    Language = defaults.Language, System = defaults.System, Recognition = defaults.Recognition,
                    CheckForUpdates = defaults.CheckForUpdates, CloseToTray = defaults.CloseToTray,
                    MinimizeToTray = defaults.MinimizeToTray, Sound = defaults.Sound,
                    StartSound = defaults.StartSound, StopSound = defaults.StopSound,
                    ScreenshotClipboard = value.ScreenshotClipboard with
                    {
                        Mode = defaults.ScreenshotClipboard.Mode, FileFormat = defaults.ScreenshotClipboard.FileFormat,
                        OutputSize = defaults.ScreenshotClipboard.OutputSize,
                    },
                },
                "AppearanceHeading" => value with
                {
                    Appearance = defaults.Appearance, BackgroundImage = defaults.BackgroundImage,
                    ShowBackground = defaults.ShowBackground,
                },
                "ScreenshotHeading" => value with
                {
                    CapturePreferences = defaults.CapturePreferences, ScrollCapture = defaults.ScrollCapture,
                    ScreenshotClipboard = value.ScreenshotClipboard with
                    {
                        HdpiTargetPercent = defaults.ScreenshotClipboard.HdpiTargetPercent,
                        PasteAfterCopy = defaults.ScreenshotClipboard.PasteAfterCopy,
                    },
                    CaptureColorMode = defaults.CaptureColorMode, CaptureToneMap = defaults.CaptureToneMap,
                    Quality = defaults.Quality, UseFfmpeg = defaults.UseFfmpeg,
                    Cursor = defaults.Cursor, SelectionDetectControls = defaults.SelectionDetectControls,
                    SnapRadius = defaults.SnapRadius, EdgeThreshold = defaults.EdgeThreshold,
                    SelectionSnappingMode = defaults.SelectionSnappingMode, SelectionHints = defaults.SelectionHints,
                    Annotation = value.Annotation with
                    {
                        Toolbar = value.Annotation.Toolbar with { Actions = defaults.Annotation.Toolbar.Actions },
                    },
                },
                "SettingsAnnotationHeading" => value with
                {
                    Annotation = ResetAnnotationBehavior(value.Annotation, defaults.Annotation),
                },
                "SettingsPinsHeading" => value with
                {
                    PinnedImages = defaults.PinnedImages, RestorePinnedImages = defaults.RestorePinnedImages,
                },
                "ScreenshotSaveHeading" => value with
                {
                    OutputDirectory = defaults.OutputDirectory, ScreenshotSave = defaults.ScreenshotSave,
                    CleanupEnabled = defaults.CleanupEnabled, CleanupMaxAgeDays = defaults.CleanupMaxAgeDays,
                    CleanupMaxFiles = defaults.CleanupMaxFiles, CleanupMaxSizeMiB = defaults.CleanupMaxSizeMiB,
                },
                "GlobalActionsHeading" => value with
                {
                    GlobalActions = defaults.GlobalActions,
                    GlobalMouseBindings = [], HotkeyPolicy = defaults.HotkeyPolicy,
                },
                "SettingsLocalInput" => value with { SelectionShortcuts = new(StringComparer.Ordinal) },
                "MouseShortcutsHeading" => value with { MouseShortcuts = defaults.MouseShortcuts },
                "GlobalMouseHeading" => value with { GlobalMouseBindings = defaults.GlobalMouseBindings },
                _ => value,
            };
        }

        private static AnnotationPreferences ResetAnnotationBehavior(AnnotationPreferences value,
            AnnotationPreferences defaults)
        {
            return value with
            {
                ColorPalette = defaults.ColorPalette, SharedColor = defaults.SharedColor,
                RememberLastTool = defaults.RememberLastTool, RememberPinnedTool = defaults.RememberPinnedTool,
                LimitToSelection = defaults.LimitToSelection,
                SecondaryEditingEnabled = defaults.SecondaryEditingEnabled,
                QuickSettingsEnabled = defaults.QuickSettingsEnabled,
                QuickSettingsPadding = defaults.QuickSettingsPadding,
                PencilCursor = defaults.PencilCursor, MaximumBrushThickness = defaults.MaximumBrushThickness,
                DisableWheelToolAdjustment = defaults.DisableWheelToolAdjustment,
                ControlPointVisibility = defaults.ControlPointVisibility, RenumberOnDelete = defaults.RenumberOnDelete,
                NumberStartsAtZero = defaults.NumberStartsAtZero, GlobalNumberCounter = defaults.GlobalNumberCounter,
                SmoothBrush = defaults.SmoothBrush, PencilShiftMode = defaults.PencilShiftMode,
                PolylineJoin = defaults.PolylineJoin, PolylineCap = defaults.PolylineCap,
                ArrowDoubleClickCommentEnabled = defaults.ArrowDoubleClickCommentEnabled,
                ArrowCurveControlPoints = defaults.ArrowCurveControlPoints, TextWrapMode = defaults.TextWrapMode,
                EraserSecondaryEditingEnabled = defaults.EraserSecondaryEditingEnabled,
                AutoSelectPrivacyMosaic = defaults.AutoSelectPrivacyMosaic,
                Toolbar = value.Toolbar with { Columns = defaults.Toolbar.Columns },
            };
        }
    }
}
