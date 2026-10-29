namespace RitsukageGif.Core
{
    public sealed partial class MediaProcessingService
    {
        public Task<IReadOnlyList<MediaProcessResult>> ProcessManyAsync(string input,
            IReadOnlyList<MediaProcessOutput> outputs, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(outputs);
            if (outputs.Count > 16) throw new ArgumentException(UiText.Get("ServiceLimitExceeded"));
            var snapshot = outputs.ToArray();
            var paths = new HashSet<string>(OperatingSystem.IsWindows()
                ? StringComparer.OrdinalIgnoreCase
                : StringComparer.Ordinal);
            foreach (var output in snapshot)
            {
                ArgumentNullException.ThrowIfNull(output);
                ArgumentNullException.ThrowIfNull(output.Options);
                if (!paths.Add(Path.GetFullPath(output.Path))) throw new ArgumentException(null, nameof(outputs));
            }

            return Task.Run<IReadOnlyList<MediaProcessResult>>(() =>
            {
                token.ThrowIfCancellationRequested();
                Logger.ZLogInformation($"Batch media processing started: outputs={snapshot.Length}");
                var results = new MediaProcessResult[snapshot.Length];
                if (snapshot.Length == 0) return results;
                var path = Path.GetFullPath(input);
                PreparedMediaInput prepared;
                try
                {
                    var info = Inspect(path, token);
                    // In-place batches must decode before publishing so every output sees the original input.
                    if (info.Format == "apng" && snapshot.All(output => output.Options.FrameIndex.HasValue) &&
                        !snapshot.Any(output => string.Equals(Path.GetFullPath(output.Path), path,
                            OperatingSystem.IsWindows()
                                ? StringComparison.OrdinalIgnoreCase
                                : StringComparison.Ordinal)))
                        return ProcessSelectedFrames(info, snapshot, token);
                    CheckBudget(info.Width, info.Height, info.Frames);
                    prepared = new(info);
                }
                catch (Exception error) when (error is not OperationCanceledException)
                {
                    Logger.ZLogError(error,
                        $"Batch media source inspection failed: outputs={snapshot.Length}, exceptionType={error.GetType().Name}, hResult={error.HResult}");
                    token.ThrowIfCancellationRequested();
                    Array.Fill(results, new(null, error));
                    return results;
                }

                using (prepared)
                {
                    try
                    {
                        prepared.GetImages(token);
                    }
                    catch (Exception error) when (error is not OperationCanceledException)
                    {
                        Logger.ZLogError(error,
                            $"Batch media source decoding failed: outputs={snapshot.Length}, exceptionType={error.GetType().Name}, hResult={error.HResult}");
                        token.ThrowIfCancellationRequested();
                        Array.Fill(results, new(null, error));
                        return results;
                    }

                    // Keep one shared decoded input and one output working set. Native codecs may use
                    // additional scratch memory; serial transforms avoid multiplying that peak per output.
                    for (var index = 0; index < snapshot.Length; index++)
                    {
                        token.ThrowIfCancellationRequested();
                        var output = snapshot[index];
                        try
                        {
                            results[index] = new(ProcessWithDiagnostics(path, Path.GetFullPath(output.Path),
                                output.Options,
                                token, prepared), null);
                        }
                        catch (Exception error) when (error is not OperationCanceledException)
                        {
                            token.ThrowIfCancellationRequested();
                            results[index] = new(null, error);
                        }
                    }
                }

                Logger.ZLogInformation(
                    $"Batch media processing finished: outputs={results.Length}, failed={results.Count(result => result.Error is not null)}");
                return results;
            }, token);
        }
    }
}
