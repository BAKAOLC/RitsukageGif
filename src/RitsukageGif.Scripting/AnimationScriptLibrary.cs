namespace RitsukageGif.Scripting
{
    public static class AnimationScriptLibrary
    {
        public static void Register(ScriptBindingRegistry bindings, IAnimationEditingService? service = null,
            string? baseDirectory = null)
        {
            ScriptServiceErrors.Register(bindings);
            MediaScriptLibrary.RegisterTypes(bindings);
            bindings.RegisterEnum("AnimationFormat", new Dictionary<string, string>
            {
                ["Gif"] = "gif", ["WebP"] = "webp", ["APng"] = "apng",
            });
            bindings.RegisterObjectSchema("AnimationExportOptions",
            [
                new("format", "AnimationFormat", true), new("colors", "number", true),
                new("quality", "number", true), new("lossless", "boolean", true),
                new("dither", "boolean", true), new("overwrite", "boolean", true),
            ]);
            bindings.RegisterObjectSchema("AnimationTransformOptions",
            [
                new("width", "number", true), new("height", "number", true),
                new("cropX", "number", true), new("cropY", "number", true),
                new("cropWidth", "number", true), new("cropHeight", "number", true),
                new("flipHorizontal", "boolean", true), new("flipVertical", "boolean", true),
                new("rotation", "number", true), new("effects", "PixelEffect[]", true),
            ]);
            bindings.RegisterObjectSchema("AnimationFrame",
                [new("width", "number"), new("height", "number"), new("delay", "number")]);
            var root = Path.GetFullPath(baseDirectory ?? Environment.CurrentDirectory);
            var type = bindings.RegisterType<IAnimationDocument>("AnimationDocument");
            type.Property("width", "number", value => (double)value.Current.Width);
            type.Property("height", "number", value => (double)value.Current.Height);
            type.Property("iterations", "number", value => (double)value.Current.Iterations);
            type.Property("canUndo", "boolean", value => value.Current.CanUndo);
            type.Property("canRedo", "boolean", value => value.Current.CanRedo);
            type.Property("closed", "boolean", value => value.IsClosed);
            type.Property("frames", "AnimationFrame[]", value => value.Current.Frames.Select(object? (frame) =>
                new Dictionary<string, object?>
                {
                    ["width"] = (double)frame.Width, ["height"] = (double)frame.Height,
                    ["delay"] = (double)frame.DelayMs,
                }).ToList());
            var table = bindings.RegisterTable("animation");
            table.Function("open", (args, token) => Result(async () =>
                    await Host().OpenAsync(Resolve((string)args[0]!), token), token),
                [new("path", "string")], "Result<AnimationDocument,ServiceError>");
            table.Function("create", (args, token) => Result(async () =>
                    await Host().CreateAsync(Integer(args[0]), Integer(args[1]), Integer(args[2]), token), token),
                [new("width", "number"), new("height", "number"), new("delay", "number", true, 100d)],
                "Result<AnimationDocument,ServiceError>");
            type.Method("setDelay", (value, args, token) => Void(() =>
                    value.SetDelayAsync(Indices(args[0]), Integer(args[1]), token), token),
                [new("indices", "number[]"), new("delay", "number")], "Result<void,ServiceError>");
            type.Method("setIterations", (value, args, token) => Void(() =>
                    value.SetIterationsAsync(checked((uint)Integer(args[0])), token), token),
                [new("iterations", "number")], "Result<void,ServiceError>");
            type.Method("duplicate", (value, args, token) => Void(() =>
                    value.DuplicateAsync(Indices(args[0]), token), token),
                [new("indices", "number[]")], "Result<void,ServiceError>");
            type.Method("delete", (value, args, token) => Void(() =>
                    value.DeleteAsync(Indices(args[0]), token), token),
                [new("indices", "number[]")], "Result<void,ServiceError>");
            type.Method("move", (value, args, token) => Void(() =>
                    value.MoveAsync(Integer(args[0]), Integer(args[1]), token), token),
                [new("from", "number"), new("to", "number")], "Result<void,ServiceError>");
            type.Method("addBlank", (value, args, token) => Void(() =>
                    value.AddBlankAsync(Integer(args[0]), Integer(args[1]), (string)args[2]!, token), token),
                [
                    new("index", "number"), new("delay", "number", true, 100d),
                    new("color", "string", true, "transparent"),
                ],
                "Result<void,ServiceError>");
            type.Method("import", (value, args, token) => Void(() =>
                    value.ImportAsync(Resolve((string)args[0]!), Integer(args[1]), token), token),
                [new("path", "string"), new("index", "number")], "Result<void,ServiceError>");
            type.Method("undo", (value, _, token) => Void(() =>
                    value.UndoAsync(token), token),
                [], "Result<void,ServiceError>");
            type.Method("redo", (value, _, token) => Void(() =>
                    value.RedoAsync(token), token),
                [], "Result<void,ServiceError>");
            type.Method("transform", (value, args, token) => Void(() =>
                    value.TransformAsync(MediaScriptLibrary.ReadOptions(args[0]), token), token),
                [new("options", "AnimationTransformOptions")], "Result<void,ServiceError>");
            type.Method("applyEffects", (value, args, token) => Void(() =>
                    value.ApplyEffectsAsync(Indices(args[0]), PixelEffectsBinding.Read(args[1]), token), token),
                [new("indices", "number[]"), new("effects", "PixelEffect[]")], "Result<void,ServiceError>");
            type.Method("overlay", (value, args, token) => Void(() =>
                    value.OverlayAsync(Indices(args[0]), Resolve((string)args[1]!), Integer(args[2]), Integer(args[3]),
                        token), token),
                [
                    new("indices", "number[]"), new("path", "string"), new("x", "number", true, 0d),
                    new("y", "number", true, 0d),
                ],
                "Result<void,ServiceError>");
            type.Method("moveSelected", (value, args, token) => Result(async () =>
                    (await value.MoveSelectedAsync(Indices(args[0]), Integer(args[1]), token))
                    .Select(object? (index) => (double)index).ToList(), token),
                [new("indices", "number[]"), new("direction", "number")], "Result<number[],ServiceError>");
            type.Method("replaceFrames", (value, args, token) => Void(async () =>
                {
                    var bytes = await File.ReadAllBytesAsync(Resolve((string)args[1]!), token);
                    await value.ReplaceFramesAsync(Indices(args[0]), bytes, token);
                }, token),
                [new("indices", "number[]"), new("path", "string")], "Result<void,ServiceError>");
            type.Method("export", (value, args, token) => Result(async () =>
                {
                    var path = Resolve((string)args[0]!);
                    await value.ExportAsync(path, MediaScriptLibrary.ReadOptions(args[1]), token);
                    return path;
                }, token),
                [new("path", "string"), new("options", "AnimationExportOptions", true)], "Result<string,ServiceError>");
            type.Method("exportFrame", (value, args, token) => Result(async () =>
                {
                    var path = Resolve((string)args[1]!);
                    await value.ExportFrameAsync(Integer(args[0]), path, token);
                    return path;
                }, token),
                [new("index", "number"), new("path", "string")], "Result<string,ServiceError>");
            type.Method("close", (value, _, token) => Void(() => value.DisposeAsync().AsTask(), token),
                [], "Result<void,ServiceError>");
            return;

            string Resolve(string path)
            {
                return Path.GetFullPath(path, root);
            }

            IAnimationEditingService Host()
            {
                return service ?? throw new InvalidOperationException(UiText.Get("EditorNoRuntime"));
            }
        }

        private static int Integer(object? value)
        {
            var number = ScriptEngine.Number(value);
            if (!double.IsFinite(number) || !number.Equals(Math.Truncate(number)) ||
                number < int.MinValue || number > int.MaxValue)
                throw new ArgumentException(UiText.Get("ScriptTypeError"));
            return (int)number;
        }

        private static int[] Indices(object? value)
        {
            var values = (List<object?>)value!;
            var indices = new int[values.Count];
            for (var index = 0; index < indices.Length; index++)
                indices[index] = Integer(values[index]);
            return indices;
        }

        private static async ValueTask<object?> Void(Func<Task> operation, CancellationToken token)
        {
            return await Result(async () =>
            {
                await operation();
                return null;
            }, token);
        }

        private static async ValueTask<object?> Result(Func<Task<object?>> operation, CancellationToken token)
        {
            try
            {
                token.ThrowIfCancellationRequested();
                return new ScriptResult(true, await operation());
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                token.ThrowIfCancellationRequested();
                return new ScriptResult(false, ScriptServiceErrors.FromException(error));
            }
        }
    }
}
