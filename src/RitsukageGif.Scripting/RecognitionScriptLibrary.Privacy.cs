namespace RitsukageGif.Scripting
{
    public static partial class RecognitionScriptLibrary
    {
        private static void RegisterPrivacy(ScriptBindingRegistry bindings, IPrivacyDetectionService? service,
            string root)
        {
            bindings.RegisterEnum("PrivacyCandidateKind", Enum.GetValues<PrivacyCandidateKind>()
                .ToDictionary(value => value.ToString(), value => value));
            bindings.RegisterObjectSchema("PrivacyBounds",
                [new("x", "number"), new("y", "number"), new("width", "number"), new("height", "number")]);
            bindings.RegisterObjectSchema("PrivacyCandidate",
            [
                new("provider", "string"), new("kind", "PrivacyCandidateKind"), new("bounds", "PrivacyBounds"),
                new("text", "string"), new("boundsApproximate", "boolean"), new("isSensitive", "boolean"),
            ]);
            bindings.RegisterObjectSchema("PrivacyProviderFailure",
                [new("provider", "string"), new("message", "string")]);
            bindings.RegisterObjectSchema("PrivacyDetectionResult",
                [new("candidates", "PrivacyCandidate[]"), new("failures", "PrivacyProviderFailure[]")]);
            var privacy = bindings.RegisterTable("privacy");
            privacy.Function("detect", async (args, token) =>
                {
                    try
                    {
                        var detector = service ?? throw new InvalidOperationException(UiText.Get("EditorNoRuntime"));
                        var image = await ReadImage(args[0], root, token);
                        var result = await detector.DetectAsync(image, (string?)args[1], token);
                        token.ThrowIfCancellationRequested();
                        return new ScriptResult(true, PrivacyResult(result));
                    }
                    catch (Exception error) when (error is not OperationCanceledException)
                    {
                        token.ThrowIfCancellationRequested();
                        return new ScriptResult(false, ScriptServiceErrors.FromException(error, args[0] as string));
                    }
                }, [new("image", "string|Bytes"), new("language", "string?", true)],
                "Result<PrivacyDetectionResult,ServiceError>");
        }

        private static Dictionary<string, object?> PrivacyResult(PrivacyDetectionResult result)
        {
            return new()
            {
                ["candidates"] = result.Candidates.Select(object? (candidate) => new Dictionary<string, object?>
                {
                    ["provider"] = candidate.ProviderId, ["kind"] = candidate.Kind, ["text"] = candidate.Text,
                    ["boundsApproximate"] = candidate.BoundsApproximate, ["isSensitive"] = candidate.IsSensitive,
                    ["bounds"] = new Dictionary<string, object?>
                    {
                        ["x"] = (double)candidate.Bounds.X, ["y"] = (double)candidate.Bounds.Y,
                        ["width"] = (double)candidate.Bounds.Width, ["height"] = (double)candidate.Bounds.Height,
                    },
                }).ToList(),
                ["failures"] = result.Failures.Select(object? (failure) => new Dictionary<string, object?>
                {
                    ["provider"] = failure.ProviderId, ["message"] = failure.Message,
                }).ToList(),
            };
        }
    }
}
