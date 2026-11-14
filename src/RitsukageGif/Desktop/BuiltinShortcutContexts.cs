namespace RitsukageGif.Desktop
{
    internal static class BuiltinShortcutContexts
    {
        internal const string CapturePin = "BuiltinShortcutCapturePinContext";
        internal const string Pinned = "BuiltinShortcutPinnedContext";
        internal const string PinnedImage = "BuiltinShortcutPinnedImageContext";
        internal const string PinnedDynamicImage = "BuiltinShortcutPinnedDynamicImageContext";
        internal const string PinnedWindowRoi = "BuiltinShortcutPinnedWindowRoiContext";
        private const string PinnedText = "BuiltinShortcutPinnedTextContext";

        internal static IReadOnlyList<string> Keys { get; } =
        [
            CapturePin, "BuiltinShortcutSelectionContext", "BuiltinShortcutScreenshotContext", Pinned, PinnedImage,
            PinnedText, PinnedDynamicImage, PinnedWindowRoi, "BuiltinShortcutLongshotContext",
        ];

        private static bool IsPinnedChild(string context)
        {
            return context is PinnedImage or PinnedText or PinnedDynamicImage or PinnedWindowRoi;
        }

        internal static bool Overlap(string first, string second)
        {
            return first == second || (first == Pinned && IsPinnedChild(second)) ||
                   (second == Pinned && IsPinnedChild(first));
        }
    }
}
