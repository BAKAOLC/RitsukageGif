using System.Diagnostics;

namespace RitsukageGif.Scripting
{
    public static partial class LongCaptureScriptLibrary
    {
        private const int MaximumEncodedBytes = 64 * 1024 * 1024;

        public static void Register(ScriptBindingRegistry bindings, ILongCaptureService? service = null,
            string? baseDirectory = null)
        {
            var root = Path.GetFullPath(baseDirectory ?? Environment.CurrentDirectory);
            ScriptServiceErrors.Register(bindings);
            RegisterSchemas(bindings);
            var capture = bindings.RegisterType<ILongCaptureSession>("LongCaptureSession");
            capture.Method("start", Operation(async (session, _, token) =>
            {
                await session.StartAsync(token);
                return true;
            }), [], "Result<boolean,ServiceError>");
            capture.Method("pause", Operation(async (session, _, token) =>
            {
                await session.PauseAsync(token);
                return true;
            }), [], "Result<boolean,ServiceError>");
            capture.Method("capture", Operation(async (session, _, token) => Match(await session.CaptureAsync(token))),
                [], "Result<LongCaptureMatch,ServiceError>");
            capture.Method("feed", Operation(async (session, args, token) =>
            {
                if (args[0] is ScrollCaptureImage frame)
                {
                    var color = new FrameColorInfo(false);
                    var frames = new CapturedFrameSet(new(frame.Width, frame.Height, checked(frame.Width * 4),
                        CapturePixelFormat.Bgra8Srgb, frame.Pixels, color, CaptureAlphaMode.Straight, 0,
                        [new(new(0, 0, frame.Width, frame.Height), color)]), frame.Hdr, Stopwatch.GetTimestamp());
                    return Match(await session.FeedFramesAsync(frames, token));
                }

                var image = await ReadImageAsync(args[0], root, token);
                return Match(await session.FeedAsync(image, token));
            }), [new("image", "string|Bytes|LongCaptureImage")], "Result<LongCaptureMatch,ServiceError>");
            capture.Method("move", Operation(async (session, args, token) =>
            {
                await session.MoveAsync(Integer(args[0]), token);
                return true;
            }), [new("position", "number")], "Result<boolean,ServiceError>");
            capture.Method("state", Operation(async (session, _, token) => State(await session.GetStateAsync(token))),
                [], "Result<LongCaptureState,ServiceError>");
            capture.Method("read", Operation(async (session, _, token) => await session.ReadAsync(token)),
                [], "Result<LongCaptureImage,ServiceError>");
            capture.Method("stop", Operation(async (session, _, token) => await session.StopAsync(token)),
                [], "Result<LongCaptureImage,ServiceError>");
            capture.Method("save", Operation(async (session, args, token) =>
                {
                    var output = Output(Path.GetFullPath((string)args[0]!, root), args[1]);
                    return MediaScriptLibrary.Info(await session.SaveAsync(output, token));
                }), [new("output", "string"), new("options", "LongCaptureSaveOptions", true)],
                "Result<MediaInfo,ServiceError>");
            capture.Method("close", Operation(async (session, _, _) =>
            {
                await session.DisposeAsync();
                return true;
            }), [], "Result<boolean,ServiceError>");

            var imageType = bindings.RegisterType<ScrollCaptureImage>("LongCaptureImage");
            imageType.Property("width", "number", image => image.Width);
            imageType.Property("height", "number", image => image.Height);
            imageType.Property("pixels", "Bytes", image => new ScriptBytes(image.Pixels));
            imageType.Property("pixelFormat", "string", _ => "Bgra8Srgb");
            imageType.Property("stride", "number", image => checked(image.Width * 4));
            imageType.Property("alphaMode", "string", _ => "Straight");
            imageType.Property("hdr", "LongCaptureHdrFrame?", image => image.Hdr);
            var hdrType = bindings.RegisterType<CapturedFrame>("LongCaptureHdrFrame");
            hdrType.Property("width", "number", frame => frame.Width);
            hdrType.Property("height", "number", frame => frame.Height);
            hdrType.Property("stride", "number", frame => frame.Stride);
            hdrType.Property("pixels", "Bytes", frame => new ScriptBytes(frame.Pixels));
            hdrType.Property("pixelFormat", "string", frame => frame.PixelFormat.ToString());
            hdrType.Property("alphaMode", "string", frame => frame.AlphaMode.ToString());
            hdrType.Property("isHdrSource", "boolean", frame => frame.ColorInfo.IsHdrSource);
            hdrType.Property("sdrWhiteNits", "number", frame => frame.ColorInfo.SdrWhiteNits);
            hdrType.Property("referenceWhiteNits", "number", _ => FrameColorInfo.ScRgbReferenceWhiteNits);
            hdrType.Property("colorRegions", "LongCaptureColorRegion[]", frame =>
                frame.ColorRegions.Select(object? (region) =>
                    new Dictionary<string, object?>
                    {
                        ["x"] = (double)region.Region.X, ["y"] = (double)region.Region.Y,
                        ["width"] = (double)region.Region.Width, ["height"] = (double)region.Region.Height,
                        ["isHdrSource"] = region.ColorInfo.IsHdrSource,
                        ["sdrWhiteNits"] = (double)region.ColorInfo.SdrWhiteNits,
                    }).ToList());

            var table = bindings.RegisterTable("longCapture");
            table.Function("create", async (args, token) =>
            {
                try
                {
                    var host = service ?? throw new InvalidOperationException(UiText.Get("EditorNoRuntime"));
                    return new ScriptResult(true, await host.CreateAsync(Options(args[0]), token));
                }
                catch (Exception error) when (error is not OperationCanceledException)
                {
                    token.ThrowIfCancellationRequested();
                    return new ScriptResult(false, ScriptServiceErrors.FromException(error));
                }
            }, [new("options", "LongCaptureOptions")], "Result<LongCaptureSession,ServiceError>");
        }

        private static ScriptHostMethod<ILongCaptureSession> Operation(
            Func<ILongCaptureSession, IReadOnlyList<object?>, CancellationToken, Task<object?>> operation)
        {
            return async (session, args, token) =>
            {
                try
                {
                    return new ScriptResult(true, await operation(session, args, token));
                }
                catch (Exception error) when (error is not OperationCanceledException)
                {
                    token.ThrowIfCancellationRequested();
                    return new ScriptResult(false, ScriptServiceErrors.FromException(error));
                }
            };
        }

        private static async Task<ReadOnlyMemory<byte>> ReadImageAsync(object? source, string root,
            CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (source is ScriptBytes bytes)
            {
                if (bytes.Length is <= 0 or > MaximumEncodedBytes)
                    throw new ArgumentOutOfRangeException(nameof(source));
                return bytes.Data;
            }

            var path = Path.GetFullPath((string)source!, root);
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
            if (stream.Length is <= 0 or > MaximumEncodedBytes)
                throw new ArgumentOutOfRangeException(nameof(source));
            var data = new byte[checked((int)stream.Length)];
            await stream.ReadExactlyAsync(data, token);
            return data;
        }
    }
}
