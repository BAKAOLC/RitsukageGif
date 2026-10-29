using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    public sealed partial record AppSettings
    {
        internal bool ContentEquals(AppSettings? other)
        {
            return Same(this, other);
        }

        private static bool Same(AppSettings? left, AppSettings? right)
        {
            if (ReferenceEquals(left, right)) return true;
            if (left is null || right is null) return false;
            return Same(left.Appearance, right.Appearance) &&
                   EqualityComparer<string>.Default.Equals(left.Language, right.Language) &&
                   EqualityComparer<bool>.Default.Equals(left.CleanupEnabled, right.CleanupEnabled) &&
                   EqualityComparer<int?>.Default.Equals(left.CleanupMaxAgeDays, right.CleanupMaxAgeDays) &&
                   EqualityComparer<int?>.Default.Equals(left.CleanupMaxFiles, right.CleanupMaxFiles) &&
                   EqualityComparer<int?>.Default.Equals(left.CleanupMaxSizeMiB, right.CleanupMaxSizeMiB) &&
                   EqualityComparer<bool>.Default.Equals(left.CheckForUpdates, right.CheckForUpdates) &&
                   EqualityComparer<bool>.Default.Equals(left.CloseToTray, right.CloseToTray) &&
                   EqualityComparer<bool>.Default.Equals(left.MinimizeToTray, right.MinimizeToTray) &&
                   EqualityComparer<bool>.Default.Equals(left.RestorePinnedImages, right.RestorePinnedImages) &&
                   Same(left.ScreenshotClipboard, right.ScreenshotClipboard) &&
                   Same(left.ScreenshotSave, right.ScreenshotSave) &&
                   Same(left.Annotation, right.Annotation) &&
                   Same(left.PinnedImages, right.PinnedImages) &&
                   SameSequence(left.PresetFiles, right.PresetFiles,
                       static (a, b) => EqualityComparer<string>.Default.Equals(a, b)) &&
                   SameSequence(left.PresetHotkeys, right.PresetHotkeys,
                       static (a, b) => EqualityComparer<PresetHotkey>.Default.Equals(a, b)) &&
                   Same(left.ScrollCapture, right.ScrollCapture) &&
                   Same(left.CapturePreferences, right.CapturePreferences) &&
                   Same(left.HotkeyPolicy, right.HotkeyPolicy) &&
                   SameSequence(left.GlobalActions, right.GlobalActions, static (a, b) => Same(a, b)) &&
                   SameSequence(left.GlobalMouseBindings, right.GlobalMouseBindings, static (a, b) => Same(a, b)) &&
                   Same(left.VideoRecording, right.VideoRecording) &&
                   EqualityComparer<int>.Default.Equals(left.Fps, right.Fps) &&
                   EqualityComparer<int>.Default.Equals(left.Divisor, right.Divisor) &&
                   EqualityComparer<OutputFormat>.Default.Equals(left.Format, right.Format) &&
                   EqualityComparer<OutputColorMode>.Default.Equals(left.CaptureColorMode, right.CaptureColorMode) &&
                   EqualityComparer<HdrToneMapMode>.Default.Equals(left.CaptureToneMap, right.CaptureToneMap) &&
                   EqualityComparer<GifQuality>.Default.Equals(left.Quality, right.Quality) &&
                   EqualityComparer<bool>.Default.Equals(left.UseFfmpeg, right.UseFfmpeg) &&
                   EqualityComparer<bool>.Default.Equals(left.Cursor, right.Cursor) &&
                   EqualityComparer<CaptureSourceKind>.Default.Equals(left.CaptureSource, right.CaptureSource) &&
                   EqualityComparer<bool>.Default.Equals(left.Sound, right.Sound) &&
                   EqualityComparer<string>.Default.Equals(left.StartSound, right.StartSound) &&
                   EqualityComparer<string>.Default.Equals(left.StopSound, right.StopSound) &&
                   EqualityComparer<string>.Default.Equals(left.BackgroundImage, right.BackgroundImage) &&
                   EqualityComparer<bool>.Default.Equals(left.ShowBackground, right.ShowBackground) &&
                   EqualityComparer<int>.Default.Equals(left.SnapRadius, right.SnapRadius) &&
                   EqualityComparer<int>.Default.Equals(left.EdgeThreshold, right.EdgeThreshold) &&
                   EqualityComparer<bool>.Default.Equals(left.SelectionDetectControls, right.SelectionDetectControls) &&
                   EqualityComparer<int>.Default.Equals(left.SelectionSnappingMode, right.SelectionSnappingMode) &&
                   EqualityComparer<SelectionHintMode>.Default.Equals(left.SelectionHints, right.SelectionHints) &&
                   EqualityComparer<string>.Default.Equals(left.OutputDirectory, right.OutputDirectory) &&
                   SameDictionary(left.SelectionShortcuts, right.SelectionShortcuts,
                       static (a, b) =>
                           SameSequence(a, b, static (a, b) => EqualityComparer<string>.Default.Equals(a, b))) &&
                   SameDictionary(left.MouseShortcuts, right.MouseShortcuts,
                       static (a, b) =>
                           SameSequence(a, b, static (a, b) => EqualityComparer<string>.Default.Equals(a, b))) &&
                   Same(left.System, right.System) &&
                   Same(left.Recognition, right.Recognition);
        }

        private static bool Same(AppearancePreferences? left, AppearancePreferences? right)
        {
            if (ReferenceEquals(left, right)) return true;
            if (left is null || right is null) return false;
            return EqualityComparer<AppearanceThemeMode>.Default.Equals(left.ThemeMode, right.ThemeMode) &&
                   EqualityComparer<string>.Default.Equals(left.FontFamily, right.FontFamily) &&
                   EqualityComparer<uint>.Default.Equals(left.AccentColor, right.AccentColor) &&
                   EqualityComparer<uint>.Default.Equals(left.LockedWindowColor, right.LockedWindowColor) &&
                   EqualityComparer<uint>.Default.Equals(left.SelectionMaskColor, right.SelectionMaskColor) &&
                   EqualityComparer<uint>.Default.Equals(left.ClickThroughShadeColor, right.ClickThroughShadeColor) &&
                   EqualityComparer<TrayIconAppearance>.Default.Equals(left.TrayIcon, right.TrayIcon) &&
                   EqualityComparer<uint>.Default.Equals(left.TrayIconColor, right.TrayIconColor) &&
                   EqualityComparer<ToolbarIconSize>.Default.Equals(left.ToolbarIconSize, right.ToolbarIconSize) &&
                   EqualityComparer<AppearanceDialogStyle>.Default.Equals(left.ColorDialogStyle,
                       right.ColorDialogStyle) &&
                   EqualityComparer<AppearanceDialogStyle>.Default.Equals(left.SaveDialogStyle,
                       right.SaveDialogStyle) &&
                   EqualityComparer<double>.Default.Equals(left.SelectionBorderWidth, right.SelectionBorderWidth) &&
                   EqualityComparer<uint?>.Default.Equals(left.SelectionBorderColor, right.SelectionBorderColor) &&
                   EqualityComparer<SelectionAnchorMode>.Default.Equals(left.SelectionAnchors,
                       right.SelectionAnchors) &&
                   EqualityComparer<bool>.Default.Equals(left.AnimateSelection, right.AnimateSelection);
        }

        private static bool Same(ScreenshotClipboardPreferences? left, ScreenshotClipboardPreferences? right)
        {
            if (ReferenceEquals(left, right)) return true;
            if (left is null || right is null) return false;
            return EqualityComparer<ScreenshotClipboardMode>.Default.Equals(left.Mode, right.Mode) &&
                   EqualityComparer<ScreenshotClipboardFileFormat>.Default.Equals(left.FileFormat, right.FileFormat) &&
                   Same(left.OutputSize, right.OutputSize) &&
                   EqualityComparer<int>.Default.Equals(left.HdpiTargetPercent, right.HdpiTargetPercent) &&
                   EqualityComparer<bool>.Default.Equals(left.PasteAfterCopy, right.PasteAfterCopy);
        }

        private static bool Same(ScreenshotSavePreferences? left, ScreenshotSavePreferences? right)
        {
            if (ReferenceEquals(left, right)) return true;
            if (left is null || right is null) return false;
            return EqualityComparer<string>.Default.Equals(left.Directory, right.Directory) &&
                   EqualityComparer<ScreenshotAfterSaveAction>.Default.Equals(left.AfterSave, right.AfterSave) &&
                   EqualityComparer<bool>.Default.Equals(left.Notify, right.Notify) &&
                   EqualityComparer<StaticImageFormat>.Default.Equals(left.Format, right.Format) &&
                   EqualityComparer<int>.Default.Equals(left.Quality, right.Quality) &&
                   Same(left.OutputSize, right.OutputSize) &&
                   EqualityComparer<string>.Default.Equals(left.NameTemplate, right.NameTemplate) &&
                   EqualityComparer<ScreenshotConflictPolicy>.Default.Equals(left.ConflictPolicy,
                       right.ConflictPolicy) &&
                   Same(left.Quick, right.Quick) &&
                   Same(left.Automatic, right.Automatic) &&
                   EqualityComparer<ScreenshotAutoSaveTriggers>.Default.Equals(left.AutoSaveTriggers,
                       right.AutoSaveTriggers) &&
                   EqualityComparer<bool>.Default.Equals(left.RememberExtension, right.RememberExtension) &&
                   EqualityComparer<StaticImageFormat?>.Default.Equals(left.LastFormat, right.LastFormat) &&
                   EqualityComparer<string?>.Default.Equals(left.LastExtension, right.LastExtension);
        }

        private static bool Same(ScreenshotSaveProfile? left, ScreenshotSaveProfile? right)
        {
            if (ReferenceEquals(left, right)) return true;
            if (left is null || right is null) return false;
            return EqualityComparer<string>.Default.Equals(left.Directory, right.Directory) &&
                   EqualityComparer<ScreenshotAfterSaveAction>.Default.Equals(left.AfterSave, right.AfterSave) &&
                   EqualityComparer<bool>.Default.Equals(left.Notify, right.Notify) &&
                   EqualityComparer<StaticImageFormat>.Default.Equals(left.Format, right.Format) &&
                   EqualityComparer<int>.Default.Equals(left.Quality, right.Quality) &&
                   Same(left.OutputSize, right.OutputSize) &&
                   EqualityComparer<string>.Default.Equals(left.NameTemplate, right.NameTemplate) &&
                   EqualityComparer<ScreenshotConflictPolicy>.Default.Equals(left.ConflictPolicy, right.ConflictPolicy);
        }

        private static bool Same(AnnotationPreferences? left, AnnotationPreferences? right)
        {
            if (ReferenceEquals(left, right)) return true;
            if (left is null || right is null) return false;
            return EqualityComparer<string>.Default.Equals(left.Color, right.Color) &&
                   EqualityComparer<double>.Default.Equals(left.Thickness, right.Thickness) &&
                   EqualityComparer<double>.Default.Equals(left.FontSize, right.FontSize) &&
                   EqualityComparer<int>.Default.Equals(left.MosaicSize, right.MosaicSize) &&
                   EqualityComparer<bool>.Default.Equals(left.RememberLastTool, right.RememberLastTool) &&
                   EqualityComparer<bool>.Default.Equals(left.LimitToSelection, right.LimitToSelection) &&
                   EqualityComparer<string>.Default.Equals(left.LastTool, right.LastTool) &&
                   EqualityComparer<bool>.Default.Equals(left.SharedColor, right.SharedColor) &&
                   Same(left.Properties, right.Properties) &&
                   SameDictionary(left.ToolStyles, right.ToolStyles, static (a, b) => Same(a, b)) &&
                   SameSequence(left.ColorPalette, right.ColorPalette,
                       static (a, b) => EqualityComparer<string>.Default.Equals(a, b)) &&
                   EqualityComparer<bool>.Default.Equals(left.SecondaryEditingEnabled, right.SecondaryEditingEnabled) &&
                   EqualityComparer<bool>.Default.Equals(left.QuickSettingsEnabled, right.QuickSettingsEnabled) &&
                   EqualityComparer<AnnotationPencilCursor>.Default.Equals(left.PencilCursor, right.PencilCursor) &&
                   EqualityComparer<double>.Default.Equals(left.MaximumBrushThickness, right.MaximumBrushThickness) &&
                   EqualityComparer<bool>.Default.Equals(left.RememberPinnedTool, right.RememberPinnedTool) &&
                   EqualityComparer<string>.Default.Equals(left.PinnedLastTool, right.PinnedLastTool) &&
                   EqualityComparer<bool>.Default.Equals(left.DisableWheelToolAdjustment,
                       right.DisableWheelToolAdjustment) &&
                   EqualityComparer<double>.Default.Equals(left.QuickSettingsPadding, right.QuickSettingsPadding) &&
                   EqualityComparer<AnnotationControlPointVisibility>.Default.Equals(left.ControlPointVisibility,
                       right.ControlPointVisibility) &&
                   EqualityComparer<bool>.Default.Equals(left.RenumberOnDelete, right.RenumberOnDelete) &&
                   EqualityComparer<bool>.Default.Equals(left.NumberStartsAtZero, right.NumberStartsAtZero) &&
                   EqualityComparer<bool>.Default.Equals(left.GlobalNumberCounter, right.GlobalNumberCounter) &&
                   EqualityComparer<bool>.Default.Equals(left.SmoothBrush, right.SmoothBrush) &&
                   EqualityComparer<AnnotationPencilShiftMode>.Default.Equals(left.PencilShiftMode,
                       right.PencilShiftMode) &&
                   EqualityComparer<PenLineJoin>.Default.Equals(left.PolylineJoin, right.PolylineJoin) &&
                   EqualityComparer<PenLineCap>.Default.Equals(left.PolylineCap, right.PolylineCap) &&
                   EqualityComparer<bool>.Default.Equals(left.ArrowDoubleClickCommentEnabled,
                       right.ArrowDoubleClickCommentEnabled) &&
                   EqualityComparer<bool>.Default.Equals(left.ArrowCurveControlPoints, right.ArrowCurveControlPoints) &&
                   EqualityComparer<AnnotationTextWrapMode>.Default.Equals(left.TextWrapMode, right.TextWrapMode) &&
                   EqualityComparer<bool>.Default.Equals(left.EraserSecondaryEditingEnabled,
                       right.EraserSecondaryEditingEnabled) &&
                   EqualityComparer<bool>.Default.Equals(left.AutoSelectPrivacyMosaic, right.AutoSelectPrivacyMosaic) &&
                   Same(left.Toolbar, right.Toolbar);
        }

        private static bool Same(PinnedImagePreferences? left, PinnedImagePreferences? right)
        {
            if (ReferenceEquals(left, right)) return true;
            if (left is null || right is null) return false;
            return EqualityComparer<bool>.Default.Equals(left.Topmost, right.Topmost) &&
                   EqualityComparer<bool>.Default.Equals(left.ShowWindowShadow, right.ShowWindowShadow) &&
                   EqualityComparer<bool>.Default.Equals(left.AutoFit, right.AutoFit) &&
                   EqualityComparer<double>.Default.Equals(left.Opacity, right.Opacity) &&
                   EqualityComparer<double>.Default.Equals(left.ZoomStep, right.ZoomStep) &&
                   EqualityComparer<double>.Default.Equals(left.FineZoomStep, right.FineZoomStep) &&
                   EqualityComparer<double>.Default.Equals(left.OpacityStep, right.OpacityStep) &&
                   EqualityComparer<double>.Default.Equals(left.MouseThroughOpacity, right.MouseThroughOpacity) &&
                   EqualityComparer<bool>.Default.Equals(left.SelectableText, right.SelectableText) &&
                   EqualityComparer<bool>.Default.Equals(left.ThumbnailSelectableText, right.ThumbnailSelectableText) &&
                   EqualityComparer<bool>.Default.Equals(left.CopyVisibleRegion, right.CopyVisibleRegion) &&
                   EqualityComparer<bool>.Default.Equals(left.ResizeFromBorder, right.ResizeFromBorder) &&
                   EqualityComparer<bool>.Default.Equals(left.ThumbnailWheelZoom, right.ThumbnailWheelZoom) &&
                   EqualityComparer<bool>.Default.Equals(left.ShowMenuShortcuts, right.ShowMenuShortcuts) &&
                   EqualityComparer<bool>.Default.Equals(left.ShowLockIndicator, right.ShowLockIndicator) &&
                   EqualityComparer<PinnedLockedOperations>.Default.Equals(left.LockedOperations,
                       right.LockedOperations) &&
                   EqualityComparer<bool>.Default.Equals(left.ShowTaskbarWhenNotTopmost,
                       right.ShowTaskbarWhenNotTopmost) &&
                   EqualityComparer<int>.Default.Equals(left.HistoryLimit, right.HistoryLimit) &&
                   EqualityComparer<PinnedDuplicateBehavior>.Default.Equals(left.DuplicateBehavior,
                       right.DuplicateBehavior) &&
                   EqualityComparer<PinnedRestoreOrder>.Default.Equals(left.RestoreOrder, right.RestoreOrder) &&
                   EqualityComparer<PinnedZoomAnchor>.Default.Equals(left.WheelZoomAnchor, right.WheelZoomAnchor) &&
                   EqualityComparer<PinnedZoomAnchor>.Default.Equals(left.ResetZoomAnchor, right.ResetZoomAnchor);
        }

        private static bool Same(ScrollCapturePreferences? left, ScrollCapturePreferences? right)
        {
            if (ReferenceEquals(left, right)) return true;
            if (left is null || right is null) return false;
            return EqualityComparer<ScrollCaptureAxis>.Default.Equals(left.InitialAxis, right.InitialAxis) &&
                   EqualityComparer<bool>.Default.Equals(left.RespondShortcuts, right.RespondShortcuts);
        }

        private static bool Same(CapturePreferences? left, CapturePreferences? right)
        {
            if (ReferenceEquals(left, right)) return true;
            if (left is null || right is null) return false;
            return EqualityComparer<CaptureDetectionMode>.Default.Equals(left.DetectionMode, right.DetectionMode) &&
                   EqualityComparer<int>.Default.Equals(left.ElementMargin, right.ElementMargin) &&
                   EqualityComparer<bool>.Default.Equals(left.ExpandOnOutsideClick, right.ExpandOnOutsideClick) &&
                   EqualityComparer<CaptureCursorPolicy>.Default.Equals(left.CursorPolicy, right.CursorPolicy) &&
                   EqualityComparer<CaptureAuxiliaryPolicy>.Default.Equals(left.AuxiliaryPolicy,
                       right.AuxiliaryPolicy) &&
                   EqualityComparer<bool>.Default.Equals(left.GuideCursor, right.GuideCursor) &&
                   EqualityComparer<bool>.Default.Equals(left.GuideSelectionCenter, right.GuideSelectionCenter) &&
                   EqualityComparer<bool>.Default.Equals(left.GuideSelectionBorder, right.GuideSelectionBorder) &&
                   EqualityComparer<bool>.Default.Equals(left.GuideThirds, right.GuideThirds) &&
                   EqualityComparer<bool>.Default.Equals(left.GuideMagnifier, right.GuideMagnifier) &&
                   EqualityComparer<bool>.Default.Equals(left.ShowInformation, right.ShowInformation) &&
                   EqualityComparer<bool>.Default.Equals(left.ShowMagnifier, right.ShowMagnifier) &&
                   EqualityComparer<bool>.Default.Equals(left.ShowShortcutHints, right.ShowShortcutHints) &&
                   EqualityComparer<int>.Default.Equals(left.RegionHistoryCapacity, right.RegionHistoryCapacity) &&
                   EqualityComparer<int>.Default.Equals(left.ScreenshotHistoryCapacity,
                       right.ScreenshotHistoryCapacity) &&
                   EqualityComparer<int>.Default.Equals(left.ScreenshotHistoryMaximumMiB,
                       right.ScreenshotHistoryMaximumMiB) &&
                   EqualityComparer<int>.Default.Equals(left.Magnification, right.Magnification) &&
                   EqualityComparer<bool>.Default.Equals(left.MagnifierGrid, right.MagnifierGrid) &&
                   EqualityComparer<bool>.Default.Equals(left.MagnifierCrosshair, right.MagnifierCrosshair) &&
                   EqualityComparer<bool>.Default.Equals(left.MagnifierCenterPixel, right.MagnifierCenterPixel) &&
                   EqualityComparer<bool>.Default.Equals(left.MagnifierSelectionBorder,
                       right.MagnifierSelectionBorder) &&
                   EqualityComparer<bool>.Default.Equals(left.MagnifierSelectionHandles,
                       right.MagnifierSelectionHandles) &&
                   EqualityComparer<bool>.Default.Equals(left.MagnifierCoordinates, right.MagnifierCoordinates) &&
                   EqualityComparer<bool>.Default.Equals(left.MagnifierShortcuts, right.MagnifierShortcuts) &&
                   EqualityComparer<string>.Default.Equals(left.MagnifierGridColor, right.MagnifierGridColor) &&
                   EqualityComparer<CaptureSampleColorFormat>.Default.Equals(left.MagnifierColorFormat,
                       right.MagnifierColorFormat) &&
                   EqualityComparer<bool>.Default.Equals(left.SmoothMagnifier, right.SmoothMagnifier) &&
                   EqualityComparer<bool>.Default.Equals(left.KeepDecoration, right.KeepDecoration) &&
                   EqualityComparer<CaptureDecoration>.Default.Equals(left.RememberedDecoration,
                       right.RememberedDecoration) &&
                   EqualityComparer<bool>.Default.Equals(left.FrameRoundEnabled, right.FrameRoundEnabled) &&
                   EqualityComparer<double>.Default.Equals(left.FrameRoundRadius, right.FrameRoundRadius);
        }

        private static bool Same(HotkeyPolicyPreferences? left, HotkeyPolicyPreferences? right)
        {
            if (ReferenceEquals(left, right)) return true;
            if (left is null || right is null) return false;
            return EqualityComparer<bool>.Default.Equals(left.DisableInFullscreen, right.DisableInFullscreen) &&
                   SameSequence(left.IgnoredPrograms, right.IgnoredPrograms,
                       static (a, b) => EqualityComparer<string>.Default.Equals(a, b));
        }

        private static bool Same(GlobalActionBinding? left, GlobalActionBinding? right)
        {
            if (ReferenceEquals(left, right)) return true;
            if (left is null || right is null) return false;
            return EqualityComparer<string>.Default.Equals(left.Id, right.Id) &&
                   EqualityComparer<string>.Default.Equals(left.Name, right.Name) &&
                   EqualityComparer<GlobalActionKind>.Default.Equals(left.Kind, right.Kind) &&
                   EqualityComparer<string>.Default.Equals(left.Shortcut, right.Shortcut) &&
                   EqualityComparer<bool>.Default.Equals(left.ShowInTray, right.ShowInTray) &&
                   EqualityComparer<bool>.Default.Equals(left.Enabled, right.Enabled) &&
                   EqualityComparer<string>.Default.Equals(left.Parameter, right.Parameter) &&
                   EqualityComparer<string>.Default.Equals(left.PresetName, right.PresetName) &&
                   SameDictionary(left.Arguments, right.Arguments,
                       static (a, b) => EqualityComparer<string>.Default.Equals(a, b));
        }

        private static bool Same(GlobalMouseBinding? left, GlobalMouseBinding? right)
        {
            if (ReferenceEquals(left, right)) return true;
            if (left is null || right is null) return false;
            return EqualityComparer<string>.Default.Equals(left.ActionId, right.ActionId) &&
                   EqualityComparer<string>.Default.Equals(left.Gesture, right.Gesture) &&
                   EqualityComparer<bool>.Default.Equals(left.Enabled, right.Enabled);
        }

        private static bool Same(VideoRecordingPreferences? left, VideoRecordingPreferences? right)
        {
            if (ReferenceEquals(left, right)) return true;
            if (left is null || right is null) return false;
            return EqualityComparer<VideoContainer>.Default.Equals(left.Container, right.Container) &&
                   EqualityComparer<int>.Default.Equals(left.Fps, right.Fps) &&
                   EqualityComparer<int>.Default.Equals(left.Divisor, right.Divisor) &&
                   EqualityComparer<int>.Default.Equals(left.Quality, right.Quality) &&
                   EqualityComparer<bool>.Default.Equals(left.SystemAudio, right.SystemAudio) &&
                   EqualityComparer<bool>.Default.Equals(left.Microphone, right.Microphone) &&
                   EqualityComparer<string?>.Default.Equals(left.SystemDevice, right.SystemDevice) &&
                   EqualityComparer<string?>.Default.Equals(left.MicrophoneDevice, right.MicrophoneDevice) &&
                   EqualityComparer<string>.Default.Equals(left.OutputDirectory, right.OutputDirectory);
        }

        private static bool Same(SystemPreferences? left, SystemPreferences? right)
        {
            if (ReferenceEquals(left, right)) return true;
            if (left is null || right is null) return false;
            return EqualityComparer<bool>.Default.Equals(left.StartWithWindows, right.StartWithWindows) &&
                   EqualityComparer<bool>.Default.Equals(left.RunAsAdministrator, right.RunAsAdministrator) &&
                   EqualityComparer<SystemTrayAction>.Default.Equals(left.TraySingleClick, right.TraySingleClick) &&
                   EqualityComparer<SystemTrayAction>.Default.Equals(left.TrayDoubleClick, right.TrayDoubleClick) &&
                   EqualityComparer<SystemTrayAction>.Default.Equals(left.TrayMiddleClick, right.TrayMiddleClick) &&
                   EqualityComparer<UpdateChannel>.Default.Equals(left.UpdateChannel, right.UpdateChannel);
        }

        private static bool Same(RecognitionPreferences? left, RecognitionPreferences? right)
        {
            if (ReferenceEquals(left, right)) return true;
            if (left is null || right is null) return false;
            return EqualityComparer<RecognitionTextLayout>.Default.Equals(left.Layout, right.Layout) &&
                   EqualityComparer<RecognitionPunctuation>.Default.Equals(left.Punctuation, right.Punctuation) &&
                   EqualityComparer<bool>.Default.Equals(left.ShowDialogOnRecognition, right.ShowDialogOnRecognition) &&
                   EqualityComparer<bool>.Default.Equals(left.ShowDialogOnCopyAll, right.ShowDialogOnCopyAll) &&
                   EqualityComparer<string>.Default.Equals(left.LanguageTag, right.LanguageTag);
        }

        private static bool Same(ScreenshotOutputSize? left, ScreenshotOutputSize? right)
        {
            if (ReferenceEquals(left, right)) return true;
            if (left is null || right is null) return false;
            return EqualityComparer<ScreenshotOutputSizeMode>.Default.Equals(left.Mode, right.Mode) &&
                   EqualityComparer<double>.Default.Equals(left.Percent, right.Percent) &&
                   EqualityComparer<int>.Default.Equals(left.Width, right.Width) &&
                   EqualityComparer<int>.Default.Equals(left.Height, right.Height);
        }

        private static bool Same(ScreenshotAnnotationProperties? left, ScreenshotAnnotationProperties? right)
        {
            if (ReferenceEquals(left, right)) return true;
            if (left is null || right is null) return false;
            return EqualityComparer<bool>.Default.Equals(left.Filled, right.Filled) &&
                   EqualityComparer<bool>.Default.Equals(left.Dashed, right.Dashed) &&
                   EqualityComparer<bool>.Default.Equals(left.Bold, right.Bold) &&
                   EqualityComparer<bool>.Default.Equals(left.Italic, right.Italic) &&
                   EqualityComparer<bool>.Default.Equals(left.StrikeThrough, right.StrikeThrough) &&
                   EqualityComparer<Color>.Default.Equals(left.TextBackground, right.TextBackground) &&
                   EqualityComparer<Color>.Default.Equals(left.TextOutline, right.TextOutline) &&
                   EqualityComparer<double>.Default.Equals(left.TextOutlineWidth, right.TextOutlineWidth) &&
                   EqualityComparer<double>.Default.Equals(left.WrapWidth, right.WrapWidth) &&
                   EqualityComparer<double>.Default.Equals(left.TextPadding, right.TextPadding) &&
                   EqualityComparer<double>.Default.Equals(left.Opacity, right.Opacity) &&
                   EqualityComparer<double>.Default.Equals(left.Magnification, right.Magnification) &&
                   EqualityComparer<int>.Default.Equals(left.Number, right.Number) &&
                   EqualityComparer<bool>.Default.Equals(left.Tiled, right.Tiled) &&
                   EqualityComparer<bool>.Default.Equals(left.NineGrid, right.NineGrid) &&
                   EqualityComparer<int>.Default.Equals(left.Anchor, right.Anchor) &&
                   EqualityComparer<double>.Default.Equals(left.ImageWidth, right.ImageWidth) &&
                   EqualityComparer<int>.Default.Equals(left.BlurRadius, right.BlurRadius) &&
                   EqualityComparer<string>.Default.Equals(left.FontFamily, right.FontFamily);
        }

        private static bool Same(AnnotationToolStyle? left, AnnotationToolStyle? right)
        {
            if (ReferenceEquals(left, right)) return true;
            if (left is null || right is null) return false;
            return EqualityComparer<string>.Default.Equals(left.Color, right.Color) &&
                   EqualityComparer<double>.Default.Equals(left.Thickness, right.Thickness) &&
                   EqualityComparer<double>.Default.Equals(left.FontSize, right.FontSize) &&
                   EqualityComparer<int>.Default.Equals(left.MosaicSize, right.MosaicSize) &&
                   Same(left.Properties, right.Properties);
        }

        private static bool Same(ToolbarPreferences? left, ToolbarPreferences? right)
        {
            if (ReferenceEquals(left, right)) return true;
            if (left is null || right is null) return false;
            return SameSequence(left.Columns, right.Columns,
                       static (a, b) => SameSequence(a, b,
                           static (a, b) => EqualityComparer<ScreenshotTool>.Default.Equals(a, b))) &&
                   SameSequence(left.Actions, right.Actions, static (a, b) => Same(a, b));
        }

        private static bool Same(ToolbarActionPreference? left, ToolbarActionPreference? right)
        {
            if (ReferenceEquals(left, right)) return true;
            if (left is null || right is null) return false;
            return EqualityComparer<ToolbarActionId>.Default.Equals(left.Id, right.Id) &&
                   EqualityComparer<ToolbarButtonVisibility>.Default.Equals(left.Visibility, right.Visibility);
        }

        private static bool SameSequence<T>(IReadOnlyList<T>? left, IReadOnlyList<T>? right,
            Func<T, T, bool> equal)
        {
            if (ReferenceEquals(left, right)) return true;
            if (left is null || right is null || left.Count != right.Count) return false;
            for (var index = 0; index < left.Count; index++)
                if (!equal(left[index], right[index]))
                    return false;
            return true;
        }

        private static bool SameDictionary<T>(Dictionary<string, T>? left, Dictionary<string, T>? right,
            Func<T, T, bool> equal)
        {
            if (ReferenceEquals(left, right)) return true;
            if (left is null || right is null || left.Count != right.Count) return false;
            foreach (var (key, value) in left)
                if (!TryGetOrdinalValue(right, key, out var other) || !equal(value, other))
                    return false;
            return true;
        }

        private static bool TryGetOrdinalValue<T>(Dictionary<string, T> values, string key, out T value)
        {
            if (ReferenceEquals(values.Comparer, StringComparer.Ordinal) ||
                ReferenceEquals(values.Comparer, EqualityComparer<string>.Default))
                return values.TryGetValue(key, out value!);
            foreach (var pair in values)
            {
                if (!string.Equals(pair.Key, key, StringComparison.Ordinal)) continue;
                value = pair.Value;
                return true;
            }

            value = default!;
            return false;
        }
    }
}
