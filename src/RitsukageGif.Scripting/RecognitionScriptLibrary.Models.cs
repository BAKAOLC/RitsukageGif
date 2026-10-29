namespace RitsukageGif.Scripting
{
    public static partial class RecognitionScriptLibrary
    {
        private static void RegisterSchemas(ScriptBindingRegistry bindings)
        {
            bindings.RegisterObjectSchema("OcrBounds",
                [new("x", "number"), new("y", "number"), new("width", "number"), new("height", "number")]);
            bindings.RegisterObjectSchema("OcrLanguage",
                [new("tag", "string"), new("displayName", "string"), new("nativeName", "string")]);
            bindings.RegisterObjectSchema("OcrCapabilities",
            [
                new("available", "boolean"), new("maxImageDimension", "number"),
                new("languages", "OcrLanguage[]"), new("failureReason", "string?"),
            ]);
            bindings.RegisterObjectSchema("OcrWord", [new("text", "string"), new("bounds", "OcrBounds")]);
            bindings.RegisterObjectSchema("OcrLine",
                [new("text", "string"), new("bounds", "OcrBounds"), new("words", "OcrWord[]")]);
            bindings.RegisterObjectSchema("OcrResult",
            [
                new("text", "string"), new("language", "string"), new("width", "number"),
                new("height", "number"), new("textAngle", "number?"), new("lines", "OcrLine[]"),
            ]);
            bindings.RegisterObjectSchema("BarcodePoint", [new("x", "number"), new("y", "number")]);
            bindings.RegisterObjectSchema("BarcodeResult",
                [new("text", "string"), new("format", "string"), new("points", "BarcodePoint[]")]);
        }

        private static Dictionary<string, object?> Capabilities(OcrCapabilities value)
        {
            return new()
            {
                ["available"] = value.Available, ["maxImageDimension"] = (double)value.MaxImageDimension,
                ["failureReason"] = value.FailureReason,
                ["languages"] = value.Languages.Select(object? (language) => new Dictionary<string, object?>
                {
                    ["tag"] = language.Tag, ["displayName"] = language.DisplayName,
                    ["nativeName"] = language.NativeName,
                }).ToList(),
            };
        }

        private static Dictionary<string, object?> Bounds(OcrBounds value)
        {
            return new()
            {
                ["x"] = (double)value.X, ["y"] = (double)value.Y,
                ["width"] = (double)value.Width, ["height"] = (double)value.Height,
            };
        }

        private static Dictionary<string, object?> Recognition(OcrRecognitionResult value)
        {
            return new()
            {
                ["text"] = value.Text, ["language"] = value.LanguageTag,
                ["width"] = (double)value.Width, ["height"] = (double)value.Height, ["textAngle"] = value.TextAngle,
                ["lines"] = value.Lines.Select(object? (line) => new Dictionary<string, object?>
                {
                    ["text"] = line.Text, ["bounds"] = Bounds(line.Bounds),
                    ["words"] = line.Words.Select(object? (word) => new Dictionary<string, object?>
                    {
                        ["text"] = word.Text, ["bounds"] = Bounds(word.Bounds),
                    }).ToList(),
                }).ToList(),
            };
        }

        private static Dictionary<string, object?> Barcode(BarcodeRecognitionResult value)
        {
            return new()
            {
                ["text"] = value.Text, ["format"] = value.Format,
                ["points"] = value.Points.Select(object? (point) => new Dictionary<string, object?>
                {
                    ["x"] = (double)point.X, ["y"] = (double)point.Y,
                }).ToList(),
            };
        }
    }
}
