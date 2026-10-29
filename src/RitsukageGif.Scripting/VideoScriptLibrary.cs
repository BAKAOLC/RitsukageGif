namespace RitsukageGif.Scripting
{
    public static class VideoScriptLibrary
    {
        public static void Register(ScriptBindingRegistry bindings, IVideoRecordingService? service = null,
            string? baseDirectory = null)
        {
            var root = Path.GetFullPath(baseDirectory ?? Environment.CurrentDirectory);
            ScriptServiceErrors.Register(bindings);
            bindings.RegisterEnum("OutputColorMode", Enum.GetValues<OutputColorMode>()
                .ToDictionary(value => value.ToString(), value => value));
            bindings.RegisterEnum("HdrToneMapMode", Enum.GetValues<HdrToneMapMode>()
                .ToDictionary(value => value.ToString(), value => value));
            bindings.RegisterEnum("VideoFormat", new Dictionary<string, string>
            {
                ["Mp4"] = "mp4", ["WebM"] = "webm",
            });
            bindings.RegisterObjectSchema("VideoAudioInput", [new("input", "string"), new("format", "string", true)]);
            bindings.RegisterObjectSchema("VideoOptions",
            [
                new("format", "VideoFormat", true), new("fps", "number", true), new("divisor", "number", true),
                new("includeCursor", "boolean", true), new("quality", "number", true),
                new("color", "OutputColorMode", true), new("toneMap", "HdrToneMapMode", true),
                new("overwrite", "boolean", true),
                new("systemAudio", "boolean", true), new("systemDevice", "string", true),
                new("microphone", "boolean", true), new("microphoneDevice", "string", true),
                new("audioInputs", "VideoAudioInput[]", true),
            ]);
            bindings.RegisterObjectSchema("VideoHandle",
                [new("id", "string"), new("path", "string"), new("cancelled", "boolean"), new("outputs", "string[]")]);
            bindings.RegisterObjectSchema("VideoCapabilities",
            [
                new("available", "boolean"), new("mp4", "boolean"), new("webm", "boolean"),
                new("mp4Audio", "boolean"), new("webmAudio", "boolean"), new("nativeAudio", "boolean"),
                new("audioInputFormats", "string[]"), new("mp4Hdr", "boolean"), new("webmHdr", "boolean"),
            ]);
            var table = bindings.RegisterTable("video");
            table.Function("capabilities", async (_, token) =>
            {
                try
                {
                    var value = await Host().GetCapabilitiesAsync(token);
                    return new ScriptResult(true, new Dictionary<string, object?>
                    {
                        ["available"] = value.Available, ["mp4"] = value.Mp4, ["webm"] = value.WebM,
                        ["mp4Audio"] = value.Mp4Audio, ["webmAudio"] = value.WebMAudio,
                        ["nativeAudio"] = value.NativeAudio, ["mp4Hdr"] = value.Mp4Hdr, ["webmHdr"] = value.WebMHdr,
                        ["audioInputFormats"] = value.AudioInputFormats.Select(object? (format) => format).ToList(),
                    });
                }
                catch (Exception error) when (error is not OperationCanceledException)
                {
                    token.ThrowIfCancellationRequested();
                    return new ScriptResult(false, ScriptServiceErrors.FromException(error));
                }
            }, [], "Result<VideoCapabilities,ServiceError>");
            table.Function("start", async (args, token) =>
            {
                var output = (string)args[0]!;
                try
                {
                    var fields = args[1] as Dictionary<string, object?> ?? [];
                    var audio = new List<VideoCaptureAudioInput>();
                    if (fields.GetValueOrDefault("audioInputs") is List<object?> inputs)
                        foreach (var input in inputs)
                        {
                            var item = (Dictionary<string, object?>)input!;
                            var inputName = (string)item["input"]!;
                            ArgumentException.ThrowIfNullOrWhiteSpace(inputName);
                            var format = item.GetValueOrDefault("format") as string;
                            if (string.IsNullOrWhiteSpace(format) && !Path.IsPathRooted(inputName) &&
                                !Uri.TryCreate(inputName, UriKind.Absolute, out _))
                                inputName = Path.GetFullPath(inputName, root);
                            audio.Add(new(inputName, format));
                        }

                    var options = new VideoCaptureOptions
                    {
                        ColorMode = fields.GetValueOrDefault("color") as OutputColorMode?,
                        ToneMap = fields.GetValueOrDefault("toneMap") as HdrToneMapMode?,
                        Format = fields.GetValueOrDefault("format") as string ?? "mp4",
                        Fps = Integer(fields, "fps"), Divisor = Integer(fields, "divisor"),
                        IncludeCursor = fields.GetValueOrDefault("includeCursor") as bool?,
                        Quality = Integer(fields, "quality") ?? 23,
                        Overwrite = fields.GetValueOrDefault("overwrite") is true,
                        SystemAudio = fields.GetValueOrDefault("systemAudio") is true,
                        SystemDevice = fields.GetValueOrDefault("systemDevice") as string,
                        Microphone = fields.GetValueOrDefault("microphone") is true,
                        MicrophoneDevice = fields.GetValueOrDefault("microphoneDevice") as string,
                        AudioInputs = audio,
                    };
                    return new ScriptResult(true,
                        Handle(await Host().StartAsync(Path.GetFullPath(output, root), options, token)));
                }
                catch (Exception error) when (error is not OperationCanceledException)
                {
                    token.ThrowIfCancellationRequested();
                    return new ScriptResult(false, ScriptServiceErrors.FromException(error, output));
                }
            }, [new("output", "string"), new("options", "VideoOptions", true)], "Result<VideoHandle,ServiceError>");
            table.Function("stop", async (args, token) =>
            {
                try
                {
                    return new ScriptResult(true,
                        Handle(await Host().StopAsync((string)args[0]!, args[1] is true, token)));
                }
                catch (Exception error) when (error is not OperationCanceledException)
                {
                    token.ThrowIfCancellationRequested();
                    return new ScriptResult(false, ScriptServiceErrors.FromException(error));
                }
            }, [new("id", "string"), new("cancel", "boolean", true)], "Result<VideoHandle,ServiceError>");
            table.Function("wait", async (args, token) =>
            {
                try
                {
                    return new ScriptResult(true, Handle(await Host().WaitAsync((string)args[0]!, token)));
                }
                catch (Exception error) when (error is not OperationCanceledException)
                {
                    token.ThrowIfCancellationRequested();
                    return new ScriptResult(false, ScriptServiceErrors.FromException(error));
                }
            }, [new("id", "string")], "Result<VideoHandle,ServiceError>");
            return;

            IVideoRecordingService Host()
            {
                return service ?? throw new InvalidOperationException(UiText.Get("EditorNoRuntime"));
            }
        }

        private static Dictionary<string, object?> Handle(VideoCaptureHandle value)
        {
            return new()
            {
                ["id"] = value.Id, ["path"] = value.Path, ["cancelled"] = value.Cancelled,
                ["outputs"] = (value.Outputs ?? [value.Path]).Select(object? (path) => path).ToList(),
            };
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
