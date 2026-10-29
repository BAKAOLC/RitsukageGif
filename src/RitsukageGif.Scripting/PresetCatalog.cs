namespace RitsukageGif.Scripting
{
    public static class PresetCatalog
    {
        public static IReadOnlyList<WindowPreset> Load(IEnumerable<string> files)
        {
            var result = new List<WindowPreset>();
            foreach (var file in files.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var fullPath = Path.GetFullPath(file);
                if (new FileInfo(fullPath).Length > 1024 * 1024) throw new FormatException(UiText.Get("DslTooComplex"));
                IReadOnlyList<WindowPreset> presets;
                try
                {
                    presets = WindowPreset.Parse(File.ReadAllText(fullPath));
                }
                catch (PresetSyntaxException ex)
                {
                    throw new PresetSyntaxException(ex.Line, ex.Column, ex.RawMessage, fullPath);
                }
                catch (FormatException ex)
                {
                    throw new FormatException(Path.GetFileName(fullPath) + ": " + ex.Message, ex);
                }

                foreach (var preset in presets)
                {
                    if (result.Any(p => p.Name == preset.Name))
                        throw new FormatException(UiText.Format("DslDuplicate", preset.Name));
                    result.Add(Resolve(preset, fullPath));
                }
            }

            return result;
        }

        // ReSharper disable once MemberCanBePrivate.Global
        public static WindowPreset Resolve(WindowPreset preset, string file)
        {
            var folder = Path.GetDirectoryName(Path.GetFullPath(file))!;

            return preset with
            {
                SourceFile = Path.GetFullPath(file),
                Outputs = [.. preset.Outputs.Select(o => o with { Directory = ResolvePath(o.Directory) })],
                Missing = ResolveFallback(preset.Missing),
                Layout = preset.Layout is { } layout
                    ? layout with
                    {
                        Layers = [.. layout.Layers.Select(l => l with { Missing = ResolveFallback(l.Missing)! })],
                    }
                    : null,
            };

            string ResolvePath(string value)
            {
                return Path.GetFullPath(Environment.ExpandEnvironmentVariables(value), folder);
            }

            PresetFallback? ResolveFallback(PresetFallback? value)
            {
                return value is { Kind: "image" } ? value with { Value = ResolvePath(value.Value) } : value;
            }
        }
    }
}
