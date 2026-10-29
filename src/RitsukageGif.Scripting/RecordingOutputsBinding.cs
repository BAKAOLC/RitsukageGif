namespace RitsukageGif.Scripting
{
    public sealed class RecordingOutputsBinding(
        ScriptBindingRegistry bindings,
        RecordingSettingsBinding settings,
        string baseDirectory)
    {
        public const string SchemaName = "RecordingOutput";
        private const int MaximumOutputs = 8;

        public void Register()
        {
            bindings.RegisterObjectSchema(SchemaName,
            [
                new("name", "string"), new("path", "string"),
                new("presetFile", "string", true), new("presetName", "string", true),
                new("options", RecordingSettingsBinding.SchemaName, true),
            ]);
        }

        public IReadOnlyList<PresetRecordingOutput> Read(object? value)
        {
            var entries = (List<object?>)bindings.Validate(value, SchemaName + "[]")!;
            if (entries.Count is < 1 or > MaximumOutputs)
                throw new ArgumentException(UiText.Get("ServiceLimitExceeded"));
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var paths = new HashSet<string>(OperatingSystem.IsWindows()
                ? StringComparer.OrdinalIgnoreCase
                : StringComparer.Ordinal);
            var result = new List<PresetRecordingOutput>(entries.Count);
            foreach (var entry in entries.Cast<Dictionary<string, object?>>())
            {
                var name = Text(entry["name"]);
                if (name.Length > 128 || name is "." or ".." || name.Any(char.IsControl))
                    throw new ArgumentException(UiText.Get("ScriptTypeError"));
                var path = Resolve(Text(entry["path"]));
                if (!names.Add(name)) throw new ArgumentException(UiText.Format("DslDuplicate", name));
                if (!paths.Add(path)) throw new ArgumentException(UiText.Format("DslDuplicate", path));
                var presetFile = entry.GetValueOrDefault("presetFile") is { } file ? Resolve(Text(file)) : null;
                var presetName = entry.GetValueOrDefault("presetName") is { } preset ? Text(preset) : null;
                result.Add(new(name, path, presetFile, presetName,
                    entry.GetValueOrDefault("options") is { } options ? settings.Read(options) : null));
            }

            return result;
        }

        private string Resolve(string path)
        {
            return Path.GetFullPath(path, baseDirectory);
        }

        private static string Text(object? value)
        {
            if (value is not string text || string.IsNullOrWhiteSpace(text))
                throw new ArgumentException(UiText.Get("ScriptTypeError"));
            return text;
        }
    }
}
