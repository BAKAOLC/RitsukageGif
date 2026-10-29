using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    public sealed partial record AppSettings
    {
        internal AppSettings MergeChanges(AppSettings baseline, AppSettings edited)
        {
            ArgumentNullException.ThrowIfNull(baseline);
            ArgumentNullException.ThrowIfNull(edited);
            if (Same(baseline, edited)) return this;
            var merged = Merge(this, baseline, edited);
            var result = merged.Snapshot();
            result.Annotation.Toolbar = merged.Annotation.Toolbar with
            {
                Columns = [.. merged.Annotation.Toolbar.Columns.Select(column => new List<ScreenshotTool>(column))],
                Actions = [.. merged.Annotation.Toolbar.Actions.Select(action => action with { })],
            };
            return result;
        }

        private static AppSettings Merge(AppSettings current, AppSettings baseline, AppSettings edited)
        {
            if (Same(baseline, edited)) return current;
            return current with
            {
                Appearance = Merge(current.Appearance, baseline.Appearance, edited.Appearance),
                Language = EqualityComparer<string>.Default.Equals(baseline.Language, edited.Language)
                    ? current.Language
                    : edited.Language,
                CleanupEnabled = EqualityComparer<bool>.Default.Equals(baseline.CleanupEnabled, edited.CleanupEnabled)
                    ? current.CleanupEnabled
                    : edited.CleanupEnabled,
                CleanupMaxAgeDays =
                EqualityComparer<int?>.Default.Equals(baseline.CleanupMaxAgeDays, edited.CleanupMaxAgeDays)
                    ? current.CleanupMaxAgeDays
                    : edited.CleanupMaxAgeDays,
                CleanupMaxFiles =
                EqualityComparer<int?>.Default.Equals(baseline.CleanupMaxFiles, edited.CleanupMaxFiles)
                    ? current.CleanupMaxFiles
                    : edited.CleanupMaxFiles,
                CleanupMaxSizeMiB =
                EqualityComparer<int?>.Default.Equals(baseline.CleanupMaxSizeMiB, edited.CleanupMaxSizeMiB)
                    ? current.CleanupMaxSizeMiB
                    : edited.CleanupMaxSizeMiB,
                CheckForUpdates =
                EqualityComparer<bool>.Default.Equals(baseline.CheckForUpdates, edited.CheckForUpdates)
                    ? current.CheckForUpdates
                    : edited.CheckForUpdates,
                CloseToTray = EqualityComparer<bool>.Default.Equals(baseline.CloseToTray, edited.CloseToTray)
                    ? current.CloseToTray
                    : edited.CloseToTray,
                MinimizeToTray = EqualityComparer<bool>.Default.Equals(baseline.MinimizeToTray, edited.MinimizeToTray)
                    ? current.MinimizeToTray
                    : edited.MinimizeToTray,
                RestorePinnedImages =
                EqualityComparer<bool>.Default.Equals(baseline.RestorePinnedImages, edited.RestorePinnedImages)
                    ? current.RestorePinnedImages
                    : edited.RestorePinnedImages,
                ScreenshotClipboard = Merge(current.ScreenshotClipboard, baseline.ScreenshotClipboard,
                    edited.ScreenshotClipboard),
                ScreenshotSave = Merge(current.ScreenshotSave, baseline.ScreenshotSave, edited.ScreenshotSave),
                Annotation = Merge(current.Annotation, baseline.Annotation, edited.Annotation),
                PinnedImages = Merge(current.PinnedImages, baseline.PinnedImages, edited.PinnedImages),
                PresetFiles =
                SameSequence(baseline.PresetFiles, edited.PresetFiles,
                    static (a, b) => EqualityComparer<string>.Default.Equals(a, b))
                    ? current.PresetFiles
                    : edited.PresetFiles,
                PresetHotkeys =
                SameSequence(baseline.PresetHotkeys, edited.PresetHotkeys,
                    static (a, b) => EqualityComparer<PresetHotkey>.Default.Equals(a, b))
                    ? current.PresetHotkeys
                    : edited.PresetHotkeys,
                ScrollCapture = Merge(current.ScrollCapture, baseline.ScrollCapture, edited.ScrollCapture),
                CapturePreferences = Merge(current.CapturePreferences, baseline.CapturePreferences,
                    edited.CapturePreferences),
                HotkeyPolicy = Merge(current.HotkeyPolicy, baseline.HotkeyPolicy, edited.HotkeyPolicy),
                GlobalActions = SameSequence(baseline.GlobalActions, edited.GlobalActions, static (a, b) => Same(a, b))
                    ? current.GlobalActions
                    : edited.GlobalActions,
                GlobalMouseBindings =
                SameSequence(baseline.GlobalMouseBindings, edited.GlobalMouseBindings, static (a, b) => Same(a, b))
                    ? current.GlobalMouseBindings
                    : edited.GlobalMouseBindings,
                VideoRecording = Merge(current.VideoRecording, baseline.VideoRecording, edited.VideoRecording),
                Fps = EqualityComparer<int>.Default.Equals(baseline.Fps, edited.Fps) ? current.Fps : edited.Fps,
                Divisor = EqualityComparer<int>.Default.Equals(baseline.Divisor, edited.Divisor)
                    ? current.Divisor
                    : edited.Divisor,
                Format = EqualityComparer<OutputFormat>.Default.Equals(baseline.Format, edited.Format)
                    ? current.Format
                    : edited.Format,
                CaptureColorMode =
                EqualityComparer<OutputColorMode>.Default.Equals(baseline.CaptureColorMode, edited.CaptureColorMode)
                    ? current.CaptureColorMode
                    : edited.CaptureColorMode,
                CaptureToneMap =
                EqualityComparer<HdrToneMapMode>.Default.Equals(baseline.CaptureToneMap, edited.CaptureToneMap)
                    ? current.CaptureToneMap
                    : edited.CaptureToneMap,
                Quality = EqualityComparer<GifQuality>.Default.Equals(baseline.Quality, edited.Quality)
                    ? current.Quality
                    : edited.Quality,
                UseFfmpeg = EqualityComparer<bool>.Default.Equals(baseline.UseFfmpeg, edited.UseFfmpeg)
                    ? current.UseFfmpeg
                    : edited.UseFfmpeg,
                Cursor = EqualityComparer<bool>.Default.Equals(baseline.Cursor, edited.Cursor)
                    ? current.Cursor
                    : edited.Cursor,
                CaptureSource =
                EqualityComparer<CaptureSourceKind>.Default.Equals(baseline.CaptureSource, edited.CaptureSource)
                    ? current.CaptureSource
                    : edited.CaptureSource,
                Sound = EqualityComparer<bool>.Default.Equals(baseline.Sound, edited.Sound)
                    ? current.Sound
                    : edited.Sound,
                StartSound = EqualityComparer<string>.Default.Equals(baseline.StartSound, edited.StartSound)
                    ? current.StartSound
                    : edited.StartSound,
                StopSound = EqualityComparer<string>.Default.Equals(baseline.StopSound, edited.StopSound)
                    ? current.StopSound
                    : edited.StopSound,
                BackgroundImage =
                EqualityComparer<string>.Default.Equals(baseline.BackgroundImage, edited.BackgroundImage)
                    ? current.BackgroundImage
                    : edited.BackgroundImage,
                ShowBackground = EqualityComparer<bool>.Default.Equals(baseline.ShowBackground, edited.ShowBackground)
                    ? current.ShowBackground
                    : edited.ShowBackground,
                SnapRadius = EqualityComparer<int>.Default.Equals(baseline.SnapRadius, edited.SnapRadius)
                    ? current.SnapRadius
                    : edited.SnapRadius,
                EdgeThreshold = EqualityComparer<int>.Default.Equals(baseline.EdgeThreshold, edited.EdgeThreshold)
                    ? current.EdgeThreshold
                    : edited.EdgeThreshold,
                SelectionDetectControls =
                EqualityComparer<bool>.Default.Equals(baseline.SelectionDetectControls, edited.SelectionDetectControls)
                    ? current.SelectionDetectControls
                    : edited.SelectionDetectControls,
                SelectionSnappingMode =
                EqualityComparer<int>.Default.Equals(baseline.SelectionSnappingMode, edited.SelectionSnappingMode)
                    ? current.SelectionSnappingMode
                    : edited.SelectionSnappingMode,
                SelectionHints =
                EqualityComparer<SelectionHintMode>.Default.Equals(baseline.SelectionHints, edited.SelectionHints)
                    ? current.SelectionHints
                    : edited.SelectionHints,
                OutputDirectory =
                EqualityComparer<string>.Default.Equals(baseline.OutputDirectory, edited.OutputDirectory)
                    ? current.OutputDirectory
                    : edited.OutputDirectory,
                SelectionShortcuts = MergeSettingDictionary(current.SelectionShortcuts, baseline.SelectionShortcuts,
                    edited.SelectionShortcuts,
                    static (a, b) => SameSequence(a, b, static (a, b) => EqualityComparer<string>.Default.Equals(a, b)),
                    static (_, _, e) => e),
                MouseShortcuts = MergeSettingDictionary(current.MouseShortcuts, baseline.MouseShortcuts,
                    edited.MouseShortcuts,
                    static (a, b) => SameSequence(a, b, static (a, b) => EqualityComparer<string>.Default.Equals(a, b)),
                    static (_, _, e) => e),
                System = Merge(current.System, baseline.System, edited.System),
                Recognition = Merge(current.Recognition, baseline.Recognition, edited.Recognition),
            };
        }

        private static AppearancePreferences Merge(AppearancePreferences current, AppearancePreferences baseline,
            AppearancePreferences edited)
        {
            if (Same(baseline, edited)) return current;
            return current with
            {
                ThemeMode = EqualityComparer<AppearanceThemeMode>.Default.Equals(baseline.ThemeMode, edited.ThemeMode)
                    ? current.ThemeMode
                    : edited.ThemeMode,
                FontFamily = EqualityComparer<string>.Default.Equals(baseline.FontFamily, edited.FontFamily)
                    ? current.FontFamily
                    : edited.FontFamily,
                AccentColor = EqualityComparer<uint>.Default.Equals(baseline.AccentColor, edited.AccentColor)
                    ? current.AccentColor
                    : edited.AccentColor,
                LockedWindowColor =
                EqualityComparer<uint>.Default.Equals(baseline.LockedWindowColor, edited.LockedWindowColor)
                    ? current.LockedWindowColor
                    : edited.LockedWindowColor,
                SelectionMaskColor =
                EqualityComparer<uint>.Default.Equals(baseline.SelectionMaskColor, edited.SelectionMaskColor)
                    ? current.SelectionMaskColor
                    : edited.SelectionMaskColor,
                ClickThroughShadeColor =
                EqualityComparer<uint>.Default.Equals(baseline.ClickThroughShadeColor, edited.ClickThroughShadeColor)
                    ? current.ClickThroughShadeColor
                    : edited.ClickThroughShadeColor,
                TrayIcon = EqualityComparer<TrayIconAppearance>.Default.Equals(baseline.TrayIcon, edited.TrayIcon)
                    ? current.TrayIcon
                    : edited.TrayIcon,
                TrayIconColor = EqualityComparer<uint>.Default.Equals(baseline.TrayIconColor, edited.TrayIconColor)
                    ? current.TrayIconColor
                    : edited.TrayIconColor,
                ToolbarIconSize =
                EqualityComparer<ToolbarIconSize>.Default.Equals(baseline.ToolbarIconSize, edited.ToolbarIconSize)
                    ? current.ToolbarIconSize
                    : edited.ToolbarIconSize,
                ColorDialogStyle =
                EqualityComparer<AppearanceDialogStyle>.Default.Equals(baseline.ColorDialogStyle,
                    edited.ColorDialogStyle)
                    ? current.ColorDialogStyle
                    : edited.ColorDialogStyle,
                SaveDialogStyle =
                EqualityComparer<AppearanceDialogStyle>.Default.Equals(baseline.SaveDialogStyle, edited.SaveDialogStyle)
                    ? current.SaveDialogStyle
                    : edited.SaveDialogStyle,
                SelectionBorderWidth =
                EqualityComparer<double>.Default.Equals(baseline.SelectionBorderWidth, edited.SelectionBorderWidth)
                    ? current.SelectionBorderWidth
                    : edited.SelectionBorderWidth,
                SelectionBorderColor =
                EqualityComparer<uint?>.Default.Equals(baseline.SelectionBorderColor, edited.SelectionBorderColor)
                    ? current.SelectionBorderColor
                    : edited.SelectionBorderColor,
                SelectionAnchors =
                EqualityComparer<SelectionAnchorMode>.Default.Equals(baseline.SelectionAnchors, edited.SelectionAnchors)
                    ? current.SelectionAnchors
                    : edited.SelectionAnchors,
                AnimateSelection =
                EqualityComparer<bool>.Default.Equals(baseline.AnimateSelection, edited.AnimateSelection)
                    ? current.AnimateSelection
                    : edited.AnimateSelection,
            };
        }

        private static ScreenshotClipboardPreferences Merge(ScreenshotClipboardPreferences current,
            ScreenshotClipboardPreferences baseline, ScreenshotClipboardPreferences edited)
        {
            if (Same(baseline, edited)) return current;
            return current with
            {
                Mode = EqualityComparer<ScreenshotClipboardMode>.Default.Equals(baseline.Mode, edited.Mode)
                    ? current.Mode
                    : edited.Mode,
                FileFormat =
                EqualityComparer<ScreenshotClipboardFileFormat>.Default.Equals(baseline.FileFormat, edited.FileFormat)
                    ? current.FileFormat
                    : edited.FileFormat,
                OutputSize = Merge(current.OutputSize, baseline.OutputSize, edited.OutputSize),
                HdpiTargetPercent =
                EqualityComparer<int>.Default.Equals(baseline.HdpiTargetPercent, edited.HdpiTargetPercent)
                    ? current.HdpiTargetPercent
                    : edited.HdpiTargetPercent,
                PasteAfterCopy = EqualityComparer<bool>.Default.Equals(baseline.PasteAfterCopy, edited.PasteAfterCopy)
                    ? current.PasteAfterCopy
                    : edited.PasteAfterCopy,
            };
        }

        private static ScreenshotSavePreferences Merge(ScreenshotSavePreferences current,
            ScreenshotSavePreferences baseline, ScreenshotSavePreferences edited)
        {
            if (Same(baseline, edited)) return current;
            return current with
            {
                Directory = EqualityComparer<string>.Default.Equals(baseline.Directory, edited.Directory)
                    ? current.Directory
                    : edited.Directory,
                AfterSave =
                EqualityComparer<ScreenshotAfterSaveAction>.Default.Equals(baseline.AfterSave, edited.AfterSave)
                    ? current.AfterSave
                    : edited.AfterSave,
                Notify = EqualityComparer<bool>.Default.Equals(baseline.Notify, edited.Notify)
                    ? current.Notify
                    : edited.Notify,
                Format = EqualityComparer<StaticImageFormat>.Default.Equals(baseline.Format, edited.Format)
                    ? current.Format
                    : edited.Format,
                Quality = EqualityComparer<int>.Default.Equals(baseline.Quality, edited.Quality)
                    ? current.Quality
                    : edited.Quality,
                OutputSize = Merge(current.OutputSize, baseline.OutputSize, edited.OutputSize),
                NameTemplate = EqualityComparer<string>.Default.Equals(baseline.NameTemplate, edited.NameTemplate)
                    ? current.NameTemplate
                    : edited.NameTemplate,
                ConflictPolicy =
                EqualityComparer<ScreenshotConflictPolicy>.Default.Equals(baseline.ConflictPolicy,
                    edited.ConflictPolicy)
                    ? current.ConflictPolicy
                    : edited.ConflictPolicy,
                Quick = Merge(current.Quick, baseline.Quick, edited.Quick),
                Automatic = Merge(current.Automatic, baseline.Automatic, edited.Automatic),
                AutoSaveTriggers =
                EqualityComparer<ScreenshotAutoSaveTriggers>.Default.Equals(baseline.AutoSaveTriggers,
                    edited.AutoSaveTriggers)
                    ? current.AutoSaveTriggers
                    : edited.AutoSaveTriggers,
                RememberExtension =
                EqualityComparer<bool>.Default.Equals(baseline.RememberExtension, edited.RememberExtension)
                    ? current.RememberExtension
                    : edited.RememberExtension,
                LastFormat = EqualityComparer<StaticImageFormat?>.Default.Equals(baseline.LastFormat, edited.LastFormat)
                    ? current.LastFormat
                    : edited.LastFormat,
                LastExtension = EqualityComparer<string?>.Default.Equals(baseline.LastExtension, edited.LastExtension)
                    ? current.LastExtension
                    : edited.LastExtension,
            };
        }

        private static ScreenshotSaveProfile Merge(ScreenshotSaveProfile current, ScreenshotSaveProfile baseline,
            ScreenshotSaveProfile edited)
        {
            if (Same(baseline, edited)) return current;
            return current with
            {
                Directory = EqualityComparer<string>.Default.Equals(baseline.Directory, edited.Directory)
                    ? current.Directory
                    : edited.Directory,
                AfterSave =
                EqualityComparer<ScreenshotAfterSaveAction>.Default.Equals(baseline.AfterSave, edited.AfterSave)
                    ? current.AfterSave
                    : edited.AfterSave,
                Notify = EqualityComparer<bool>.Default.Equals(baseline.Notify, edited.Notify)
                    ? current.Notify
                    : edited.Notify,
                Format = EqualityComparer<StaticImageFormat>.Default.Equals(baseline.Format, edited.Format)
                    ? current.Format
                    : edited.Format,
                Quality = EqualityComparer<int>.Default.Equals(baseline.Quality, edited.Quality)
                    ? current.Quality
                    : edited.Quality,
                OutputSize = Merge(current.OutputSize, baseline.OutputSize, edited.OutputSize),
                NameTemplate = EqualityComparer<string>.Default.Equals(baseline.NameTemplate, edited.NameTemplate)
                    ? current.NameTemplate
                    : edited.NameTemplate,
                ConflictPolicy =
                EqualityComparer<ScreenshotConflictPolicy>.Default.Equals(baseline.ConflictPolicy,
                    edited.ConflictPolicy)
                    ? current.ConflictPolicy
                    : edited.ConflictPolicy,
            };
        }

        private static AnnotationPreferences Merge(AnnotationPreferences current, AnnotationPreferences baseline,
            AnnotationPreferences edited)
        {
            if (Same(baseline, edited)) return current;
            return current with
            {
                Color = EqualityComparer<string>.Default.Equals(baseline.Color, edited.Color)
                    ? current.Color
                    : edited.Color,
                Thickness = EqualityComparer<double>.Default.Equals(baseline.Thickness, edited.Thickness)
                    ? current.Thickness
                    : edited.Thickness,
                FontSize = EqualityComparer<double>.Default.Equals(baseline.FontSize, edited.FontSize)
                    ? current.FontSize
                    : edited.FontSize,
                MosaicSize = EqualityComparer<int>.Default.Equals(baseline.MosaicSize, edited.MosaicSize)
                    ? current.MosaicSize
                    : edited.MosaicSize,
                RememberLastTool =
                EqualityComparer<bool>.Default.Equals(baseline.RememberLastTool, edited.RememberLastTool)
                    ? current.RememberLastTool
                    : edited.RememberLastTool,
                LimitToSelection =
                EqualityComparer<bool>.Default.Equals(baseline.LimitToSelection, edited.LimitToSelection)
                    ? current.LimitToSelection
                    : edited.LimitToSelection,
                LastTool = EqualityComparer<string>.Default.Equals(baseline.LastTool, edited.LastTool)
                    ? current.LastTool
                    : edited.LastTool,
                SharedColor = EqualityComparer<bool>.Default.Equals(baseline.SharedColor, edited.SharedColor)
                    ? current.SharedColor
                    : edited.SharedColor,
                Properties = Merge(current.Properties, baseline.Properties, edited.Properties),
                ToolStyles = MergeSettingDictionary(current.ToolStyles, baseline.ToolStyles, edited.ToolStyles,
                    static (a, b) => Same(a, b), static (c, b, e) => Merge(c, b, e)),
                ColorPalette =
                SameSequence(baseline.ColorPalette, edited.ColorPalette,
                    static (a, b) => EqualityComparer<string>.Default.Equals(a, b))
                    ? current.ColorPalette
                    : edited.ColorPalette,
                SecondaryEditingEnabled =
                EqualityComparer<bool>.Default.Equals(baseline.SecondaryEditingEnabled, edited.SecondaryEditingEnabled)
                    ? current.SecondaryEditingEnabled
                    : edited.SecondaryEditingEnabled,
                QuickSettingsEnabled =
                EqualityComparer<bool>.Default.Equals(baseline.QuickSettingsEnabled, edited.QuickSettingsEnabled)
                    ? current.QuickSettingsEnabled
                    : edited.QuickSettingsEnabled,
                PencilCursor =
                EqualityComparer<AnnotationPencilCursor>.Default.Equals(baseline.PencilCursor, edited.PencilCursor)
                    ? current.PencilCursor
                    : edited.PencilCursor,
                MaximumBrushThickness =
                EqualityComparer<double>.Default.Equals(baseline.MaximumBrushThickness, edited.MaximumBrushThickness)
                    ? current.MaximumBrushThickness
                    : edited.MaximumBrushThickness,
                RememberPinnedTool =
                EqualityComparer<bool>.Default.Equals(baseline.RememberPinnedTool, edited.RememberPinnedTool)
                    ? current.RememberPinnedTool
                    : edited.RememberPinnedTool,
                PinnedLastTool = EqualityComparer<string>.Default.Equals(baseline.PinnedLastTool, edited.PinnedLastTool)
                    ? current.PinnedLastTool
                    : edited.PinnedLastTool,
                DisableWheelToolAdjustment =
                EqualityComparer<bool>.Default.Equals(baseline.DisableWheelToolAdjustment,
                    edited.DisableWheelToolAdjustment)
                    ? current.DisableWheelToolAdjustment
                    : edited.DisableWheelToolAdjustment,
                QuickSettingsPadding =
                EqualityComparer<double>.Default.Equals(baseline.QuickSettingsPadding, edited.QuickSettingsPadding)
                    ? current.QuickSettingsPadding
                    : edited.QuickSettingsPadding,
                ControlPointVisibility =
                EqualityComparer<AnnotationControlPointVisibility>.Default.Equals(baseline.ControlPointVisibility,
                    edited.ControlPointVisibility)
                    ? current.ControlPointVisibility
                    : edited.ControlPointVisibility,
                RenumberOnDelete =
                EqualityComparer<bool>.Default.Equals(baseline.RenumberOnDelete, edited.RenumberOnDelete)
                    ? current.RenumberOnDelete
                    : edited.RenumberOnDelete,
                NumberStartsAtZero =
                EqualityComparer<bool>.Default.Equals(baseline.NumberStartsAtZero, edited.NumberStartsAtZero)
                    ? current.NumberStartsAtZero
                    : edited.NumberStartsAtZero,
                GlobalNumberCounter =
                EqualityComparer<bool>.Default.Equals(baseline.GlobalNumberCounter, edited.GlobalNumberCounter)
                    ? current.GlobalNumberCounter
                    : edited.GlobalNumberCounter,
                SmoothBrush = EqualityComparer<bool>.Default.Equals(baseline.SmoothBrush, edited.SmoothBrush)
                    ? current.SmoothBrush
                    : edited.SmoothBrush,
                PencilShiftMode =
                EqualityComparer<AnnotationPencilShiftMode>.Default.Equals(baseline.PencilShiftMode,
                    edited.PencilShiftMode)
                    ? current.PencilShiftMode
                    : edited.PencilShiftMode,
                PolylineJoin = EqualityComparer<PenLineJoin>.Default.Equals(baseline.PolylineJoin, edited.PolylineJoin)
                    ? current.PolylineJoin
                    : edited.PolylineJoin,
                PolylineCap = EqualityComparer<PenLineCap>.Default.Equals(baseline.PolylineCap, edited.PolylineCap)
                    ? current.PolylineCap
                    : edited.PolylineCap,
                ArrowDoubleClickCommentEnabled =
                EqualityComparer<bool>.Default.Equals(baseline.ArrowDoubleClickCommentEnabled,
                    edited.ArrowDoubleClickCommentEnabled)
                    ? current.ArrowDoubleClickCommentEnabled
                    : edited.ArrowDoubleClickCommentEnabled,
                ArrowCurveControlPoints =
                EqualityComparer<bool>.Default.Equals(baseline.ArrowCurveControlPoints, edited.ArrowCurveControlPoints)
                    ? current.ArrowCurveControlPoints
                    : edited.ArrowCurveControlPoints,
                TextWrapMode =
                EqualityComparer<AnnotationTextWrapMode>.Default.Equals(baseline.TextWrapMode, edited.TextWrapMode)
                    ? current.TextWrapMode
                    : edited.TextWrapMode,
                EraserSecondaryEditingEnabled =
                EqualityComparer<bool>.Default.Equals(baseline.EraserSecondaryEditingEnabled,
                    edited.EraserSecondaryEditingEnabled)
                    ? current.EraserSecondaryEditingEnabled
                    : edited.EraserSecondaryEditingEnabled,
                AutoSelectPrivacyMosaic =
                EqualityComparer<bool>.Default.Equals(baseline.AutoSelectPrivacyMosaic, edited.AutoSelectPrivacyMosaic)
                    ? current.AutoSelectPrivacyMosaic
                    : edited.AutoSelectPrivacyMosaic,
                Toolbar = Merge(current.Toolbar, baseline.Toolbar, edited.Toolbar),
            };
        }

        private static PinnedImagePreferences Merge(PinnedImagePreferences current, PinnedImagePreferences baseline,
            PinnedImagePreferences edited)
        {
            if (Same(baseline, edited)) return current;
            return current with
            {
                Topmost = EqualityComparer<bool>.Default.Equals(baseline.Topmost, edited.Topmost)
                    ? current.Topmost
                    : edited.Topmost,
                ShowWindowShadow =
                EqualityComparer<bool>.Default.Equals(baseline.ShowWindowShadow, edited.ShowWindowShadow)
                    ? current.ShowWindowShadow
                    : edited.ShowWindowShadow,
                AutoFit = EqualityComparer<bool>.Default.Equals(baseline.AutoFit, edited.AutoFit)
                    ? current.AutoFit
                    : edited.AutoFit,
                Opacity = EqualityComparer<double>.Default.Equals(baseline.Opacity, edited.Opacity)
                    ? current.Opacity
                    : edited.Opacity,
                ZoomStep = EqualityComparer<double>.Default.Equals(baseline.ZoomStep, edited.ZoomStep)
                    ? current.ZoomStep
                    : edited.ZoomStep,
                FineZoomStep = EqualityComparer<double>.Default.Equals(baseline.FineZoomStep, edited.FineZoomStep)
                    ? current.FineZoomStep
                    : edited.FineZoomStep,
                OpacityStep = EqualityComparer<double>.Default.Equals(baseline.OpacityStep, edited.OpacityStep)
                    ? current.OpacityStep
                    : edited.OpacityStep,
                MouseThroughOpacity =
                EqualityComparer<double>.Default.Equals(baseline.MouseThroughOpacity, edited.MouseThroughOpacity)
                    ? current.MouseThroughOpacity
                    : edited.MouseThroughOpacity,
                SelectableText = EqualityComparer<bool>.Default.Equals(baseline.SelectableText, edited.SelectableText)
                    ? current.SelectableText
                    : edited.SelectableText,
                ThumbnailSelectableText =
                EqualityComparer<bool>.Default.Equals(baseline.ThumbnailSelectableText, edited.ThumbnailSelectableText)
                    ? current.ThumbnailSelectableText
                    : edited.ThumbnailSelectableText,
                CopyVisibleRegion =
                EqualityComparer<bool>.Default.Equals(baseline.CopyVisibleRegion, edited.CopyVisibleRegion)
                    ? current.CopyVisibleRegion
                    : edited.CopyVisibleRegion,
                ResizeFromBorder =
                EqualityComparer<bool>.Default.Equals(baseline.ResizeFromBorder, edited.ResizeFromBorder)
                    ? current.ResizeFromBorder
                    : edited.ResizeFromBorder,
                ThumbnailWheelZoom =
                EqualityComparer<bool>.Default.Equals(baseline.ThumbnailWheelZoom, edited.ThumbnailWheelZoom)
                    ? current.ThumbnailWheelZoom
                    : edited.ThumbnailWheelZoom,
                ShowMenuShortcuts =
                EqualityComparer<bool>.Default.Equals(baseline.ShowMenuShortcuts, edited.ShowMenuShortcuts)
                    ? current.ShowMenuShortcuts
                    : edited.ShowMenuShortcuts,
                ShowLockIndicator =
                EqualityComparer<bool>.Default.Equals(baseline.ShowLockIndicator, edited.ShowLockIndicator)
                    ? current.ShowLockIndicator
                    : edited.ShowLockIndicator,
                LockedOperations =
                EqualityComparer<PinnedLockedOperations>.Default.Equals(baseline.LockedOperations,
                    edited.LockedOperations)
                    ? current.LockedOperations
                    : edited.LockedOperations,
                ShowTaskbarWhenNotTopmost =
                EqualityComparer<bool>.Default.Equals(baseline.ShowTaskbarWhenNotTopmost,
                    edited.ShowTaskbarWhenNotTopmost)
                    ? current.ShowTaskbarWhenNotTopmost
                    : edited.ShowTaskbarWhenNotTopmost,
                HistoryLimit = EqualityComparer<int>.Default.Equals(baseline.HistoryLimit, edited.HistoryLimit)
                    ? current.HistoryLimit
                    : edited.HistoryLimit,
                DuplicateBehavior =
                EqualityComparer<PinnedDuplicateBehavior>.Default.Equals(baseline.DuplicateBehavior,
                    edited.DuplicateBehavior)
                    ? current.DuplicateBehavior
                    : edited.DuplicateBehavior,
                RestoreOrder =
                EqualityComparer<PinnedRestoreOrder>.Default.Equals(baseline.RestoreOrder, edited.RestoreOrder)
                    ? current.RestoreOrder
                    : edited.RestoreOrder,
                WheelZoomAnchor =
                EqualityComparer<PinnedZoomAnchor>.Default.Equals(baseline.WheelZoomAnchor, edited.WheelZoomAnchor)
                    ? current.WheelZoomAnchor
                    : edited.WheelZoomAnchor,
                ResetZoomAnchor =
                EqualityComparer<PinnedZoomAnchor>.Default.Equals(baseline.ResetZoomAnchor, edited.ResetZoomAnchor)
                    ? current.ResetZoomAnchor
                    : edited.ResetZoomAnchor,
            };
        }

        private static ScrollCapturePreferences Merge(ScrollCapturePreferences current,
            ScrollCapturePreferences baseline, ScrollCapturePreferences edited)
        {
            if (Same(baseline, edited)) return current;
            return current with
            {
                InitialAxis =
                EqualityComparer<ScrollCaptureAxis>.Default.Equals(baseline.InitialAxis, edited.InitialAxis)
                    ? current.InitialAxis
                    : edited.InitialAxis,
                RespondShortcuts =
                EqualityComparer<bool>.Default.Equals(baseline.RespondShortcuts, edited.RespondShortcuts)
                    ? current.RespondShortcuts
                    : edited.RespondShortcuts,
            };
        }

        private static CapturePreferences Merge(CapturePreferences current, CapturePreferences baseline,
            CapturePreferences edited)
        {
            if (Same(baseline, edited)) return current;
            return current with
            {
                DetectionMode =
                EqualityComparer<CaptureDetectionMode>.Default.Equals(baseline.DetectionMode, edited.DetectionMode)
                    ? current.DetectionMode
                    : edited.DetectionMode,
                ElementMargin = EqualityComparer<int>.Default.Equals(baseline.ElementMargin, edited.ElementMargin)
                    ? current.ElementMargin
                    : edited.ElementMargin,
                ExpandOnOutsideClick =
                EqualityComparer<bool>.Default.Equals(baseline.ExpandOnOutsideClick, edited.ExpandOnOutsideClick)
                    ? current.ExpandOnOutsideClick
                    : edited.ExpandOnOutsideClick,
                CursorPolicy =
                EqualityComparer<CaptureCursorPolicy>.Default.Equals(baseline.CursorPolicy, edited.CursorPolicy)
                    ? current.CursorPolicy
                    : edited.CursorPolicy,
                AuxiliaryPolicy =
                EqualityComparer<CaptureAuxiliaryPolicy>.Default.Equals(baseline.AuxiliaryPolicy,
                    edited.AuxiliaryPolicy)
                    ? current.AuxiliaryPolicy
                    : edited.AuxiliaryPolicy,
                GuideCursor = EqualityComparer<bool>.Default.Equals(baseline.GuideCursor, edited.GuideCursor)
                    ? current.GuideCursor
                    : edited.GuideCursor,
                GuideSelectionCenter =
                EqualityComparer<bool>.Default.Equals(baseline.GuideSelectionCenter, edited.GuideSelectionCenter)
                    ? current.GuideSelectionCenter
                    : edited.GuideSelectionCenter,
                GuideSelectionBorder =
                EqualityComparer<bool>.Default.Equals(baseline.GuideSelectionBorder, edited.GuideSelectionBorder)
                    ? current.GuideSelectionBorder
                    : edited.GuideSelectionBorder,
                GuideThirds = EqualityComparer<bool>.Default.Equals(baseline.GuideThirds, edited.GuideThirds)
                    ? current.GuideThirds
                    : edited.GuideThirds,
                GuideMagnifier = EqualityComparer<bool>.Default.Equals(baseline.GuideMagnifier, edited.GuideMagnifier)
                    ? current.GuideMagnifier
                    : edited.GuideMagnifier,
                ShowInformation =
                EqualityComparer<bool>.Default.Equals(baseline.ShowInformation, edited.ShowInformation)
                    ? current.ShowInformation
                    : edited.ShowInformation,
                ShowMagnifier = EqualityComparer<bool>.Default.Equals(baseline.ShowMagnifier, edited.ShowMagnifier)
                    ? current.ShowMagnifier
                    : edited.ShowMagnifier,
                ShowShortcutHints =
                EqualityComparer<bool>.Default.Equals(baseline.ShowShortcutHints, edited.ShowShortcutHints)
                    ? current.ShowShortcutHints
                    : edited.ShowShortcutHints,
                RegionHistoryCapacity =
                EqualityComparer<int>.Default.Equals(baseline.RegionHistoryCapacity, edited.RegionHistoryCapacity)
                    ? current.RegionHistoryCapacity
                    : edited.RegionHistoryCapacity,
                ScreenshotHistoryCapacity =
                EqualityComparer<int>.Default.Equals(baseline.ScreenshotHistoryCapacity,
                    edited.ScreenshotHistoryCapacity)
                    ? current.ScreenshotHistoryCapacity
                    : edited.ScreenshotHistoryCapacity,
                ScreenshotHistoryMaximumMiB =
                EqualityComparer<int>.Default.Equals(baseline.ScreenshotHistoryMaximumMiB,
                    edited.ScreenshotHistoryMaximumMiB)
                    ? current.ScreenshotHistoryMaximumMiB
                    : edited.ScreenshotHistoryMaximumMiB,
                Magnification = EqualityComparer<int>.Default.Equals(baseline.Magnification, edited.Magnification)
                    ? current.Magnification
                    : edited.Magnification,
                MagnifierGrid = EqualityComparer<bool>.Default.Equals(baseline.MagnifierGrid, edited.MagnifierGrid)
                    ? current.MagnifierGrid
                    : edited.MagnifierGrid,
                MagnifierCrosshair =
                EqualityComparer<bool>.Default.Equals(baseline.MagnifierCrosshair, edited.MagnifierCrosshair)
                    ? current.MagnifierCrosshair
                    : edited.MagnifierCrosshair,
                MagnifierCenterPixel =
                EqualityComparer<bool>.Default.Equals(baseline.MagnifierCenterPixel, edited.MagnifierCenterPixel)
                    ? current.MagnifierCenterPixel
                    : edited.MagnifierCenterPixel,
                MagnifierSelectionBorder =
                EqualityComparer<bool>.Default.Equals(baseline.MagnifierSelectionBorder,
                    edited.MagnifierSelectionBorder)
                    ? current.MagnifierSelectionBorder
                    : edited.MagnifierSelectionBorder,
                MagnifierSelectionHandles =
                EqualityComparer<bool>.Default.Equals(baseline.MagnifierSelectionHandles,
                    edited.MagnifierSelectionHandles)
                    ? current.MagnifierSelectionHandles
                    : edited.MagnifierSelectionHandles,
                MagnifierCoordinates =
                EqualityComparer<bool>.Default.Equals(baseline.MagnifierCoordinates, edited.MagnifierCoordinates)
                    ? current.MagnifierCoordinates
                    : edited.MagnifierCoordinates,
                MagnifierShortcuts =
                EqualityComparer<bool>.Default.Equals(baseline.MagnifierShortcuts, edited.MagnifierShortcuts)
                    ? current.MagnifierShortcuts
                    : edited.MagnifierShortcuts,
                MagnifierGridColor =
                EqualityComparer<string>.Default.Equals(baseline.MagnifierGridColor, edited.MagnifierGridColor)
                    ? current.MagnifierGridColor
                    : edited.MagnifierGridColor,
                MagnifierColorFormat =
                EqualityComparer<CaptureSampleColorFormat>.Default.Equals(baseline.MagnifierColorFormat,
                    edited.MagnifierColorFormat)
                    ? current.MagnifierColorFormat
                    : edited.MagnifierColorFormat,
                SmoothMagnifier =
                EqualityComparer<bool>.Default.Equals(baseline.SmoothMagnifier, edited.SmoothMagnifier)
                    ? current.SmoothMagnifier
                    : edited.SmoothMagnifier,
                KeepDecoration = EqualityComparer<bool>.Default.Equals(baseline.KeepDecoration, edited.KeepDecoration)
                    ? current.KeepDecoration
                    : edited.KeepDecoration,
                RememberedDecoration =
                EqualityComparer<CaptureDecoration>.Default.Equals(baseline.RememberedDecoration,
                    edited.RememberedDecoration)
                    ? current.RememberedDecoration
                    : edited.RememberedDecoration,
                FrameRoundEnabled =
                EqualityComparer<bool>.Default.Equals(baseline.FrameRoundEnabled, edited.FrameRoundEnabled)
                    ? current.FrameRoundEnabled
                    : edited.FrameRoundEnabled,
                FrameRoundRadius =
                EqualityComparer<double>.Default.Equals(baseline.FrameRoundRadius, edited.FrameRoundRadius)
                    ? current.FrameRoundRadius
                    : edited.FrameRoundRadius,
            };
        }

        private static HotkeyPolicyPreferences Merge(HotkeyPolicyPreferences current, HotkeyPolicyPreferences baseline,
            HotkeyPolicyPreferences edited)
        {
            if (Same(baseline, edited)) return current;
            return current with
            {
                DisableInFullscreen =
                EqualityComparer<bool>.Default.Equals(baseline.DisableInFullscreen, edited.DisableInFullscreen)
                    ? current.DisableInFullscreen
                    : edited.DisableInFullscreen,
                IgnoredPrograms =
                SameSequence(baseline.IgnoredPrograms, edited.IgnoredPrograms,
                    static (a, b) => EqualityComparer<string>.Default.Equals(a, b))
                    ? current.IgnoredPrograms
                    : edited.IgnoredPrograms,
            };
        }

        private static VideoRecordingPreferences Merge(VideoRecordingPreferences current,
            VideoRecordingPreferences baseline, VideoRecordingPreferences edited)
        {
            if (Same(baseline, edited)) return current;
            return new()
            {
                Container = EqualityComparer<VideoContainer>.Default.Equals(baseline.Container, edited.Container)
                    ? current.Container
                    : edited.Container,
                Fps = EqualityComparer<int>.Default.Equals(baseline.Fps, edited.Fps) ? current.Fps : edited.Fps,
                Divisor = EqualityComparer<int>.Default.Equals(baseline.Divisor, edited.Divisor)
                    ? current.Divisor
                    : edited.Divisor,
                Quality = EqualityComparer<int>.Default.Equals(baseline.Quality, edited.Quality)
                    ? current.Quality
                    : edited.Quality,
                SystemAudio = EqualityComparer<bool>.Default.Equals(baseline.SystemAudio, edited.SystemAudio)
                    ? current.SystemAudio
                    : edited.SystemAudio,
                Microphone = EqualityComparer<bool>.Default.Equals(baseline.Microphone, edited.Microphone)
                    ? current.Microphone
                    : edited.Microphone,
                SystemDevice = EqualityComparer<string?>.Default.Equals(baseline.SystemDevice, edited.SystemDevice)
                    ? current.SystemDevice
                    : edited.SystemDevice,
                MicrophoneDevice =
                    EqualityComparer<string?>.Default.Equals(baseline.MicrophoneDevice, edited.MicrophoneDevice)
                        ? current.MicrophoneDevice
                        : edited.MicrophoneDevice,
                OutputDirectory =
                    EqualityComparer<string>.Default.Equals(baseline.OutputDirectory, edited.OutputDirectory)
                        ? current.OutputDirectory
                        : edited.OutputDirectory,
            };
        }

        private static SystemPreferences Merge(SystemPreferences current, SystemPreferences baseline,
            SystemPreferences edited)
        {
            if (Same(baseline, edited)) return current;
            return new()
            {
                StartWithWindows =
                    EqualityComparer<bool>.Default.Equals(baseline.StartWithWindows, edited.StartWithWindows)
                        ? current.StartWithWindows
                        : edited.StartWithWindows,
                RunAsAdministrator =
                    EqualityComparer<bool>.Default.Equals(baseline.RunAsAdministrator, edited.RunAsAdministrator)
                        ? current.RunAsAdministrator
                        : edited.RunAsAdministrator,
                TraySingleClick =
                    EqualityComparer<SystemTrayAction>.Default.Equals(baseline.TraySingleClick, edited.TraySingleClick)
                        ? current.TraySingleClick
                        : edited.TraySingleClick,
                TrayDoubleClick =
                    EqualityComparer<SystemTrayAction>.Default.Equals(baseline.TrayDoubleClick, edited.TrayDoubleClick)
                        ? current.TrayDoubleClick
                        : edited.TrayDoubleClick,
                TrayMiddleClick =
                    EqualityComparer<SystemTrayAction>.Default.Equals(baseline.TrayMiddleClick, edited.TrayMiddleClick)
                        ? current.TrayMiddleClick
                        : edited.TrayMiddleClick,
                UpdateChannel =
                    EqualityComparer<UpdateChannel>.Default.Equals(baseline.UpdateChannel, edited.UpdateChannel)
                        ? current.UpdateChannel
                        : edited.UpdateChannel,
            };
        }

        private static RecognitionPreferences Merge(RecognitionPreferences current, RecognitionPreferences baseline,
            RecognitionPreferences edited)
        {
            if (Same(baseline, edited)) return current;
            return new()
            {
                Layout = EqualityComparer<RecognitionTextLayout>.Default.Equals(baseline.Layout, edited.Layout)
                    ? current.Layout
                    : edited.Layout,
                Punctuation =
                    EqualityComparer<RecognitionPunctuation>.Default.Equals(baseline.Punctuation, edited.Punctuation)
                        ? current.Punctuation
                        : edited.Punctuation,
                ShowDialogOnRecognition =
                    EqualityComparer<bool>.Default.Equals(baseline.ShowDialogOnRecognition,
                        edited.ShowDialogOnRecognition)
                        ? current.ShowDialogOnRecognition
                        : edited.ShowDialogOnRecognition,
                ShowDialogOnCopyAll =
                    EqualityComparer<bool>.Default.Equals(baseline.ShowDialogOnCopyAll, edited.ShowDialogOnCopyAll)
                        ? current.ShowDialogOnCopyAll
                        : edited.ShowDialogOnCopyAll,
                LanguageTag = EqualityComparer<string>.Default.Equals(baseline.LanguageTag, edited.LanguageTag)
                    ? current.LanguageTag
                    : edited.LanguageTag,
            };
        }

        private static ScreenshotOutputSize Merge(ScreenshotOutputSize current, ScreenshotOutputSize baseline,
            ScreenshotOutputSize edited)
        {
            if (Same(baseline, edited)) return current;
            return new()
            {
                Mode = EqualityComparer<ScreenshotOutputSizeMode>.Default.Equals(baseline.Mode, edited.Mode)
                    ? current.Mode
                    : edited.Mode,
                Percent = EqualityComparer<double>.Default.Equals(baseline.Percent, edited.Percent)
                    ? current.Percent
                    : edited.Percent,
                Width = EqualityComparer<int>.Default.Equals(baseline.Width, edited.Width)
                    ? current.Width
                    : edited.Width,
                Height = EqualityComparer<int>.Default.Equals(baseline.Height, edited.Height)
                    ? current.Height
                    : edited.Height,
            };
        }

        private static ScreenshotAnnotationProperties Merge(ScreenshotAnnotationProperties current,
            ScreenshotAnnotationProperties baseline, ScreenshotAnnotationProperties edited)
        {
            if (Same(baseline, edited)) return current;
            return current with
            {
                Filled = EqualityComparer<bool>.Default.Equals(baseline.Filled, edited.Filled)
                    ? current.Filled
                    : edited.Filled,
                Dashed = EqualityComparer<bool>.Default.Equals(baseline.Dashed, edited.Dashed)
                    ? current.Dashed
                    : edited.Dashed,
                Bold = EqualityComparer<bool>.Default.Equals(baseline.Bold, edited.Bold) ? current.Bold : edited.Bold,
                Italic = EqualityComparer<bool>.Default.Equals(baseline.Italic, edited.Italic)
                    ? current.Italic
                    : edited.Italic,
                StrikeThrough = EqualityComparer<bool>.Default.Equals(baseline.StrikeThrough, edited.StrikeThrough)
                    ? current.StrikeThrough
                    : edited.StrikeThrough,
                TextBackground = EqualityComparer<Color>.Default.Equals(baseline.TextBackground, edited.TextBackground)
                    ? current.TextBackground
                    : edited.TextBackground,
                TextOutline = EqualityComparer<Color>.Default.Equals(baseline.TextOutline, edited.TextOutline)
                    ? current.TextOutline
                    : edited.TextOutline,
                TextOutlineWidth =
                EqualityComparer<double>.Default.Equals(baseline.TextOutlineWidth, edited.TextOutlineWidth)
                    ? current.TextOutlineWidth
                    : edited.TextOutlineWidth,
                WrapWidth = EqualityComparer<double>.Default.Equals(baseline.WrapWidth, edited.WrapWidth)
                    ? current.WrapWidth
                    : edited.WrapWidth,
                TextPadding = EqualityComparer<double>.Default.Equals(baseline.TextPadding, edited.TextPadding)
                    ? current.TextPadding
                    : edited.TextPadding,
                Opacity = EqualityComparer<double>.Default.Equals(baseline.Opacity, edited.Opacity)
                    ? current.Opacity
                    : edited.Opacity,
                Magnification = EqualityComparer<double>.Default.Equals(baseline.Magnification, edited.Magnification)
                    ? current.Magnification
                    : edited.Magnification,
                Number = EqualityComparer<int>.Default.Equals(baseline.Number, edited.Number)
                    ? current.Number
                    : edited.Number,
                Tiled = EqualityComparer<bool>.Default.Equals(baseline.Tiled, edited.Tiled)
                    ? current.Tiled
                    : edited.Tiled,
                NineGrid = EqualityComparer<bool>.Default.Equals(baseline.NineGrid, edited.NineGrid)
                    ? current.NineGrid
                    : edited.NineGrid,
                Anchor = EqualityComparer<int>.Default.Equals(baseline.Anchor, edited.Anchor)
                    ? current.Anchor
                    : edited.Anchor,
                ImageWidth = EqualityComparer<double>.Default.Equals(baseline.ImageWidth, edited.ImageWidth)
                    ? current.ImageWidth
                    : edited.ImageWidth,
                BlurRadius = EqualityComparer<int>.Default.Equals(baseline.BlurRadius, edited.BlurRadius)
                    ? current.BlurRadius
                    : edited.BlurRadius,
                FontFamily = EqualityComparer<string>.Default.Equals(baseline.FontFamily, edited.FontFamily)
                    ? current.FontFamily
                    : edited.FontFamily,
            };
        }

        private static AnnotationToolStyle Merge(AnnotationToolStyle current, AnnotationToolStyle baseline,
            AnnotationToolStyle edited)
        {
            if (Same(baseline, edited)) return current;
            return current with
            {
                Color = EqualityComparer<string>.Default.Equals(baseline.Color, edited.Color)
                    ? current.Color
                    : edited.Color,
                Thickness = EqualityComparer<double>.Default.Equals(baseline.Thickness, edited.Thickness)
                    ? current.Thickness
                    : edited.Thickness,
                FontSize = EqualityComparer<double>.Default.Equals(baseline.FontSize, edited.FontSize)
                    ? current.FontSize
                    : edited.FontSize,
                MosaicSize = EqualityComparer<int>.Default.Equals(baseline.MosaicSize, edited.MosaicSize)
                    ? current.MosaicSize
                    : edited.MosaicSize,
                Properties = Merge(current.Properties, baseline.Properties, edited.Properties),
            };
        }

        private static ToolbarPreferences Merge(ToolbarPreferences current, ToolbarPreferences baseline,
            ToolbarPreferences edited)
        {
            if (Same(baseline, edited)) return current;
            return current with
            {
                Columns = SameSequence(baseline.Columns, edited.Columns,
                    static (a, b) => SameSequence(a, b,
                        static (a, b) => EqualityComparer<ScreenshotTool>.Default.Equals(a, b)))
                    ? current.Columns
                    : edited.Columns,
                Actions = SameSequence(baseline.Actions, edited.Actions, static (a, b) => Same(a, b))
                    ? current.Actions
                    : edited.Actions,
            };
        }

        private static Dictionary<string, T> MergeSettingDictionary<T>(Dictionary<string, T> current,
            Dictionary<string, T> baseline, Dictionary<string, T> edited, Func<T, T, bool> equal,
            Func<T, T, T, T> merge)
        {
            if (SameDictionary(baseline, edited, equal)) return current;
            var result = new Dictionary<string, T>(current, StringComparer.Ordinal);
            foreach (var key in baseline.Keys)
                if (!TryGetOrdinalValue(edited, key, out _))
                    result.Remove(key);
            foreach (var (key, value) in edited)
            {
                var existed = TryGetOrdinalValue(baseline, key, out var original);
                if (existed && equal(original, value)) continue;
                result[key] = existed && TryGetOrdinalValue(current, key, out var active)
                    ? merge(active, original, value)
                    : value;
            }

            return result;
        }
    }
}
