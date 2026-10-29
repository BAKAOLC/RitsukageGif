namespace RitsukageGif.Scripting
{
    public static partial class RecognitionScriptLibrary
    {
        private const int MaximumEncodedBytes = 64 * 1024 * 1024;

        public static void Register(ScriptBindingRegistry bindings, ITextRecognitionService? textService = null,
            IBarcodeRecognitionService? barcodeService = null, string? baseDirectory = null,
            IPrivacyDetectionService? privacyService = null)
        {
            var root = Path.GetFullPath(baseDirectory ?? Environment.CurrentDirectory);
            ScriptServiceErrors.Register(bindings);
            RegisterSchemas(bindings);
            RegisterPrivacy(bindings, privacyService, root);
            var ocr = bindings.RegisterTable("ocr");
            ocr.Function("capabilities", async (_, token) =>
            {
                try
                {
                    token.ThrowIfCancellationRequested();
                    var capabilities = textService is null
                        ? new(false, 0, [], UiText.Get("EditorNoRuntime"))
                        : await textService.GetCapabilitiesAsync(token);
                    token.ThrowIfCancellationRequested();
                    return new ScriptResult(true, Capabilities(capabilities));
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    token.ThrowIfCancellationRequested();
                    return new ScriptResult(false, ScriptServiceErrors.FromException(exception));
                }
            }, [], "Result<OcrCapabilities,ServiceError>");
            ocr.Function("recognize", async (args, token) =>
                {
                    try
                    {
                        var service = textService ?? throw new InvalidOperationException(UiText.Get("EditorNoRuntime"));
                        var image = await ReadImage(args[0], root, token);
                        var result = await service.RecognizeImageAsync(image, (string?)args[1], token);
                        token.ThrowIfCancellationRequested();
                        return new ScriptResult(true, Recognition(result));
                    }
                    catch (Exception exception) when (exception is not OperationCanceledException)
                    {
                        token.ThrowIfCancellationRequested();
                        return new ScriptResult(false, ScriptServiceErrors.FromException(exception, args[0] as string));
                    }
                }, [new("image", "string|Bytes"), new("language", "string?", true)],
                "Result<OcrResult,ServiceError>");
            var barcode = bindings.RegisterTable("barcode");
            barcode.Function("recognize", async (args, token) =>
            {
                try
                {
                    var service = barcodeService ?? throw new InvalidOperationException(UiText.Get("EditorNoRuntime"));
                    var image = await ReadImage(args[0], root, token);
                    var result = await service.RecognizeAsync(image, token);
                    token.ThrowIfCancellationRequested();
                    return new ScriptResult(true, result.Select(object? (item) => Barcode(item)).ToList());
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    token.ThrowIfCancellationRequested();
                    return new ScriptResult(false, ScriptServiceErrors.FromException(exception, args[0] as string));
                }
            }, [new("image", "string|Bytes")], "Result<BarcodeResult[],ServiceError>");
        }

        private static async ValueTask<ReadOnlyMemory<byte>> ReadImage(object? source, string root,
            CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (source is ScriptBytes bytes)
            {
                if (bytes.Length is <= 0 or > MaximumEncodedBytes)
                    throw new ArgumentOutOfRangeException(nameof(source));
                return bytes.Data;
            }

            if (source is not string path) throw new ArgumentException(UiText.Get("ScriptTypeError"));
            await using var stream = new FileStream(Path.GetFullPath(path, root), FileMode.Open, FileAccess.Read,
                FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
            if (stream.Length is <= 0 or > MaximumEncodedBytes)
                throw new ArgumentOutOfRangeException(nameof(source));
            var data = new byte[checked((int)stream.Length)];
            await stream.ReadExactlyAsync(data, token);
            var trailing = new byte[1];
            if (await stream.ReadAsync(trailing, token) != 0)
                throw new IOException(UiText.Get("ServiceLimitExceeded"));
            return data;
        }
    }
}
