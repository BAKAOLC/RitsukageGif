namespace RitsukageGif.Desktop
{
    public sealed partial record AppSettings
    {
        public string GetActionShortcut(GlobalActionKind kind)
        {
            foreach (var action in GlobalActions)
                if (action.Enabled && action.Kind == kind && !string.IsNullOrWhiteSpace(action.Shortcut))
                    return action.Shortcut;
            return "";
        }

        private static List<GlobalActionBinding> CreateDefaultActions()
        {
            return
            [
                CreateAction(GlobalActionKind.Screenshot), CreateAction(GlobalActionKind.ScreenshotAndCopy),
                CreateAction(GlobalActionKind.PinClipboard), CreateAction(GlobalActionKind.RestoreLastPinnedImage),
                CreateAction(GlobalActionKind.ClosePinnedImages), CreateAction(GlobalActionKind.TogglePinnedImages),
                CreateAction(GlobalActionKind.SelectRegion, "Ctrl+Shift+S"),
                CreateAction(GlobalActionKind.ToggleRecording, "Ctrl+Shift+A"),
                CreateAction(GlobalActionKind.OpenVideoRecorder),
            ];
        }

        private static GlobalActionBinding CreateAction(GlobalActionKind kind, string shortcut = "")
        {
            return new()
            {
                Id = "builtin." + kind.ToString().ToLowerInvariant(),
                Kind = kind, Shortcut = shortcut,
            };
        }
    }
}
