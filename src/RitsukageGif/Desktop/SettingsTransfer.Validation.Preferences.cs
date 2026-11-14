namespace RitsukageGif.Desktop
{
    internal static partial class SettingsTransfer
    {
        private static void ValidatePreferenceValues(AppSettings settings)
        {
            ValidateNetworkPreferences(settings.Network);
            var appearance = settings.Appearance;
            if (!Enum.IsDefined(appearance.ThemeMode) || !Enum.IsDefined(appearance.TrayIcon) ||
                !Enum.IsDefined(appearance.ToolbarIconSize) || !Enum.IsDefined(appearance.ColorDialogStyle) ||
                !Enum.IsDefined(appearance.SaveDialogStyle) || !Enum.IsDefined(appearance.SelectionAnchors))
                throw Invalid("Appearance");
            ValidateFiniteRange(appearance.SelectionBorderWidth, "Appearance.SelectionBorderWidth", 0,
                AppearancePreferences.MaximumSelectionBorderWidth);
            if (!Math.Truncate(appearance.SelectionBorderWidth).Equals(appearance.SelectionBorderWidth))
                throw Invalid("Appearance.SelectionBorderWidth");
            ValidateFontFamily(appearance.FontFamily, "Appearance.FontFamily");

            var pinned = settings.PinnedImages;
            const PinnedLockRestrictions operations =
                PinnedLockRestrictions.Move | PinnedLockRestrictions.PointerClose |
                PinnedLockRestrictions.KeyboardClose | PinnedLockRestrictions.IncomingDrop;
            if (!Enum.IsDefined(pinned.TitlePosition) || !Enum.IsDefined(pinned.DisplayPosition) ||
                !(pinned.ActiveGroup is { Length: 0 } || Guid.TryParseExact(pinned.ActiveGroup, "N", out _)) ||
                // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
                pinned.Groups is null || pinned.Groups.Any(group => group is not { IsValid: true }) ||
                pinned.Groups.Select(group => group.Id).Distinct(StringComparer.Ordinal).Count() !=
                pinned.Groups.Count ||
                pinned.Groups.Select(group => group.Name).Distinct(StringComparer.Ordinal).Count() !=
                pinned.Groups.Count ||
                (pinned.ActiveGroup.Length != 0 && pinned.Groups.All(group => group.Id != pinned.ActiveGroup)) ||
                !Enum.IsDefined(pinned.OutputContent) || !Enum.IsDefined(pinned.ReplacementAction) ||
                !Enum.IsDefined(pinned.DuplicateBehavior) ||
                !Enum.IsDefined(pinned.RestoreOrder) || !Enum.IsDefined(pinned.WheelZoomAnchor) ||
                !Enum.IsDefined(pinned.ResetZoomAnchor) || (pinned.LockRestrictions & ~operations) != 0 ||
                pinned.MaximumWindowSize < 0 || pinned.QuickThumbnailWidth is < 1 ||
                pinned.QuickThumbnailHeight is < 1 || pinned.HistoryLimit < 0 ||
                (pinned.SupportedContentKinds & ~PinnedContentKinds.All) != 0)
                throw Invalid("PinnedImages");
            ValidateFiniteRange(pinned.Opacity, "PinnedImages.Opacity", .15, 1);
            ValidateFiniteRange(pinned.MouseThroughOpacity, "PinnedImages.MouseThroughOpacity", .15, 1);
            ValidateFiniteRange(pinned.ZoomStep, "PinnedImages.ZoomStep", .01, 1);
            ValidateFiniteRange(pinned.FineZoomStep, "PinnedImages.FineZoomStep", .001, .1);
            ValidateFiniteRange(pinned.OpacityStep, "PinnedImages.OpacityStep", .01, .5);

            var save = settings.ScreenshotSave;
            ValidateSaveProfile(save, "ScreenshotSave");
            ValidateSaveProfile(save.Quick, "ScreenshotSave.Quick");
            ValidateSaveProfile(save.Automatic, "ScreenshotSave.Automatic");
            if (!Enum.IsDefined(save.QuickNotificationAction) || save.AutomaticCleanupMaxAgeDays is <= 0 or > 36500 ||
                (save.AutoSaveTriggers & ~ScreenshotAutoSaveTriggers.All) != 0 ||
                (save.LastFormat is { } format && !Enum.IsDefined(format)) ||
                (save.LastExtension is { } extension && ScreenshotSaveProfile.NormalizeExtension(extension) is null))
                throw Invalid("ScreenshotSave");
            var clipboard = settings.ScreenshotClipboard;
            if (!Enum.IsDefined(clipboard.FileFormat) ||
                clipboard.HdpiTargetPercent is not (0 or 100 or 150 or 200 or 250 or 300))
                throw Invalid("ScreenshotClipboard");
            ValidateOutputSizeValue(clipboard.OutputSize, "ScreenshotClipboard.OutputSize");

            var annotation = settings.Annotation;
            foreach (var (tool, style) in annotation.ToolStyles)
                ValidateFontFamily(style.Properties.FontFamily,
                    "Annotation.ToolStyles." + tool + ".Properties.FontFamily");
            if (!Enum.TryParse<ScreenshotTool>(annotation.LastTool, out var lastTool) || !Enum.IsDefined(lastTool) ||
                annotation.LastTool != lastTool.ToString()) throw Invalid("Annotation.LastTool");
            if (!Enum.TryParse<ScreenshotTool>(annotation.PinnedLastTool, out var pinnedTool) ||
                !Enum.IsDefined(pinnedTool) ||
                annotation.PinnedLastTool != pinnedTool.ToString()) throw Invalid("Annotation.PinnedLastTool");
            if (settings.HotkeyPolicy.IgnoredPrograms.Any(program =>
                    !HotkeyPolicyPreferences.TryNormalizeProgram(program, out _)))
                throw Invalid("HotkeyPolicy.IgnoredPrograms");
            if (settings.GlobalMouseIgnoredPrograms.Any(program => !ProgramExclusionRules.TryNormalize(program, out _)))
                throw Invalid(nameof(AppSettings.GlobalMouseIgnoredPrograms));

            var system = settings.System;
            if (!Enum.IsDefined(system.TraySingleClick) || !Enum.IsDefined(system.TrayDoubleClick) ||
                !Enum.IsDefined(system.TrayMiddleClick) || !Enum.IsDefined(system.UpdateChannel))
                throw Invalid("System");
            if (!Enum.IsDefined(settings.Recognition.Layout) || !Enum.IsDefined(settings.Recognition.Punctuation) ||
                !Enum.IsDefined(settings.Recognition.CopyFormat) || !Enum.IsDefined(settings.Recognition.LatexFormat))
                throw Invalid("Recognition");
            if (!RecognitionDialogPreferences.ValidSize(settings.Recognition.Dialog.Width,
                    settings.Recognition.Dialog.Height))
                throw Invalid("Recognition.Dialog");
            if (!Enum.IsDefined(settings.Translation.Provider) || settings.Translation.TimeoutSeconds is < 1 or > 300)
                throw Invalid("Translation");
            ValidateTranslationLanguage(settings.Translation.SourceLanguage, "Translation.SourceLanguage", true, false);
            ValidateTranslationLanguage(settings.Translation.TargetLanguage, "Translation.TargetLanguage", false,
                false);
            ValidateTranslationLanguage(settings.Translation.FallbackLanguage, "Translation.FallbackLanguage", false,
                true);
        }

        private static void ValidateTranslationLanguage(string value, string path, bool automatic, bool optional)
        {
            if (optional && value.Length == 0) return;
            if (value.Length is 0 or > 64 ||
                value.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not ('-' or '_')) ||
                (!automatic && value.Equals("auto", StringComparison.OrdinalIgnoreCase))) throw Invalid(path);
        }

        private static void ValidateSaveProfile(ScreenshotSaveProfile profile, string path)
        {
            if (!Enum.IsDefined(profile.AfterSave) || !Enum.IsDefined(profile.Format) ||
                !Enum.IsDefined(profile.ConflictPolicy) || profile.Quality is < 0 or > 100 ||
                string.IsNullOrWhiteSpace(profile.NameTemplate)) throw Invalid(path);
            var context = new ScreenshotNameContext(DateTime.Now);
            _ = ScreenshotPathTemplate.Expand(profile.NameTemplate, context);
            if (!string.IsNullOrWhiteSpace(profile.Directory))
                _ = profile.ResolveDirectory(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), context);
            ValidateOutputSizeValue(profile.OutputSize, path + ".OutputSize");
        }

        private static void ValidateOutputSizeValue(ScreenshotOutputSize size, string path)
        {
            if (!Enum.IsDefined(size.Mode) || size.Width is <= 0 or > 128 * 1024 * 1024 ||
                size.Height is <= 0 or > 128 * 1024 * 1024 || !double.IsFinite(size.Percent) ||
                size.Percent is <= 0 or > 10000 ||
                (size.Mode is ScreenshotOutputSizeMode.Exact or ScreenshotOutputSizeMode.Fit &&
                 (long)size.Width * size.Height > 128L * 1024 * 1024)) throw Invalid(path);
        }

        private static void ValidateFontFamily(string family, string path)
        {
            if (family.Trim().Length > 256) throw Invalid(path);
        }

        private static void ValidateFiniteRange(double value, string path, double minimum, double maximum)
        {
            if (!double.IsFinite(value) || value < minimum || value > maximum) throw Invalid(path);
        }
    }
}
