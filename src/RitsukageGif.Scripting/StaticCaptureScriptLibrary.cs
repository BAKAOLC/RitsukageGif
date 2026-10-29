namespace RitsukageGif.Scripting
{
    public static class StaticCaptureScriptLibrary
    {
        public static void Register(ScriptBindingRegistry bindings, IStaticCaptureService? service = null,
            string? baseDirectory = null)
        {
            var root = Path.GetFullPath(baseDirectory ?? Environment.CurrentDirectory);
            ScriptServiceErrors.Register(bindings);
            bindings.RegisterEnum("StaticImageFormat", Enum.GetValues<StaticImageFormat>()
                .ToDictionary(value => value.ToString(), value => value));
            bindings.RegisterEnum("OutputColorMode", Enum.GetValues<OutputColorMode>()
                .ToDictionary(value => value.ToString(), value => value));
            bindings.RegisterEnum("HdrToneMapMode", Enum.GetValues<HdrToneMapMode>()
                .ToDictionary(value => value.ToString(), value => value));
            bindings.RegisterEnum("HdrImageFormat", Enum.GetValues<HdrImageFormat>()
                .ToDictionary(value => value.ToString(), value => value));
            bindings.RegisterObjectSchema("StaticCaptureOutput",
            [
                new("path", "string"), new("color", "OutputColorMode", true),
                new("format", "StaticImageFormat", true), new("hdrFormat", "HdrImageFormat", true),
                new("quality", "number", true), new("background", "string", true),
            ]);
            bindings.RegisterObjectSchema("StaticCaptureBatchOptions",
            [
                new("includeCursor", "boolean", true), new("scalePercent", "number", true),
                new("outputName", "string", true), new("toneMap", "HdrToneMapMode", true),
            ]);
            bindings.RegisterObjectSchema("StaticCaptureOptions",
            [
                new("format", "StaticImageFormat", true), new("quality", "number", true),
                new("background", "string", true), new("overwrite", "boolean", true),
                new("includeCursor", "boolean", true), new("scalePercent", "number", true),
                new("outputName", "string", true), new("toneMap", "HdrToneMapMode", true),
            ]);
            if (bindings.GetObjectSchema("MediaInfo") is null)
                bindings.RegisterObjectSchema("MediaInfo",
                [
                    new("path", "string"), new("bytes", "number"), new("width", "number"), new("height", "number"),
                    new("frames", "number"), new("duration", "number"), new("format", "string"),
                ]);
            var table = bindings.RegisterTable("capture");
            table.Function("save", async (args, token) =>
                {
                    var output = (string)args[0]!;
                    try
                    {
                        token.ThrowIfCancellationRequested();
                        var fields = args[1] as Dictionary<string, object?> ?? [];
                        var quality = Integer(fields, "quality") ?? 90;
                        var scale = Integer(fields, "scalePercent");
                        if (quality is < 0 or > 100 || scale is < 1 or > 100)
                            throw new ArgumentOutOfRangeException(nameof(args));
                        var options = new StaticCaptureOptions
                        {
                            Image = new()
                            {
                                Format = fields.GetValueOrDefault("format") as StaticImageFormat?,
                                Quality = quality,
                                Background = fields.GetValueOrDefault("background") as string ?? "#ffffff",
                                Overwrite = fields.GetValueOrDefault("overwrite") is true,
                            },
                            IncludeCursor = fields.GetValueOrDefault("includeCursor") as bool?,
                            ScalePercent = scale,
                            OutputName = fields.GetValueOrDefault("outputName") as string,
                            ToneMap = fields.GetValueOrDefault("toneMap") as HdrToneMapMode?,
                        };
                        var host = service ?? throw new InvalidOperationException(UiText.Get("EditorNoRuntime"));
                        var info = await host.CaptureAsync(Path.GetFullPath(output, root), options, token);
                        return new ScriptResult(true, MediaScriptLibrary.Info(info));
                    }
                    catch (Exception error) when (error is not OperationCanceledException)
                    {
                        token.ThrowIfCancellationRequested();
                        return new ScriptResult(false, ScriptServiceErrors.FromException(error, output));
                    }
                }, [new("output", "string"), new("options", "StaticCaptureOptions", true)],
                "Result<MediaInfo,ServiceError>");
            table.Function("saveOutputs", async (args, token) =>
                {
                    try
                    {
                        token.ThrowIfCancellationRequested();
                        var destinations = (List<object?>)args[0]!;
                        if (destinations.Count is < 1 or > 16)
                            throw new ArgumentException(UiText.Get("ServiceLimitExceeded"));
                        var outputs = new List<StaticCaptureOutput>(destinations.Count);
                        foreach (var value in destinations)
                        {
                            var fields = (Dictionary<string, object?>)value!;
                            var color = fields.GetValueOrDefault("color") as OutputColorMode? ?? OutputColorMode.Sdr;
                            if (color is not (OutputColorMode.Sdr or OutputColorMode.Hdr) ||
                                (color == OutputColorMode.Sdr && fields.ContainsKey("hdrFormat")) ||
                                (color == OutputColorMode.Hdr && (fields.ContainsKey("format") ||
                                                                  fields.ContainsKey("quality") ||
                                                                  fields.ContainsKey("background"))))
                                throw new ArgumentException(UiText.Get("StaticCaptureBatchOptionsInvalid"));
                            var quality = Integer(fields, "quality") ?? 90;
                            if (quality is < 0 or > 100) throw new ArgumentOutOfRangeException(nameof(args));
                            outputs.Add(new(Path.GetFullPath((string)fields["path"]!, root))
                            {
                                Color = color,
                                Image = new()
                                {
                                    Format = fields.GetValueOrDefault("format") as StaticImageFormat?,
                                    Quality = quality,
                                    Background = fields.GetValueOrDefault("background") as string ?? "#ffffff",
                                },
                                HdrImage = new() { Format = fields.GetValueOrDefault("hdrFormat") as HdrImageFormat? },
                            });
                        }

                        var options = args[1] as Dictionary<string, object?> ?? [];
                        var scale = Integer(options, "scalePercent");
                        if (scale is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(args));
                        var host = service ?? throw new InvalidOperationException(UiText.Get("EditorNoRuntime"));
                        var results = await host.CaptureOutputsAsync(outputs, new()
                        {
                            IncludeCursor = options.GetValueOrDefault("includeCursor") as bool?,
                            ScalePercent = scale,
                            OutputName = options.GetValueOrDefault("outputName") as string,
                            ToneMap = options.GetValueOrDefault("toneMap") as HdrToneMapMode?,
                        }, token);
                        return new ScriptResult(true,
                            results.Select(object? (info) => MediaScriptLibrary.Info(info)).ToList());
                    }
                    catch (Exception error) when (error is not OperationCanceledException)
                    {
                        token.ThrowIfCancellationRequested();
                        return new ScriptResult(false, ScriptServiceErrors.FromException(error));
                    }
                }, [new("outputs", "StaticCaptureOutput[]"), new("options", "StaticCaptureBatchOptions", true)],
                "Result<MediaInfo[],ServiceError>");
        }

        private static int? Integer(Dictionary<string, object?> fields, string name)
        {
            if (fields.GetValueOrDefault(name) is not { } value) return null;
            var number = ScriptEngine.Number(value);
            if (!double.IsInteger(number) || number is < int.MinValue or > int.MaxValue)
                throw new ArgumentException(UiText.Get("ScriptTypeError"));
            return (int)number;
        }
    }
}
