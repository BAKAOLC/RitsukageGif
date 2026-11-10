namespace RitsukageGif.Scripting
{
    public static partial class LongCaptureScriptLibrary
    {
        private static void RegisterSchemas(ScriptBindingRegistry bindings)
        {
            bindings.RegisterEnum("LongCaptureSource", Enum.GetValues<LongCaptureSourceKind>()
                .ToDictionary(value => value.ToString(), value => value));
            bindings.RegisterEnum("ScrollCaptureAxis", Enum.GetValues<ScrollCaptureAxis>()
                .ToDictionary(value => value.ToString(), value => value));
            bindings.RegisterEnum("ScrollCaptureStatus", Enum.GetValues<ScrollCaptureStatus>()
                .ToDictionary(value => value.ToString(), value => value));
            bindings.RegisterEnum("StaticImageFormat", Enum.GetValues<StaticImageFormat>()
                .ToDictionary(value => value.ToString(), value => value));
            bindings.RegisterEnum("HdrImageFormat", Enum.GetValues<HdrImageFormat>()
                .ToDictionary(value => value.ToString(), value => value));
            bindings.RegisterEnum("OutputColorMode", Enum.GetValues<OutputColorMode>()
                .ToDictionary(value => value.ToString(), value => value));
            bindings.RegisterEnum("HdrToneMapMode", Enum.GetValues<HdrToneMapMode>()
                .ToDictionary(value => value.ToString(), value => value));
            bindings.RegisterObjectSchema("LongCaptureColorRegion",
            [
                new("x", "number"), new("y", "number"), new("width", "number"), new("height", "number"),
                new("isHdrSource", "boolean"), new("sdrWhiteNits", "number"),
            ]);
            bindings.RegisterObjectSchema("LongCaptureOptions",
            [
                new("x", "number", true), new("y", "number", true), new("width", "number"),
                new("height", "number"), new("source", "LongCaptureSource", true),
                new("windowId", "number", true), new("axis", "ScrollCaptureAxis", true),
                new("interval", "number", true), new("autoCropOnReverse", "boolean", true),
                new("retainHdr", "boolean", true), new("leadingInset", "number", true),
                new("trailingInset", "number", true), new("maximumLength", "number", true),
                new("maximumBytes", "number", true), new("minimumOverlap", "number", true),
                new("toneMap", "HdrToneMapMode", true),
            ]);
            bindings.RegisterObjectSchema("LongCaptureSaveOptions",
            [
                new("color", "OutputColorMode", true), new("format", "StaticImageFormat", true),
                new("hdrFormat", "HdrImageFormat", true), new("quality", "number", true),
                new("background", "string", true), new("overwrite", "boolean", true),
            ]);
            bindings.RegisterObjectSchema("LongCaptureMatch",
            [
                new("status", "ScrollCaptureStatus"), new("offset", "number"), new("addedPixels", "number"),
                new("confidence", "number"), new("width", "number"), new("height", "number"),
            ]);
            bindings.RegisterObjectSchema("LongCaptureState",
            [
                new("running", "boolean"), new("stopped", "boolean"), new("x", "number"), new("y", "number"),
                new("regionWidth", "number"), new("regionHeight", "number"), new("axis", "ScrollCaptureAxis"),
                new("width", "number"), new("height", "number"), new("last", "LongCaptureMatch?"),
                new("error", "string?"),
            ]);
            if (bindings.GetObjectSchema("MediaInfo") is null)
                bindings.RegisterObjectSchema("MediaInfo",
                [
                    new("path", "string"), new("bytes", "number"), new("width", "number"), new("height", "number"),
                    new("frames", "number"), new("duration", "number"), new("format", "string"),
                ]);
        }

        private static LongCaptureOptions Options(object? value)
        {
            var fields = (Dictionary<string, object?>)value!;
            var windowId = fields.GetValueOrDefault("windowId") is { } id ? LongInteger(id) : (long?)null;
            return new()
            {
                Region = new(Integer(fields.GetValueOrDefault("x") ?? 0d),
                    Integer(fields.GetValueOrDefault("y") ?? 0d), Integer(fields["width"]), Integer(fields["height"])),
                Source = fields.GetValueOrDefault("source") as LongCaptureSourceKind? ?? LongCaptureSourceKind.Frames,
                WindowId = windowId,
                IntervalSeconds = fields.GetValueOrDefault("interval") is { } interval
                    ? ScriptEngine.Number(interval)
                    : 0.1,
                ToneMap = fields.GetValueOrDefault("toneMap") as HdrToneMapMode? ?? HdrToneMapMode.HighlightCompression,
                Stitching = new()
                {
                    Axis = fields.GetValueOrDefault("axis") as ScrollCaptureAxis? ?? ScrollCaptureAxis.Vertical,
                    AutoCropOnReverse = fields.GetValueOrDefault("autoCropOnReverse") is true,
                    RetainHdr = fields.GetValueOrDefault("retainHdr") is true,
                    LeadingInset = Integer(fields.GetValueOrDefault("leadingInset") ?? 0d),
                    TrailingInset = Integer(fields.GetValueOrDefault("trailingInset") ?? 0d),
                    MaximumLength = Integer(fields.GetValueOrDefault("maximumLength") ?? 32768d),
                    MaximumBytes = LongInteger(fields.GetValueOrDefault("maximumBytes") ?? 268435456d),
                    MinimumOverlap = fields.GetValueOrDefault("minimumOverlap") is { } overlap
                        ? ScriptEngine.Number(overlap)
                        : 0.3,
                },
            };
        }

        private static StaticCaptureOutput Output(string path, object? value)
        {
            var fields = value as Dictionary<string, object?> ?? [];
            var color = fields.GetValueOrDefault("color") as OutputColorMode? ?? OutputColorMode.Sdr;
            if (color is not (OutputColorMode.Sdr or OutputColorMode.Hdr) ||
                (color == OutputColorMode.Sdr && fields.ContainsKey("hdrFormat")) ||
                (color == OutputColorMode.Hdr && (fields.ContainsKey("format") || fields.ContainsKey("quality") ||
                                                  fields.ContainsKey("background"))))
                throw new ArgumentException("The long capture save options do not match the output color mode.");
            var quality = Integer(fields.GetValueOrDefault("quality") ?? 90d);
            if (quality is < 0 or > 100) throw new ArgumentOutOfRangeException(nameof(value));
            return new(path)
            {
                Color = color,
                Image = new()
                {
                    Format = fields.GetValueOrDefault("format") as StaticImageFormat?,
                    Quality = quality,
                    Background = fields.GetValueOrDefault("background") as string ?? "#ffffff",
                    Overwrite = fields.GetValueOrDefault("overwrite") is true,
                },
                HdrImage = new()
                {
                    Format = fields.GetValueOrDefault("hdrFormat") as HdrImageFormat?,
                    Overwrite = fields.GetValueOrDefault("overwrite") is true,
                },
            };
        }

        private static int Integer(object? value)
        {
            return checked((int)LongInteger(value));
        }

        private static long LongInteger(object? value)
        {
            var number = ScriptEngine.Number(value);
            if (!double.IsFinite(number) || number < -9007199254740991d || number > 9007199254740991d ||
                !number.Equals(Math.Truncate(number)))
                throw new ArgumentOutOfRangeException(nameof(value));
            return (long)number;
        }

        private static Dictionary<string, object?> Match(ScrollCaptureResult result)
        {
            return new()
            {
                ["status"] = result.Status, ["offset"] = (double)result.Offset,
                ["addedPixels"] = (double)result.AddedPixels, ["confidence"] = result.Confidence,
                ["width"] = (double)result.Width, ["height"] = (double)result.Height,
            };
        }

        private static Dictionary<string, object?> State(LongCaptureState state)
        {
            return new()
            {
                ["running"] = state.Running, ["stopped"] = state.Stopped,
                ["x"] = (double)state.Region.X, ["y"] = (double)state.Region.Y,
                ["regionWidth"] = (double)state.Region.Width, ["regionHeight"] = (double)state.Region.Height,
                ["axis"] = state.Axis, ["width"] = (double)state.Width, ["height"] = (double)state.Height,
                ["last"] = state.LastResult is { } result ? Match(result) : null, ["error"] = state.Error,
            };
        }
    }
}
