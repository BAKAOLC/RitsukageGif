namespace RitsukageGif.Desktop
{
    internal static class GlobalMouseActionCatalog
    {
        internal static IReadOnlyList<GlobalActionKind> FixedKinds { get; } = Array.AsReadOnly([
            GlobalActionKind.ScreenshotAndPin, GlobalActionKind.ScreenshotAndCopy,
            GlobalActionKind.ScreenshotAndCopyText, GlobalActionKind.ScreenshotQuickSave,
            GlobalActionKind.ScreenshotAndTranslate, GlobalActionKind.Screenshot,
            GlobalActionKind.ScreenshotAndSave, GlobalActionKind.ScreenshotAndScroll,
            GlobalActionKind.ScreenshotAndRecord,
        ]);

        internal static string FixedId(GlobalActionKind kind)
        {
            return "builtin.mouse." + kind;
        }

        internal static string FixedLabel(GlobalActionKind kind)
        {
            return UiText.Get(kind switch
            {
                GlobalActionKind.Screenshot => "GlobalMouseScreenshotAndEdit",
                GlobalActionKind.ScreenshotAndSave => "GlobalMouseScreenshotAndSave",
                GlobalActionKind.ScreenshotAndScroll => "GlobalMouseLongShot",
                GlobalActionKind.ScreenshotAndRecord => "GlobalMouseGifShot",
                _ => "GlobalAction" + kind,
            });
        }

        private static bool IsFixed(GlobalActionBinding action)
        {
            return FixedKinds.Contains(action.Kind) && action.Id == FixedId(action.Kind);
        }

        internal static bool IsMouseOnly(GlobalActionBinding action)
        {
            return !action.ShowInTray && string.IsNullOrWhiteSpace(action.Shortcut) && IsFixed(action);
        }
    }
}
