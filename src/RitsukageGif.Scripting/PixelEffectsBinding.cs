using System.Globalization;

namespace RitsukageGif.Scripting
{
    public static class PixelEffectsBinding
    {
        private const string SchemaName = "PixelEffect";

        public static void Register(ScriptBindingRegistry bindings)
        {
            if (bindings.GetObjectSchema(SchemaName) is not null) return;
            bindings.RegisterEnum("EffectKind", Enum.GetValues<PixelEffectKind>()
                .ToDictionary(value => value.ToString(), value => value));
            bindings.RegisterEnum("EffectSpace", Enum.GetValues<PixelEffectSpace>()
                .ToDictionary(value => value.ToString(), value => value));
            bindings.RegisterObjectSchema("EffectRect",
                [new("x", "number"), new("y", "number"), new("width", "number"), new("height", "number")]);
            bindings.RegisterObjectSchema(SchemaName,
            [
                new("kind", "EffectKind"), new("space", "EffectSpace", true), new("region", "EffectRect?", true),
                new("amount", "number", true), new("blockSize", "number", true), new("radius", "number", true),
                new("color", "string", true), new("brightness", "number", true), new("contrast", "number", true),
            ]);
        }

        public static IReadOnlyList<PixelEffect> Read(object? value)
        {
            if (value is not List<object?> entries) throw new ArgumentException(UiText.Get("ScriptTypeError"));
            if (entries.Count > 32) throw new ArgumentException(UiText.Get("ServiceLimitExceeded"));
            var effects = new PixelEffect[entries.Count];
            for (var i = 0; i < entries.Count; i++)
            {
                if (entries[i] is not Dictionary<string, object?> fields)
                    throw new ArgumentException(UiText.Get("ScriptTypeError"));
                var region = fields.GetValueOrDefault("region") is Dictionary<string, object?> rectangle
                    ? new PixelRect(Integer(rectangle, "x", 0), Integer(rectangle, "y", 0),
                        Integer(rectangle, "width", 0), Integer(rectangle, "height", 0))
                    : (PixelRect?)null;
                var effect = new PixelEffect((PixelEffectKind)fields["kind"]!, region,
                    Number(fields, "amount", 1), Integer(fields, "blockSize", 8), Integer(fields, "radius", 4),
                    Color(fields.GetValueOrDefault("color")), Number(fields, "brightness", 0),
                    Number(fields, "contrast", 1),
                    fields.GetValueOrDefault("space") is PixelEffectSpace space ? space : PixelEffectSpace.Source);
                effect.Validate();
                effects[i] = effect;
            }

            return effects;
        }

        public static List<object?> Values(IReadOnlyList<PixelEffect>? effects)
        {
            return
            [
                .. (effects ?? [])
                .Select(object? (effect) => new Dictionary<string, object?>
                {
                    ["kind"] = effect.Kind, ["space"] = effect.Space, ["amount"] = effect.Amount,
                    ["blockSize"] = (double)effect.BlockSize, ["radius"] = (double)effect.Radius,
                    ["color"] = "#" + effect.Color.ToString("X8", CultureInfo.InvariantCulture),
                    ["brightness"] = effect.Brightness, ["contrast"] = effect.Contrast,
                    ["region"] = effect.Region is { } rect
                        ? new Dictionary<string, object?>
                        {
                            ["x"] = (double)rect.X, ["y"] = (double)rect.Y,
                            ["width"] = (double)rect.Width, ["height"] = (double)rect.Height,
                        }
                        : null,
                }),
            ];
        }

        private static double Number(Dictionary<string, object?> fields, string name, double fallback)
        {
            return fields.GetValueOrDefault(name) is { } value ? ScriptEngine.Number(value) : fallback;
        }

        private static int Integer(Dictionary<string, object?> fields, string name, int fallback)
        {
            var value = Number(fields, name, fallback);
            // Fractional inputs must be rejected exactly; a tolerance would silently accept invalid integers.
            // ReSharper disable once CompareOfFloatsByEqualityOperator
            if (!double.IsFinite(value) || value != Math.Truncate(value) || value < int.MinValue ||
                value > int.MaxValue)
                throw new ArgumentException(UiText.Get("ScriptTypeError"));
            return (int)value;
        }

        private static uint Color(object? value)
        {
            if (value is null) return 0xff000000;
            if (value is not string { Length: 7 or 9 } text || text[0] != '#' ||
                !uint.TryParse(text.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var color))
                throw new ArgumentException(UiText.Get("ScriptTypeError"));
            return text.Length == 7 ? color | 0xff000000 : color;
        }
    }
}
