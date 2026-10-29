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
                GlobalActions =
                [
                    .. GlobalActions.Select(action => action with
                    {
                        Arguments = new(action.Arguments, StringComparer.Ordinal),
                    }),
                ],
                MouseShortcuts = MouseShortcuts.ToDictionary(pair => pair.Key,
                    pair => (string[])pair.Value.Clone(), StringComparer.Ordinal),
                SelectionShortcuts = SelectionShortcuts.ToDictionary(pair => pair.Key,
                    pair => (string[])pair.Value.Clone(), StringComparer.Ordinal),
                Appearance = Appearance with { },
                System = System with { }, Recognition = Recognition with { },
                CapturePreferences = CapturePreferences with { },
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
                PinnedImages = PinnedImages with { },
                VideoRecording = VideoRecording with { },
                HotkeyPolicy = HotkeyPolicy with
                {
                    IgnoredPrograms = [.. HotkeyPolicy.IgnoredPrograms],
                },
                Annotation = Annotation with
                {
                    ColorPalette = [.. Annotation.ColorPalette],
                    Toolbar = Annotation.Toolbar.Normalize(),
                    Properties = Annotation.Properties with { },
                    ToolStyles = Annotation.ToolStyles.ToDictionary(pair => pair.Key,
                        pair => pair.Value with { Properties = pair.Value.Properties with { } },
                        StringComparer.Ordinal),
                },
            };
        }
    }
}
