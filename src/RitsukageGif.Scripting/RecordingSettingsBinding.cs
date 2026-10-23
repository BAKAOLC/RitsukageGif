namespace RitsukageGif.Scripting
{
    public sealed class RecordingSettingsBinding(ScriptBindingRegistry bindings)
    {
        public const string SchemaName = "RecordingOptions";

        private readonly Dictionary<string, RecordingSettingDefinition> _fields = new(StringComparer.Ordinal)
        {
            ["fps"] = new(new("fps", "number", true), (settings, value) => settings with { Fps = Integer(value) }),
            ["scale"] =
                new(new("scale", "number", true), (settings, value) => settings with { Scale = Integer(value) }),
            ["format"] = new(new("format", "OutputFormat", true),
                (settings, value) => settings with { Format = (OutputFormat)value! }),
            ["quality"] = new(new("quality", "GifQuality", true),
                (settings, value) => settings with { Quality = (GifQuality)value! }),
            ["cursor"] = new(new("cursor", "boolean", true),
                (settings, value) => settings with { Cursor = (bool)value! }),
            ["sound"] = new(new("sound", "boolean", true), (settings, value) => settings with { Sound = (bool)value! }),
            ["ffmpeg"] = new(new("ffmpeg", "boolean", true),
                (settings, value) => settings with { Ffmpeg = (bool)value! }),
            ["copy"] = new(new("copy", "boolean", true), (settings, value) => settings with { Copy = (bool)value! })
        };

        public void Register()
        {
            bindings.RegisterEnum("OutputFormat", new Dictionary<string, OutputFormat>
            {
                ["Gif"] = OutputFormat.Gif, ["WebP"] = OutputFormat.WebP, ["APng"] = OutputFormat.APng
            });
            bindings.RegisterEnum("GifQuality", new Dictionary<string, GifQuality>
            {
                ["Fast"] = GifQuality.Fast, ["Balanced"] = GifQuality.Balanced, ["Best"] = GifQuality.Best
            });
            bindings.RegisterObjectSchema(SchemaName, _fields.Values.Select(field => field.Property).ToArray());
            bindings.RegisterDeclarationSchema("record", SchemaName);
        }

        public PresetRecordingSettings Read(object? value)
        {
            var fields = (Dictionary<string, object?>)bindings.Validate(value, SchemaName)!;
            var settings = new PresetRecordingSettings();
            foreach (var (name, field) in fields) settings = _fields[name].Apply(settings, field);
            return settings;
        }

        private static int Integer(object? value)
        {
            var number = ScriptEngine.Number(value);
            if (number is < 1 or > 100 || !number.Equals(Math.Truncate(number)))
                throw new ArgumentException(UiText.Format("DslRange", 1, 100));
            return (int)number;
        }
    }
}
