namespace RitsukageGif.Core
{
    public static class SampleColorFormats
    {
        public static SampleColorTemplate DefaultTemplate { get; } = Compile(CreateDefaults()[0]);

        public static SampleColorTemplate Compile(ColorFormatDefinition definition)
        {
            if (SampleColorTokenRegistry.Default.TryCompile(definition.Template, out var result, out var error))
                return result!;
            throw new FormatException($"{error!.Message} Position: {error.Offset + 1}.");
        }

        public static List<ColorFormatDefinition> CreateDefaults()
        {
            return
            [
                new() { Id = "rgb", Name = "RGB", Template = "rgb(%Re, %Gr, %Bl)" },
                new() { Id = "bgr", Name = "BGR", Template = "bgr(%Bl, %Gr, %Re)" },
                new() { Id = "hex", Name = "HEX", Template = "%Rex%Grx%Blx" },
                new() { Id = "hash-hex", Name = "#HEX", Template = "#%Rex%Grx%Blx" },
                new() { Id = "hsv", Name = "HSV", Template = "hsv(%Hu, %Sb%, %Va%)" },
                new() { Id = "hsl", Name = "HSL", Template = "hsl(%Hu, %Sl%, %Ll%)" },
            ];
        }

        public static List<ColorFormatDefinition> Normalize(IEnumerable<ColorFormatDefinition?>? definitions)
        {
            if (definitions is null) return CreateDefaults();
            var result = new List<ColorFormatDefinition>();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in definitions)
            {
                if (item is null || string.IsNullOrWhiteSpace(item.Id) || string.IsNullOrWhiteSpace(item.Name) ||
                    string.IsNullOrEmpty(item.Template) || ids.Contains(item.Id) ||
                    !SampleColorTokenRegistry.Default.TryCompile(item.Template, out _, out _)) continue;
                ids.Add(item.Id);
                result.Add(item);
            }

            return result.Count == 0 ? CreateDefaults() : result;
        }

        public static ColorFormatDefinition First(IReadOnlyList<ColorFormatDefinition> definitions,
            string? selectedId = null)
        {
            ColorFormatDefinition? firstEnabled = null;
            // ReSharper disable once ForCanBeConvertedToForeach
            for (var index = 0; index < definitions.Count; index++)
            {
                var item = definitions[index];
                if (!item.Enabled) continue;
                firstEnabled ??= item;
                if (item.Id == selectedId) return item;
            }

            return firstEnabled ?? (definitions.Count == 0 ? CreateDefaults()[0] : definitions[0]);
        }

        public static ColorFormatDefinition Next(IReadOnlyList<ColorFormatDefinition> definitions, string selectedId)
        {
            var index = -1;
            for (var i = 0; i < definitions.Count; i++)
                if (definitions[i].Id == selectedId)
                {
                    index = i;
                    break;
                }

            if (index < 0) return First(definitions);
            for (var step = 1; step < definitions.Count; step++)
            {
                var item = definitions[(index + step) % definitions.Count];
                if (item.Enabled) return item;
            }

            return definitions[index];
        }
    }
}
