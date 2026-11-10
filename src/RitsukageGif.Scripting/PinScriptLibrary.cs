namespace RitsukageGif.Scripting
{
    public static partial class PinScriptLibrary
    {
        public static ScriptTableBinding Register(ScriptBindingRegistry bindings, IPinAutomationService? service = null,
            string? baseDirectory = null)
        {
            bindings.RegisterEnum("PinTransparentLayer", Enum.GetValues<PinTransparentLayer>()
                .ToDictionary(value => value.ToString(), value => value));
            var root = Path.GetFullPath(baseDirectory ?? Environment.CurrentDirectory);
            ScriptServiceErrors.Register(bindings);
            bindings.RegisterObjectSchema("PinInfo",
            [
                new("id", "string"), new("kind", "string"), new("group", "string"), new("title", "string"),
                new("x", "number"), new("y", "number"), new("width", "number"), new("height", "number"),
                new("zoom", "number"), new("opacity", "number"), new("visible", "boolean"),
                new("topmost", "boolean"), new("locked", "boolean"), new("mouseThrough", "boolean"),
                new("canMouseThrough", "boolean"),
                new("thumbnail", "boolean"), new("shadow", "boolean"), new("annotationsVisible", "boolean"),
                new("selectable", "boolean?"), new("ignoreFormat", "boolean?"),
                new("scrollX", "number"), new("scrollY", "number"), new("transparentLayer", "PinTransparentLayer?"),
                new("canIgnoreTransparentInput", "boolean"),
            ]);
            bindings.RegisterObjectSchema("PinUpdate",
            [
                new("x", "number", true), new("y", "number", true), new("zoom", "number", true),
                new("opacity", "number", true), new("visible", "boolean", true), new("topmost", "boolean", true),
                new("locked", "boolean", true), new("mouseThrough", "boolean", true),
                new("group", "string", true), new("title", "string", true),
                new("thumbnail", "boolean", true), new("shadow", "boolean", true),
                new("annotationsVisible", "boolean", true),
                new("selectable", "boolean", true), new("ignoreFormat", "boolean", true),
                new("scrollX", "number", true), new("scrollY", "number", true),
                new("transparentLayer", "PinTransparentLayer", true),
            ]);
            bindings.RegisterObjectSchema("PinClosedInfo",
                [new("id", "string"), new("kind", "string"), new("group", "string"), new("title", "string")]);
            bindings.RegisterObjectSchema("PinGroupInfo",
            [
                new("id", "string"), new("name", "string"), new("color", "string?"),
                new("active", "boolean"), new("open", "number"), new("total", "number"),
            ]);
            var pins = bindings.RegisterTable("pins");
            RegisterContent(bindings, pins, service, root);
            pins.Function("list", Wrap(async (_, token) =>
                    (await Host().ListAsync(token)).Select<PinInfo, object?>(Info).ToList()),
                [], "Result<PinInfo[],ServiceError>");
            pins.Function("get", Wrap(async (args, token) =>
                    await Host().GetAsync((string)args[0]!, token) is { } value ? Info(value) : null),
                [new("id", "string")], "Result<PinInfo?,ServiceError>");
            pins.Function("pinFile", Wrap(async (args, token) =>
                    Info(await Host().PinFileAsync(Path.GetFullPath((string)args[0]!, root), token))),
                [new("path", "string")], "Result<PinInfo,ServiceError>");
            pins.Function("set", Wrap(async (args, token) =>
                    Info(await Host().SetAsync((string)args[0]!, Update((Dictionary<string, object?>)args[1]!),
                        token))),
                [new("id", "string"), new("options", "PinUpdate")], "Result<PinInfo,ServiceError>");
            pins.Function("fit", Wrap(async (args, token) =>
                    Info(await Host().FitAsync((string)args[0]!, Integer(args[1]), Integer(args[2]), token))),
                [new("id", "string"), new("width", "number"), new("height", "number")],
                "Result<PinInfo,ServiceError>");
            pins.Function("close", Wrap(async (args, token) =>
            {
                await Host().CloseAsync((string)args[0]!, (bool)args[1]!, token);
                return null;
            }), [new("id", "string"), new("destroy", "boolean", true, false)], "Result<void,ServiceError>");
            pins.Function("restore", Wrap(async (args, token) =>
                    await Host().RestoreAsync(args[0] as string, token) is { } value ? Info(value) : null),
                [new("id", "string?", true)], "Result<PinInfo?,ServiceError>");
            pins.Function("closed", Wrap(async (_, token) =>
                (await Host().ClosedAsync(token)).Select<PinClosedInfo, object?>(value =>
                    new Dictionary<string, object?>
                    {
                        ["id"] = value.Id, ["kind"] = value.Kind, ["group"] = value.Group, ["title"] = value.Title,
                    }).ToList()), [], "Result<PinClosedInfo[],ServiceError>");
            pins.Function("groups", Wrap(async (_, token) =>
                    (await Host().GroupsAsync(token)).Select<PinGroupInfo, object?>(Group).ToList()),
                [], "Result<PinGroupInfo[],ServiceError>");
            pins.Function("createGroup", Wrap(async (args, token) =>
                    Group(await Host().CreateGroupAsync((string)args[0]!, args[1] as string, token))),
                [new("name", "string"), new("color", "string?", true)], "Result<PinGroupInfo,ServiceError>");
            pins.Function("setGroup", Wrap(async (args, token) =>
                    Group(await Host().SetGroupAsync((string)args[0]!, args[1] as string, args[2] as string, token))),
                [new("id", "string"), new("name", "string?", true), new("color", "string?", true)],
                "Result<PinGroupInfo,ServiceError>");
            pins.Function("activateGroup", Wrap(async (args, token) =>
            {
                await Host().ActivateGroupAsync((string)args[0]!, token);
                return null;
            }), [new("id", "string")], "Result<void,ServiceError>");
            pins.Function("removeGroup", Wrap(async (args, token) =>
            {
                await Host().RemoveGroupAsync((string)args[0]!, args[1] as string, token);
                return null;
            }), [new("id", "string"), new("moveTo", "string?", true)], "Result<void,ServiceError>");
            return pins;

            IPinAutomationService Host()
            {
                return service ?? throw new InvalidOperationException(UiText.Get("EditorNoRuntime"));
            }
        }

        private static ScriptNative Wrap(Func<IReadOnlyList<object?>, CancellationToken, Task<object?>> action)
        {
            return async (args, token) =>
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    var value = await action(args, token);
                    token.ThrowIfCancellationRequested();
                    return new ScriptResult(true, value);
                }
                catch (OperationCanceledException) when (!token.IsCancellationRequested)
                {
                    return new ScriptResult(false,
                        ScriptServiceErrors.Create("unavailable",
                            "The pin operation was canceled because its source became unavailable."));
                }
                catch (Exception error) when (error is not OperationCanceledException)
                {
                    token.ThrowIfCancellationRequested();
                    return new ScriptResult(false, error is KeyNotFoundException
                        ? ScriptServiceErrors.Create("notFound", error.Message)
                        : ScriptServiceErrors.FromException(error));
                }
            };
        }

        private static PinUpdate Update(Dictionary<string, object?> fields)
        {
            return new()
            {
                ScrollX = fields.TryGetValue("scrollX", out var scrollX) ? ScriptEngine.Number(scrollX) : null,
                ScrollY = fields.TryGetValue("scrollY", out var scrollY) ? ScriptEngine.Number(scrollY) : null,
                TransparentLayer = fields.GetValueOrDefault("transparentLayer") as PinTransparentLayer?,
                X = fields.TryGetValue("x", out var x) ? Integer(x) : null,
                Y = fields.TryGetValue("y", out var y) ? Integer(y) : null,
                Zoom = fields.TryGetValue("zoom", out var zoom) ? ScriptEngine.Number(zoom) : null,
                Opacity = fields.TryGetValue("opacity", out var opacity) ? ScriptEngine.Number(opacity) : null,
                Visible = fields.GetValueOrDefault("visible") as bool?,
                Topmost = fields.GetValueOrDefault("topmost") as bool?,
                Locked = fields.GetValueOrDefault("locked") as bool?,
                MouseThrough = fields.GetValueOrDefault("mouseThrough") as bool?,
                Group = fields.GetValueOrDefault("group") as string,
                Title = fields.GetValueOrDefault("title") as string,
                Thumbnail = fields.GetValueOrDefault("thumbnail") as bool?,
                Shadow = fields.GetValueOrDefault("shadow") as bool?,
                AnnotationsVisible = fields.GetValueOrDefault("annotationsVisible") as bool?,
                Selectable = fields.GetValueOrDefault("selectable") as bool?,
                IgnoreFormat = fields.GetValueOrDefault("ignoreFormat") as bool?,
            };
        }

        private static int Integer(object? value)
        {
            var number = ScriptEngine.Number(value);
            if (!double.IsFinite(number) || !number.Equals(Math.Truncate(number)) ||
                number < int.MinValue || number > int.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(value));
            return (int)number;
        }

        private static Dictionary<string, object?> Info(PinInfo value)
        {
            return new()
            {
                ["id"] = value.Id, ["kind"] = value.Kind, ["group"] = value.Group, ["title"] = value.Title,
                ["x"] = (double)value.X, ["y"] = (double)value.Y,
                ["width"] = (double)value.Width, ["height"] = (double)value.Height,
                ["zoom"] = value.Zoom, ["opacity"] = value.Opacity, ["visible"] = value.Visible,
                ["topmost"] = value.Topmost, ["locked"] = value.Locked,
                ["mouseThrough"] = value.MouseThrough, ["canMouseThrough"] = value.CanMouseThrough,
                ["thumbnail"] = value.Thumbnail, ["shadow"] = value.Shadow,
                ["annotationsVisible"] = value.AnnotationsVisible,
                ["selectable"] = value.Selectable, ["ignoreFormat"] = value.IgnoreFormat,
                ["scrollX"] = value.ScrollX, ["scrollY"] = value.ScrollY,
                ["transparentLayer"] = value.TransparentLayer,
                ["canIgnoreTransparentInput"] = value.CanIgnoreTransparentInput,
            };
        }

        private static Dictionary<string, object?> Group(PinGroupInfo value)
        {
            return new()
            {
                ["id"] = value.Id, ["name"] = value.Name, ["color"] = value.Color,
                ["active"] = value.Active, ["open"] = (double)value.Open, ["total"] = (double)value.Total,
            };
        }
    }
}
