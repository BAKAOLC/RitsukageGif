namespace RitsukageGif.Desktop
{
    public sealed partial record AppSettings
    {
        internal AppSettings Snapshot()
        {
            return this with
            {
                PresetHotkeys = [.. PresetHotkeys], PresetFiles = [.. PresetFiles],
                GlobalMouseBindings = [.. GlobalMouseBindings.Select(binding => binding with { })],
                GlobalMouseIgnoredPrograms = [.. GlobalMouseIgnoredPrograms],
                GlobalActions =
                [
                    .. GlobalActions.Select(action => action with
                    {
                        Arguments = new(action.Arguments, StringComparer.Ordinal),
                        IgnoredPrograms = [.. action.IgnoredPrograms],
                    }),
                ],
                MouseShortcuts = MouseShortcuts.ToDictionary(pair => pair.Key,
                    pair => (string[])pair.Value.Clone(), StringComparer.Ordinal),
                MixingKeypadMainKeyboard = MixingKeypadMainKeyboard,
                SelectionShortcuts = SelectionShortcuts.ToDictionary(pair => pair.Key,
                    pair => (string[])pair.Value.Clone(), StringComparer.Ordinal),
                Appearance = Appearance with { },
                System = System with { DesktopToolbar = System.DesktopToolbar.Normalize() },
                Recognition = Recognition with { },
                Translation = Translation with { }, Network = Network with { },
                CapturePreferences = CapturePreferences with
                {
                    ColorFormats = [.. CapturePreferences.ColorFormats],
                    FrameDecoration = CapturePreferences.FrameDecoration with { },
                },
                ScrollCapture = ScrollCapture with { },
                ScreenshotClipboard = ScreenshotClipboard with
                {
                    OutputSize = ScreenshotClipboard.OutputSize with { },
                },
                ScreenshotSave = ScreenshotSave with
                {
                    OutputSize = ScreenshotSave.OutputSize with { },
                    Quick = ScreenshotSave.Quick with { OutputSize = ScreenshotSave.Quick.OutputSize with { } },
                    Automatic = ScreenshotSave.Automatic with
                    {
                        OutputSize = ScreenshotSave.Automatic.OutputSize with { },
                    },
                },
                PinnedImages = PinnedImages with
                {
                    Groups = [.. PinnedImages.Groups.Select(group => group with { })],
                    TextHighlightColors = [.. PinnedImages.TextHighlightColors],
                },
                VideoRecording = VideoRecording with { },
                ScreenshotRecording = ScreenshotRecording with
                {
                    InputEffects = ScreenshotRecording.InputEffects with { },
                    ProgressEffects = ScreenshotRecording.ProgressEffects with { },
                    Camera = ScreenshotRecording.Camera with
                    {
                        Primary = ScreenshotRecording.Camera.Primary with { },
                        Secondary = ScreenshotRecording.Camera.Secondary with { },
                    },
                },
                HotkeyPolicy = HotkeyPolicy with
                {
                    IgnoredPrograms = [.. HotkeyPolicy.IgnoredPrograms],
                },
                Annotation = Annotation with
                {
                    ColorPalette = [.. Annotation.ColorPalette],
                    Toolbar = Annotation.Toolbar.Normalize(),
                    ToolStyles = Annotation.ToolStyles.ToDictionary(pair => pair.Key,
                        pair => pair.Value with { Properties = pair.Value.Properties with { } },
                        StringComparer.Ordinal),
                },
            };
        }
    }
}
