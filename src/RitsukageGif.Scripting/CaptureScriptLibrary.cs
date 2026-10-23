namespace RitsukageGif.Scripting
{
    public static class CaptureScriptLibrary
    {
        public static ScriptBindingRegistry CreateBindings(PresetScriptSession? host = null,
            string? baseDirectory = null)
        {
            var bindings = StandardScriptLibrary.CreateBindings(host?.DebugSession);
            ParameterScriptLibrary.Register(bindings, host?.Parameters, host is null ? null : host.WaitParametersBound);
            InputScriptLibrary.Register(bindings, host?.InputReader);
            FileScriptLibrary.Register(bindings, baseDirectory ?? host?.SourceDirectory);
            MediaScriptLibrary.Register(bindings, host?.MediaService, baseDirectory ?? host?.SourceDirectory);
            ImageScriptLibrary.Register(bindings, host?.ImageService, baseDirectory ?? host?.SourceDirectory);
            var settings = new RecordingSettingsBinding(bindings);
            settings.Register();
            var outputs = new RecordingOutputsBinding(bindings, settings,
                Path.GetFullPath(baseDirectory ?? host?.SourceDirectory ?? Environment.CurrentDirectory));
            outputs.Register();
            bindings.RegisterEnum("Easing", new Dictionary<string, string>
            {
                ["Linear"] = "linear", ["EaseIn"] = "ease-in", ["EaseOut"] = "ease-out", ["EaseInOut"] = "ease-in-out",
                ["ease-in"] = "ease-in", ["ease-out"] = "ease-out", ["ease-in-out"] = "ease-in-out"
            });
            bindings.RegisterEnum("CaptureEvent", new Dictionary<string, string>
            {
                ["Found"] = "found", ["Lost"] = "lost", ["Closed"] = "closed", ["Moved"] = "moved",
                ["Resized"] = "resized",
                ["Title"] = "title", ["Saved"] = "saved", ["Cancelled"] = "cancelled", ["Stopped"] = "stopped",
                ["Error"] = "error"
            });
            bindings.RegisterObjectSchema("RecordingStatus",
            [
                new("exists", "boolean"), new("recording", "boolean"), new("busy", "boolean"), new("idle", "boolean"),
                new("title", "string"), new("width", "number"), new("height", "number"), new("elapsed", "number"),
                new("outputs", "string[]"), new("streams", "RecordingStreamStatus[]")
            ]);
            bindings.RegisterObjectSchema("RecordingStreamStatus",
            [
                new("name", "string"), new("state", "string"), new("captured", "number"),
                new("encoded", "number"), new("elapsed", "number"), new("path", "string"), new("error", "string?")
            ]);
            bindings.RegisterObjectSchema("SceneLayer",
            [
                new("x", "number"), new("y", "number"), new("width", "number"), new("height", "number"),
                new("visible", "boolean"), new("opacity", "number"), new("effects", "PixelEffect[]"),
                new("playback", "PlaybackState?")
            ]);
            bindings.RegisterObjectSchema("PlaybackOptions",
            [
                new("position", "number", true), new("speed", "number", true), new("paused", "boolean", true),
                new("loop", "boolean", true), new("holdLast", "boolean", true)
            ]);
            bindings.RegisterObjectSchema("PlaybackState",
            [
                new("position", "number"), new("speed", "number"), new("paused", "boolean"),
                new("loop", "boolean"), new("holdLast", "boolean")
            ]);
            PresetScriptSession Host() => host ?? throw new InvalidOperationException(UiText.Get("EditorNoRuntime"));
            var recording = bindings.RegisterTable("record");
            ScriptNative start = (args, token) => Host().ExecuteBoundAction("start",
                args.Count == 0 || args[0] is null ? null : settings.Read(args[0]), token);
            ScriptParameter[] startParameters = [new("options", RecordingSettingsBinding.SchemaName, true)];
            recording.Function("start", start, startParameters, "void");
            recording.Function("startMany", (args, token) => Host().StartManyBound(outputs.Read(args[0]), token),
                [new("outputs", RecordingOutputsBinding.SchemaName + "[]")], "void");
            recording.Function("status", (_, token) => Host().StatusBound(token), [], "RecordingStatus");
            recording.Function("outputs", (_, token) => Host().OutputsBound(token), [], "RecordingStreamStatus[]");
            bindings.RegisterFunction("start", start, startParameters, "void");
            foreach (var name in new[] { "stop", "cancel", "copy", "select", "show", "hide" })
            {
                ScriptNative action = (_, token) => Host().ExecuteBoundAction(name, null, token);
                recording.Function(name, action, [], "void");
                bindings.RegisterFunction(name, action, [], "void");
            }

            bindings.RegisterFunction("wait", (args, token) => Host().WaitBound(args, token),
                [new("seconds", "number")], "void");
            bindings.RegisterFunction("waitUntil", (args, token) => Host().WaitUntilBound(args, token),
                [new("condition", "function"), new("timeout", "number", true, 30d)], "void");
            bindings.RegisterFunction("status", (_, token) => Host().StatusBound(token), [], "RecordingStatus");
            bindings.RegisterFunction("breakpoint", ScriptEngine.Native(_ => Host().BreakpointBound()), [], "void");
            var scene = bindings.RegisterTable("scene");
            bindings.RegisterObjectSchema("SceneRect",
                [new("x", "number"), new("y", "number"), new("width", "number"), new("height", "number")]);
            bindings.RegisterObjectSchema("SceneUpdate",
            [
                new("placement", "SceneRect", true), new("crop", "SceneRect", true),
                new("visible", "boolean", true), new("opacity", "number", true), new("effects", "PixelEffect[]", true),
                new("playback", "PlaybackOptions", true)
            ]);
            scene.Function("set", ScriptEngine.Native(args => Host().SetLayerBound((string)args[0]!,
                    (Dictionary<string, object?>)args[1]!)),
                [new("name", "string"), new("options", "SceneUpdate")], "void");
            ScriptParameter[] rectangle =
            [
                new("name", "string"), new("x", "number"), new("y", "number"), new("width", "number"),
                new("height", "number")
            ];
            scene.Function("move", ScriptEngine.Native(args => Host().UpdateLayerBound("move", args)), rectangle,
                "void");
            scene.Function("crop", ScriptEngine.Native(args => Host().UpdateLayerBound("crop", args)), rectangle,
                "void");
            scene.Function("animate", ScriptEngine.Native(args => Host().UpdateLayerBound("animate", args)),
                [.. rectangle, new("seconds", "number"), new("easing", "Easing", true, "linear")], "void");
            scene.Function("visible", ScriptEngine.Native(args => Host().UpdateLayerBound("visible", args)),
                [new("name", "string"), new("visible", "boolean")], "void");
            scene.Function("opacity", ScriptEngine.Native(args => Host().UpdateLayerBound("opacity", args)),
                [new("name", "string"), new("opacity", "number")], "void");
            scene.Function("get", ScriptEngine.Native(args => Host().ReadLayerBound((string)args[0]!)),
                [new("name", "string")], "SceneLayer");
            scene.Function("setFor", ScriptEngine.Native(args => Host().OutputScene((string)args[0]!)
                    .SetLayerBound((string)args[1]!, (Dictionary<string, object?>)args[2]!)),
                [new("output", "string"), new("name", "string"), new("options", "SceneUpdate")], "void");
            scene.Function("effects", ScriptEngine.Native(args => Host().SetLayerBound((string)args[0]!,
                    new Dictionary<string, object?> { ["effects"] = args[1] })),
                [new("name", "string"), new("effects", "PixelEffect[]")], "void");
            scene.Function("effectsFor", ScriptEngine.Native(args => Host().OutputScene((string)args[0]!)
                    .SetLayerBound((string)args[1]!, new Dictionary<string, object?> { ["effects"] = args[2] })),
                [new("output", "string"), new("name", "string"), new("effects", "PixelEffect[]")], "void");
            scene.Function("playback", ScriptEngine.Native(args => Host().SetLayerBound((string)args[0]!,
                    new Dictionary<string, object?> { ["playback"] = args[1] })),
                [new("name", "string"), new("options", "PlaybackOptions")], "void");
            scene.Function("playbackFor", ScriptEngine.Native(args => Host().OutputScene((string)args[0]!)
                    .SetLayerBound((string)args[1]!, new Dictionary<string, object?> { ["playback"] = args[2] })),
                [new("output", "string"), new("name", "string"), new("options", "PlaybackOptions")], "void");
            scene.Function("getFor", ScriptEngine.Native(args => Host().OutputScene((string)args[0]!)
                    .ReadLayerBound((string)args[1]!)),
                [new("output", "string"), new("name", "string")], "SceneLayer");
            foreach (var operation in new[] { "move", "crop", "animate", "visible", "opacity" })
            {
                ScriptParameter[] parameters = operation switch
                {
                    "visible" => [new("name", "string"), new("visible", "boolean")],
                    "opacity" => [new("name", "string"), new("opacity", "number")],
                    "animate" => [.. rectangle, new("seconds", "number"), new("easing", "Easing", true, "linear")],
                    _ => rectangle
                };
                scene.Function(operation + "For", ScriptEngine.Native(args => Host().OutputScene((string)args[0]!)
                        .UpdateLayerBound(operation, args.Skip(1).ToArray())),
                    [new("output", "string"), .. parameters], "void");
            }

            var events = bindings.RegisterTable("events");
            events.Function("on", ScriptEngine.Native(args => Host().SubscribeBound((string)args[0]!, args[1])),
                [new("name", "CaptureEvent|string"), new("handler", "function")], "void");
            events.Function("off", ScriptEngine.Native(args => Host().UnsubscribeBound((string)args[0]!)),
                [new("name", "CaptureEvent|string")], "void");
            events.Function("emit", ScriptEngine.Native(args => Host().EmitBound((string)args[0]!)),
                [new("name", "CaptureEvent|string")], "void");
            events.Function("pump", (_, token) => Host().PumpBound(token), [], "void");
            return bindings;
        }
    }
}
