namespace RitsukageGif.Scripting
{
    public static class ImageScriptLibrary
    {
        public static void Register(ScriptBindingRegistry bindings, IImageCompositionService? service = null,
            string? baseDirectory = null)
        {
            var root = Path.GetFullPath(baseDirectory ?? Environment.CurrentDirectory);
            PixelEffectsBinding.Register(bindings);
            bindings.RegisterObjectSchema("ImageLayer",
            [
                new("path", "string"), new("frameIndex", "number", true), new("x", "number", true),
                new("y", "number", true), new("width", "number", true), new("height", "number", true),
                new("cropX", "number", true), new("cropY", "number", true), new("cropWidth", "number", true),
                new("cropHeight", "number", true), new("opacity", "number", true), new("rotation", "number", true),
                new("effects", "PixelEffect[]", true),
            ]);
            bindings.RegisterObjectSchema("ImageCompositionOptions",
            [
                new("width", "number"), new("height", "number"), new("layers", "ImageLayer[]", true),
                new("background", "string", true), new("format", "MediaFormat", true),
                new("quality", "number", true), new("lossless", "boolean", true), new("overwrite", "boolean", true),
            ]);
            bindings.RegisterObjectSchema("ImageContactSheetOptions",
            [
                new("columns", "number", true), new("thumbnailWidth", "number", true),
                new("thumbnailHeight", "number", true), new("firstFrame", "number", true),
                new("frameStep", "number", true), new("maxFrames", "number", true),
                new("background", "string", true), new("overwrite", "boolean", true),
            ]);
            var table = bindings.RegisterTable("image");
            table.Function("compose", async (args, token) =>
                {
                    var output = (string)args[0]!;
                    try
                    {
                        var fields = Fields(args[1]);
                        var layers = fields.GetValueOrDefault("layers") as List<object?> ?? [];
                        var options = new ImageCompositionOptions
                        {
                            Width = Integer(fields, "width") ?? 0, Height = Integer(fields, "height") ?? 0,
                            Background = fields.GetValueOrDefault("background") as string ?? "transparent",
                            Format = fields.GetValueOrDefault("format") as string,
                            Quality = Integer(fields, "quality") ?? 90,
                            Lossless = fields.GetValueOrDefault("lossless") is not false,
                            Overwrite = fields.GetValueOrDefault("overwrite") is true,
                            Layers = [.. layers.Select(value => Layer(Fields(value), root))],
                        };
                        return new ScriptResult(true, Info(await Host().ComposeAsync(Resolve(output), options, token)));
                    }
                    catch (Exception error) when (error is not OperationCanceledException)
                    {
                        token.ThrowIfCancellationRequested();
                        return new ScriptResult(false, ScriptServiceErrors.FromException(error, output));
                    }
                }, [new("output", "string"), new("options", "ImageCompositionOptions")],
                "Result<MediaInfo,ServiceError>");
            table.Function("contactSheet", async (args, token) =>
                {
                    var output = (string)args[1]!;
                    try
                    {
                        var fields = Fields(args[2]);
                        var options = new ImageContactSheetOptions
                        {
                            Columns = Integer(fields, "columns") ?? 4,
                            ThumbnailWidth = Integer(fields, "thumbnailWidth") ?? 160,
                            ThumbnailHeight = Integer(fields, "thumbnailHeight") ?? 120,
                            FirstFrame = Integer(fields, "firstFrame") ?? 0,
                            FrameStep = Integer(fields, "frameStep") ?? 1,
                            MaxFrames = Integer(fields, "maxFrames") ?? 64,
                            Background = fields.GetValueOrDefault("background") as string ?? "transparent",
                            Overwrite = fields.GetValueOrDefault("overwrite") is true,
                        };
                        return new ScriptResult(true, Info(await Host().ContactSheetAsync(Resolve((string)args[0]!),
                            Resolve(output), options, token)));
                    }
                    catch (Exception error) when (error is not OperationCanceledException)
                    {
                        token.ThrowIfCancellationRequested();
                        return new ScriptResult(false, ScriptServiceErrors.FromException(error, output));
                    }
                }, [new("input", "string"), new("output", "string"), new("options", "ImageContactSheetOptions", true)],
                "Result<MediaInfo,ServiceError>");
            return;

            string Resolve(string path)
            {
                return Path.GetFullPath(path, root);
            }

            IImageCompositionService Host()
            {
                return service ?? throw new InvalidOperationException(UiText.Get("EditorNoRuntime"));
            }
        }

        private static ImageLayerOptions Layer(Dictionary<string, object?> fields, string root)
        {
            return new()
            {
                Path = Path.GetFullPath((string)fields["path"]!, root),
                FrameIndex = Integer(fields, "frameIndex") ?? 0,
                X = Integer(fields, "x") ?? 0, Y = Integer(fields, "y") ?? 0,
                Width = Integer(fields, "width"), Height = Integer(fields, "height"),
                CropX = Integer(fields, "cropX"), CropY = Integer(fields, "cropY"),
                CropWidth = Integer(fields, "cropWidth"), CropHeight = Integer(fields, "cropHeight"),
                Opacity = fields.TryGetValue("opacity", out var opacity) ? ScriptEngine.Number(opacity) : 1,
                Rotation = Integer(fields, "rotation") ?? 0,
                Effects = fields.GetValueOrDefault("effects") is { } effects ? PixelEffectsBinding.Read(effects) : [],
            };
        }

        private static Dictionary<string, object?> Fields(object? value)
        {
            return value as Dictionary<string, object?> ?? [];
        }

        private static int? Integer(Dictionary<string, object?> fields, string name)
        {
            if (!fields.TryGetValue(name, out var value) || value is null) return null;
            var number = ScriptEngine.Number(value);
            // Pixel dimensions and frame indices must be exact integers.
            // ReSharper disable once CompareOfFloatsByEqualityOperator
            if (!double.IsFinite(number) || number != Math.Truncate(number) || number < int.MinValue ||
                number > int.MaxValue)
                throw new ArgumentException(UiText.Get("ScriptTypeError"));
            return (int)number;
        }

        private static Dictionary<string, object?> Info(MediaInfo info)
        {
            return new()
            {
                ["path"] = info.Path, ["bytes"] = (double)info.Bytes, ["width"] = (double)info.Width,
                ["height"] = (double)info.Height, ["frames"] = (double)info.Frames,
                ["duration"] = info.DurationSeconds, ["format"] = info.Format,
            };
        }
    }
}
