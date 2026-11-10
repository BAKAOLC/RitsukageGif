namespace RitsukageGif.Desktop
{
    internal static class DesktopToolbarActions
    {
        private static readonly Dictionary<string, GlobalActionBinding> Bindings;

        static DesktopToolbarActions()
        {
            Bindings = Ids.ToDictionary(id => id, CreateBinding, StringComparer.Ordinal);
        }

        internal static IReadOnlyList<string> Ids { get; } =
            Array.AsReadOnly<string>([
                "screenshot", "pin", "delayed", "ocr", "scroll", "record", "translate", "table", "formula",
            ]);

        internal static GlobalActionDefinition Definition(string id)
        {
            return GlobalActions.Definition(id switch
            {
                "screenshot" or "delayed" => GlobalActionKind.Screenshot,
                "pin" => GlobalActionKind.PinClipboard,
                "ocr" => GlobalActionKind.ScreenshotAndCopyText,
                "scroll" => GlobalActionKind.ScreenshotAndScroll,
                "record" => GlobalActionKind.ScreenshotAndRecord,
                "translate" => GlobalActionKind.ScreenshotAndTranslate,
                "table" => GlobalActionKind.ScreenshotAndRecognizeTable,
                "formula" => GlobalActionKind.ScreenshotAndRecognizeFormula,
                _ => throw new ArgumentOutOfRangeException(nameof(id)),
            });
        }

        internal static string NameKey(string id)
        {
            return id == "delayed" ? "DesktopToolbarDelayed" : Definition(id).NameKey;
        }

        internal static GlobalActionBinding Binding(string id)
        {
            return Bindings[id];
        }

        private static GlobalActionBinding CreateBinding(string id)
        {
            return new()
            {
                Id = "desktop." + id, Kind = Definition(id).Kind, ShowInTray = false,
                Arguments = id == "delayed"
                    ? new(StringComparer.Ordinal) { ["delay"] = "3" }
                    : new(StringComparer.Ordinal),
            };
        }
    }
}
