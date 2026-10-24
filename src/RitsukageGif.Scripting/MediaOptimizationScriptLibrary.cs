namespace RitsukageGif.Scripting
{
    public static class MediaOptimizationScriptLibrary
    {
        public static void Register(ScriptBindingRegistry bindings, ScriptTableBinding media,
            IMediaOptimizationService? service, string root)
        {
            ScriptServiceErrors.Register(bindings);
            bindings.RegisterObjectSchema("MediaOptimizationOptions",
            [
                new("targetBytes", "number"), new("processing", "MediaOptions", true),
                new("minimumQuality", "number", true), new("minimumColors", "number", true),
                new("minimumScale", "number", true), new("maximumAttempts", "number", true)
            ]);
            bindings.RegisterObjectSchema("MediaOptimizationResult",
            [
                new("achieved", "boolean"), new("info", "MediaInfo?", true), new("smallestBytes", "number"),
                new("attempts", "number"), new("settings", "MediaOptions")
            ]);
            media.Function("optimize", async (args, token) =>
                {
                    var output = (string)args[1]!;
                    try
                    {
                        var fields = (Dictionary<string, object?>)args[2]!;
                        var target = Number(fields, "targetBytes", 0);
                        // Fractional byte counts must be rejected, without an epsilon tolerance.
                        // ReSharper disable once CompareOfFloatsByEqualityOperator
                        if (target <= 0 || target > 9_007_199_254_740_991 || target != Math.Truncate(target))
                            throw new ArgumentException(UiText.Get("ScriptTypeError"));
                        var options = new MediaOptimizationOptions
                        {
                            TargetBytes = (long)target,
                            Processing = MediaScriptLibrary.ReadOptions(fields.GetValueOrDefault("processing")),
                            MinimumQuality = Integer(fields, "minimumQuality"),
                            MinimumColors = Integer(fields, "minimumColors"),
                            MinimumScale = Number(fields, "minimumScale", 1),
                            MaximumAttempts = Integer(fields, "maximumAttempts") ?? 8
                        };
                        var host = service ?? throw new InvalidOperationException(UiText.Get("EditorNoRuntime"));
                        var result = await host.OptimizeAsync(Path.GetFullPath((string)args[0]!, root),
                            Path.GetFullPath(output, root), options, token);
                        var settings = fields.GetValueOrDefault("processing") is Dictionary<string, object?> supplied
                            ? new Dictionary<string, object?>(supplied)
                            : new Dictionary<string, object?>();
                        settings["format"] = result.Settings.Format;
                        settings["width"] = (double?)result.Settings.Width;
                        settings["height"] = (double?)result.Settings.Height;
                        settings["quality"] = (double)result.Settings.Quality;
                        settings["colors"] = (double)result.Settings.Colors;
                        settings["lossless"] = result.Settings.Lossless;
                        settings["dither"] = result.Settings.Dither;
                        settings["overwrite"] = result.Settings.Overwrite;
                        return new ScriptResult(true, new Dictionary<string, object?>
                        {
                            ["achieved"] = result.Achieved,
                            ["info"] = result.Info is { } info ? MediaScriptLibrary.Info(info) : null,
                            ["smallestBytes"] = (double)result.SmallestBytes,
                            ["attempts"] = (double)result.Attempts,
                            ["settings"] = settings
                        });
                    }
                    catch (Exception error) when (error is not OperationCanceledException)
                    {
                        token.ThrowIfCancellationRequested();
                        return new ScriptResult(false, ScriptServiceErrors.FromException(error, output));
                    }
                }, [new("input", "string"), new("output", "string"), new("options", "MediaOptimizationOptions")],
                "Result<MediaOptimizationResult,ServiceError>");
        }

        private static double Number(Dictionary<string, object?> fields, string name, double fallback)
        {
            if (!fields.TryGetValue(name, out var value) || value is null) return fallback;
            var number = ScriptEngine.Number(value);
            if (!double.IsFinite(number)) throw new ArgumentException(UiText.Get("ScriptTypeError"));
            return number;
        }

        private static int? Integer(Dictionary<string, object?> fields, string name)
        {
            if (!fields.ContainsKey(name)) return null;
            var number = Number(fields, name, 0);
            // Integer options require exact integrality within the validated finite range.
            // ReSharper disable once CompareOfFloatsByEqualityOperator
            if (number != Math.Truncate(number) || number < int.MinValue || number > int.MaxValue)
                throw new ArgumentException(UiText.Get("ScriptTypeError"));
            return (int)number;
        }
    }
}
