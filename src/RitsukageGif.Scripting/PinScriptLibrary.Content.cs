namespace RitsukageGif.Scripting
{
    public static partial class PinScriptLibrary
    {
        private static void RegisterContent(ScriptBindingRegistry bindings, ScriptTableBinding pins,
            IPinAutomationService? service, string root)
        {
            bindings.RegisterEnum("PinOutputView", Enum.GetValues<PinOutputView>()
                .ToDictionary(value => value.ToString(), value => value));
            bindings.RegisterEnum("PinCopyKind", Enum.GetValues<PinCopyKind>()
                .ToDictionary(value => value.ToString(), value => value));
            bindings.RegisterEnum("PinTransform", Enum.GetValues<PinTransform>()
                .ToDictionary(value => value.ToString(), value => value));
            bindings.RegisterEnum("PinArrangement", Enum.GetValues<PinArrangement>()
                .ToDictionary(value => value.ToString(), value => value));
            bindings.RegisterObjectSchema("PinSnapshot",
                [new("image", "Bytes"), new("hdr", "Bytes?"), new("color", "OutputColorMode")]);
            bindings.RegisterObjectSchema("PinPlaybackState",
            [
                new("playing", "boolean"), new("speed", "number"), new("frameIndex", "number"), new("frames", "number"),
            ]);
            ScriptParameter[] outputParameters =
            [
                new("id", "string"), new("view", "PinOutputView", true, PinOutputView.Preferred),
                new("color", "OutputColorMode", true, OutputColorMode.Sdr),
            ];
            pins.Function("snapshot", Wrap(async (args, token) =>
            {
                var value = await Host().SnapshotAsync((string)args[0]!, (PinOutputView)args[1]!,
                    (OutputColorMode)args[2]!, token);
                return new Dictionary<string, object?>
                {
                    ["image"] = new ScriptBytes(value.Image),
                    ["hdr"] = value.Hdr is { } hdr ? new ScriptBytes(hdr) : null, ["color"] = value.Color,
                };
            }), outputParameters, "Result<PinSnapshot,ServiceError>");
            pins.Function("save", Wrap(async (args, token) =>
                    (await Host().SaveAsync((string)args[0]!, Path.GetFullPath((string)args[1]!, root),
                        (PinOutputView)args[2]!, (OutputColorMode)args[3]!, token)).Cast<object?>().ToList()),
                [new("id", "string"), new("path", "string"), .. outputParameters.Skip(1)],
                "Result<string[],ServiceError>");
            pins.Function("copy", Wrap(async (args, token) =>
            {
                await Host().CopyAsync((string)args[0]!, (PinCopyKind)args[1]!, (PinOutputView)args[2]!, token);
                return null;
            }), [
                new("id", "string"), new("kind", "PinCopyKind", true, PinCopyKind.Image),
                new("view", "PinOutputView", true, PinOutputView.Preferred),
            ], "Result<void,ServiceError>");
            pins.Function("focus", Wrap(async (args, token) =>
            {
                await Host().FocusAsync((string)args[0]!, token);
                return null;
            }), [new("id", "string")], "Result<void,ServiceError>");
            pins.Function("files", Wrap(async (args, token) =>
                    (await Host().FilesAsync((string)args[0]!, token)).Cast<object?>().ToList()),
                [new("id", "string")], "Result<string[],ServiceError>");
            pins.Function("text", Wrap(async (args, token) => await Host().TextAsync((string)args[0]!, token)),
                [new("id", "string")], "Result<string?,ServiceError>");
            pins.Function("html", Wrap(async (args, token) => await Host().HtmlAsync((string)args[0]!, token)),
                [new("id", "string")], "Result<string?,ServiceError>");
            pins.Function("createImage", Wrap(async (args, token) =>
                    Info(await Host().CreateImageAsync(((ScriptBytes)args[0]!).Data, token))),
                [new("image", "Bytes")], "Result<PinInfo,ServiceError>");
            pins.Function("createText", Wrap(async (args, token) =>
                    Info(await Host().CreateTextAsync((string)args[0]!, token))),
                [new("text", "string")], "Result<PinInfo,ServiceError>");
            pins.Function("createHtml", Wrap(async (args, token) =>
                    Info(await Host().CreateHtmlAsync((string)args[0]!, args[1] as string, token))),
                [new("html", "string"), new("text", "string?", true)], "Result<PinInfo,ServiceError>");
            pins.Function("createColor", Wrap(async (args, token) =>
                    Info(await Host().CreateColorAsync((string)args[0]!, token))),
                [new("color", "string")], "Result<PinInfo,ServiceError>");
            pins.Function("createFiles", Wrap(async (args, token) =>
                {
                    string[] paths =
                        [.. ((List<object?>)args[0]!).Cast<string>().Select(path => Path.GetFullPath(path, root))];
                    return Info(await Host().CreateFilesAsync(paths, token));
                }),
                [new("paths", "string[]")], "Result<PinInfo,ServiceError>");
            pins.Function("createFormula", Wrap(async (args, token) =>
                    Info(await Host().CreateFormulaAsync((string)args[0]!, token))),
                [new("latex", "string")], "Result<PinInfo,ServiceError>");
            pins.Function("duplicate", Wrap(async (args, token) =>
                    Info(await Host().DuplicateAsync((string)args[0]!, token))),
                [new("id", "string")], "Result<PinInfo,ServiceError>");
            pins.Function("transform", Wrap(async (args, token) =>
                    Info(await Host().TransformAsync((string)args[0]!, (PinTransform)args[1]!, token))),
                [new("id", "string"), new("transform", "PinTransform")], "Result<PinInfo,ServiceError>");
            pins.Function("adjustBrightness", Wrap(async (args, token) =>
                    Info(await Host().AdjustBrightnessAsync((string)args[0]!, ScriptEngine.Number(args[1]), token))),
                [new("id", "string"), new("delta", "number")], "Result<PinInfo,ServiceError>");
            pins.Function("resetProcessing", Wrap(async (args, token) =>
                    Info(await Host().ResetProcessingAsync((string)args[0]!, token))),
                [new("id", "string")], "Result<PinInfo,ServiceError>");
            pins.Function("arrange", Wrap(async (args, token) =>
                {
                    var items = (List<object?>)args[0]!;
                    var ids = new string[items.Count];
                    for (var i = 0; i < ids.Length; i++) ids[i] = (string)items[i]!;
                    var arranged = await Host().ArrangeAsync(ids,
                        (PinArrangement)args[1]!, (bool)args[2]!, token);
                    List<object?> values = [.. arranged.Select<PinInfo, object?>(Info)];
                    return values;
                }),
                [
                    new("ids", "string[]"), new("arrangement", "PinArrangement"),
                    new("allowNegativeSpacing", "boolean", true, false),
                ], "Result<PinInfo[],ServiceError>");
            pins.Function("originalImage", Wrap(async (args, token) =>
                    new ScriptBytes(await Host().OriginalImageAsync((string)args[0]!, token))),
                [new("id", "string")], "Result<Bytes,ServiceError>");
            pins.Function("playback", Wrap(async (args, token) => PlaybackValue(await Host().PlaybackAsync(
                    (string)args[0]!, args[1] as bool?, args[2] is null ? null : ScriptEngine.Number(args[2]), token))),
                [new("id", "string"), new("paused", "boolean?", true), new("speed", "number?", true)],
                "Result<PinPlaybackState,ServiceError>");
            pins.Function("step", Wrap(async (args, token) =>
                    PlaybackValue(await Host().StepAsync((string)args[0]!, Integer(args[1]), token))),
                [new("id", "string"), new("direction", "number")], "Result<PinPlaybackState,ServiceError>");
            pins.Function("thumbnailRegion", Wrap(async (args, token) =>
            {
                var fields = (Dictionary<string, object?>)args[1]!;
                return Info(await Host().SetThumbnailRegionAsync((string)args[0]!,
                    new(Integer(fields["x"]), Integer(fields["y"]), Integer(fields["width"]),
                        Integer(fields["height"])), token));
            }), [new("id", "string"), new("region", "EffectRect")], "Result<PinInfo,ServiceError>");
            return;

            IPinAutomationService Host()
            {
                return service ?? throw new InvalidOperationException(UiText.Get("EditorNoRuntime"));
            }
        }

        private static Dictionary<string, object?> PlaybackValue(PinPlaybackState value)
        {
            return new()
            {
                ["playing"] = value.Playing, ["speed"] = value.Speed,
                ["frameIndex"] = (double)value.FrameIndex, ["frames"] = (double)value.Frames,
            };
        }
    }
}
