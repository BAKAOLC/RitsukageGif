namespace RitsukageGif.Scripting
{
    public static class MediaScriptLibrary
    {
        public static void Register(ScriptBindingRegistry bindings, IMediaProcessingService? service = null,
            string? baseDirectory = null)
        {
            var root = Path.GetFullPath(baseDirectory ?? Environment.CurrentDirectory);
            PixelEffectsBinding.Register(bindings);
            bindings.RegisterEnum("MediaFormat", new Dictionary<string, string>
            {
                ["Gif"] = "gif", ["WebP"] = "webp", ["Png"] = "png", ["APng"] = "apng",
            });
            bindings.RegisterObjectSchema("MediaInfo",
            [
                new("path", "string"), new("bytes", "number"), new("width", "number"), new("height", "number"),
                new("frames", "number"), new("duration", "number"), new("format", "string"),
            ]);
            bindings.RegisterObjectSchema("MediaOptions",
            [
                new("format", "MediaFormat", true), new("colors", "number", true),
                new("width", "number", true), new("height", "number", true), new("quality", "number", true),
                new("lossless", "boolean", true), new("dither", "boolean", true), new("overwrite", "boolean", true),
                new("cropX", "number", true), new("cropY", "number", true),
                new("cropWidth", "number", true), new("cropHeight", "number", true),
                new("flipHorizontal", "boolean", true), new("flipVertical", "boolean", true),
                new("rotation", "number", true), new("frameIndex", "number", true),
                new("effects", "PixelEffect[]", true),
            ]);
            bindings.RegisterObjectSchema("MediaOutput",
                [new("path", "string"), new("options", "MediaOptions", true)]);
            var media = bindings.RegisterTable("media");
            media.Function("inspect", (args, token) => Inspect((string)args[0]!, token),
                [new("path", "string")], "Result<MediaInfo,ServiceError>");
            media.Function("process", (args, token) => Process((string)args[0]!, (string)args[1]!, args[2], token),
                [new("input", "string"), new("output", "string"), new("options", "MediaOptions", true)],
                "Result<MediaInfo,ServiceError>");
            media.Function("processMany", async (args, token) =>
                {
                    token.ThrowIfCancellationRequested();
                    var input = (string)args[0]!;
                    var outputs = (List<object?>)args[1]!;
                    if (outputs.Count > 16) throw new ArgumentException(UiText.Get("ServiceLimitExceeded"));
                    var seen = new HashSet<string>(OperatingSystem.IsWindows()
                        ? StringComparer.OrdinalIgnoreCase
                        : StringComparer.Ordinal);
                    var destinations = outputs.Cast<Dictionary<string, object?>>().ToArray();
                    // Reject aliases before starting workers so two encoders cannot race to publish the same file.
                    foreach (var destination in destinations)
                    {
                        var path = Resolve((string)destination["path"]!);
                        if (!seen.Add(path)) throw new ArgumentException(UiText.Get("ScriptTypeError"));
                    }

                    var results = new object?[destinations.Length];
                    var requests = new List<MediaProcessOutput>(destinations.Length);
                    var indices = new List<int>(destinations.Length);
                    for (var index = 0; index < destinations.Length; index++)
                    {
                        var destination = destinations[index];
                        try
                        {
                            requests.Add(new(Resolve((string)destination["path"]!),
                                ReadOptions(destination.GetValueOrDefault("options"))));
                            indices.Add(index);
                        }
                        catch (Exception error) when (error is not OperationCanceledException)
                        {
                            results[index] = new ScriptResult(false,
                                ScriptServiceErrors.FromException(error, (string)destination["path"]!));
                        }
                    }

                    try
                    {
                        var processed = await Host().ProcessManyAsync(Resolve(input), requests, token);
                        for (var index = 0; index < processed.Count; index++)
                        {
                            var result = processed[index];
                            results[indices[index]] = result.Error is { } error
                                ? new ScriptResult(false,
                                    ScriptServiceErrors.FromException(error, requests[index].Path))
                                : new ScriptResult(true, Info(result.Info!));
                        }
                    }
                    catch (Exception error) when (error is not OperationCanceledException)
                    {
                        token.ThrowIfCancellationRequested();
                        foreach (var index in indices)
                            results[index] = new ScriptResult(false,
                                ScriptServiceErrors.FromException(error, (string)destinations[index]["path"]!));
                    }

                    return results.ToList();
                }, [new("input", "string"), new("outputs", "MediaOutput[]")],
                "Result<MediaInfo,ServiceError>[]");
            MediaOptimizationScriptLibrary.Register(bindings, media, service as IMediaOptimizationService, root);
            return;

            string Resolve(string path)
            {
                return Path.GetFullPath(path, root);
            }

            IMediaProcessingService Host()
            {
                return service ?? throw new InvalidOperationException(UiText.Get("EditorNoRuntime"));
            }

            async ValueTask<object?> Inspect(string path, CancellationToken token)
            {
                try
                {
                    return new ScriptResult(true, Info(await Host().InspectAsync(Resolve(path), token)));
                }
                catch (Exception error) when (error is not OperationCanceledException)
                {
                    token.ThrowIfCancellationRequested();
                    return new ScriptResult(false, ScriptServiceErrors.FromException(error, path));
                }
            }

            async ValueTask<object?> Process(string input, string output, object? options, CancellationToken token)
            {
                try
                {
                    return new ScriptResult(true,
                        Info(await Host().ProcessAsync(Resolve(input), Resolve(output), ReadOptions(options), token)));
                }
                catch (Exception error) when (error is not OperationCanceledException)
                {
                    token.ThrowIfCancellationRequested();
                    return new ScriptResult(false, ScriptServiceErrors.FromException(error, output));
                }
            }
        }

        internal static Dictionary<string, object?> Info(MediaInfo info)
        {
            return new()
            {
                ["path"] = info.Path, ["bytes"] = (double)info.Bytes, ["width"] = (double)info.Width,
                ["height"] = (double)info.Height, ["frames"] = (double)info.Frames,
                ["duration"] = info.DurationSeconds, ["format"] = info.Format,
            };
        }

        internal static MediaProcessOptions ReadOptions(object? value)
        {
            var fields = value as Dictionary<string, object?> ?? [];

            return new()
            {
                Format = fields.GetValueOrDefault("format") as string,
                Colors = Integer("colors") ?? 256, Width = Integer("width"), Height = Integer("height"),
                Quality = Integer("quality") ?? 90, Lossless = Boolean("lossless", true), Dither = Boolean("dither"),
                Overwrite = Boolean("overwrite"), CropX = Integer("cropX"), CropY = Integer("cropY"),
                CropWidth = Integer("cropWidth"), CropHeight = Integer("cropHeight"),
                FlipHorizontal = Boolean("flipHorizontal"), FlipVertical = Boolean("flipVertical"),
                Rotation = Integer("rotation") ?? 0, FrameIndex = Integer("frameIndex"),
                Effects = fields.GetValueOrDefault("effects") is { } effects ? PixelEffectsBinding.Read(effects) : null,
            };

            int? Integer(string name)
            {
                if (!fields.TryGetValue(name, out var field) || field is null) return null;
                var number = ScriptEngine.Number(field);
                if (!double.IsFinite(number) || !number.Equals(Math.Truncate(number)) || number < int.MinValue ||
                    number > int.MaxValue)
                    throw new ArgumentException(UiText.Get("ScriptTypeError"));
                return (int)number;
            }

            bool Boolean(string name, bool fallback = false)
            {
                return fields.GetValueOrDefault(name) is bool flag ? flag : fallback;
            }
        }
    }
}
