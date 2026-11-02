namespace RitsukageGif.Scripting
{
    public static partial class ImageScriptLibrary
    {
        private static void RegisterInpainting(ScriptBindingRegistry bindings, ScriptTableBinding table,
            IImageCompositionService? service, string root)
        {
            bindings.RegisterEnum("ImageMaskChannel", Enum.GetValues<ImageMaskChannel>()
                .ToDictionary(value => value.ToString(), value => value));
            bindings.RegisterObjectSchema("ImageInpaintingOptions",
            [
                new("regions", "EffectRect[]", true), new("mask", "string", true),
                new("maskChannel", "ImageMaskChannel", true), new("radius", "number", true),
                new("frameIndex", "number", true), new("format", "ImageFormat", true),
                new("quality", "number", true), new("lossless", "boolean", true), new("overwrite", "boolean", true),
            ]);
            table.Function("inpaint", async (args, token) =>
                {
                    var input = (string)args[0]!;
                    try
                    {
                        var fields = Fields(args[2]);
                        var regions = fields.GetValueOrDefault("regions") as List<object?> ?? [];
                        var options = new ImageInpaintingOptions
                        {
                            Regions =
                            [
                                .. regions.Select(value =>
                                {
                                    var rectangle = Fields(value);
                                    return new PixelRect(Integer(rectangle, "x") ?? 0, Integer(rectangle, "y") ?? 0,
                                        Integer(rectangle, "width") ?? 0, Integer(rectangle, "height") ?? 0);
                                }),
                            ],
                            MaskPath = fields.GetValueOrDefault("mask") is string path
                                ? Path.GetFullPath(path, root)
                                : null,
                            MaskChannel = fields.GetValueOrDefault("maskChannel") is ImageMaskChannel channel
                                ? channel
                                : ImageMaskChannel.Luminance,
                            Radius = Integer(fields, "radius") ?? 4,
                            FrameIndex = Integer(fields, "frameIndex") ?? 0,
                            Format = fields.GetValueOrDefault("format") as string,
                            Quality = Integer(fields, "quality") ?? 90,
                            Lossless = fields.GetValueOrDefault("lossless") is not false,
                            Overwrite = fields.GetValueOrDefault("overwrite") is true,
                        };
                        var host = service ?? throw new InvalidOperationException(UiText.Get("EditorNoRuntime"));
                        return new ScriptResult(true, Info(await host.InpaintAsync(Path.GetFullPath(input, root),
                            Path.GetFullPath((string)args[1]!, root), options, token)));
                    }
                    catch (Exception error) when (error is not OperationCanceledException)
                    {
                        token.ThrowIfCancellationRequested();
                        return new ScriptResult(false, ScriptServiceErrors.FromException(error, input));
                    }
                }, [new("input", "string"), new("output", "string"), new("options", "ImageInpaintingOptions")],
                "Result<MediaInfo,ServiceError>");
        }
    }
}
