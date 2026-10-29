namespace RitsukageGif.Core
{
    public sealed class MediaOptimizationService(IMediaProcessingService processing) : IMediaOptimizationService
    {
        private static readonly ILogger Logger = LogHub.GetLogger("Core.Media");

        public async Task<MediaOptimizationResult> OptimizeAsync(string input, string output,
            MediaOptimizationOptions options, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(options);
            ArgumentNullException.ThrowIfNull(options.Processing);
            var initial = options.Processing;
            var minimumQuality = options.MinimumQuality ?? initial.Quality;
            var minimumColors = options.MinimumColors ?? initial.Colors;
            if (options.TargetBytes <= 0 || options.MaximumAttempts is < 1 or > 32 ||
                initial.Width is <= 0 || initial.Height is <= 0 ||
                !double.IsFinite(options.MinimumScale) || options.MinimumScale is <= 0 or > 1 ||
                minimumQuality < 1 || minimumQuality > initial.Quality || initial.Quality > 100 ||
                minimumColors < 2 || minimumColors > initial.Colors || initial.Colors > 256)
                throw new ArgumentOutOfRangeException(nameof(options));
            input = Path.GetFullPath(input);
            output = Path.GetFullPath(output);
            token.ThrowIfCancellationRequested();
            Logger.ZLogInformation(
                $"Media optimization started: targetBytes={options.TargetBytes}, maximumAttempts={options.MaximumAttempts}, minimumScale={options.MinimumScale}");
            if (!initial.Overwrite && File.Exists(output)) throw new IOException(output);
            var source = await processing.InspectAsync(input, token).ConfigureAwait(false);
            using var prepared = processing is MediaProcessingService
                ? new PreparedMediaInput(source, initial.FrameIndex)
                : null;
            var format = (initial.Format ?? Path.GetExtension(output).TrimStart('.')).ToLowerInvariant();
            var width = initial.CropWidth ?? source.Width;
            var height = initial.CropHeight ?? source.Height;
            if (initial.Rotation is 90 or 270) (width, height) = (height, width);
            var targetWidth = initial.Width ?? (initial.Height.HasValue
                ? checked((int)Math.Max(1, Math.Round(width * (double)initial.Height.Value / height)))
                : width);
            var targetHeight = initial.Height ?? (initial.Width.HasValue
                ? checked((int)Math.Max(1, Math.Round(height * (double)initial.Width.Value / width)))
                : height);
            var minimumWidth = Math.Max(1, (int)Math.Ceiling(targetWidth * options.MinimumScale));
            var minimumHeight = Math.Max(1, (int)Math.Ceiling(targetHeight * options.MinimumScale));
            var directory = Path.GetDirectoryName(output)!;
            token.ThrowIfCancellationRequested();
            Directory.CreateDirectory(directory);
            var temporary = Path.Combine(directory, $".optimize-{Guid.NewGuid():N}.{format}");
            var smallest = long.MaxValue;
            var best = initial;
            MediaProcessOptions? previous = null;
            var attempts = 0;
            try
            {
                // A bounded sweep does not assume that encoded size is monotonic in quality or palette size.
                // Lossless mode and frame timing are never changed implicitly.
                for (var step = 0; step < options.MaximumAttempts; step++)
                {
                    token.ThrowIfCancellationRequested();
                    var progress = options.MaximumAttempts == 1 ? 0 : step / (double)(options.MaximumAttempts - 1);
                    var scale = 1 - (1 - options.MinimumScale) * progress;
                    var candidate = initial with
                    {
                        Format = format,
                        Width = Math.Clamp((int)Math.Round(targetWidth * scale), minimumWidth, targetWidth),
                        Height = Math.Clamp((int)Math.Round(targetHeight * scale), minimumHeight, targetHeight),
                        Quality = format == "webp" && !initial.Lossless
                            ? (int)Math.Round(initial.Quality - (initial.Quality - minimumQuality) * progress)
                            : initial.Quality,
                        Colors = format == "gif"
                            ? (int)Math.Round(initial.Colors - (initial.Colors - minimumColors) * progress)
                            : initial.Colors,
                        Overwrite = true,
                    };
                    if (candidate == previous) continue;
                    previous = candidate;
                    var result = await (prepared is null
                            ? processing.ProcessAsync(input, temporary, candidate, token)
                            : MediaProcessingService.ProcessPreparedAsync(prepared, temporary, candidate, token))
                        .ConfigureAwait(false);
                    attempts++;
                    Logger.ZLogDebug(
                        $"Media optimization attempt completed: attempt={attempts}, bytes={result.Bytes}, width={result.Width}, height={result.Height}, quality={candidate.Quality}, colors={candidate.Colors}");
                    var settings = candidate with { Overwrite = initial.Overwrite };
                    if (result.Bytes < smallest)
                    {
                        smallest = result.Bytes;
                        best = settings;
                    }

                    if (result.Bytes > options.TargetBytes) continue;
                    token.ThrowIfCancellationRequested();
                    File.Move(temporary, output, initial.Overwrite);
                    Logger.ZLogInformation(
                        $"Media optimization target reached: attempts={attempts}, bytes={result.Bytes}, targetBytes={options.TargetBytes}");
                    return new(true, result with { Path = output }, smallest, attempts, settings);
                }

                Logger.ZLogInformation(
                    $"Media optimization attempts exhausted: attempts={attempts}, smallestBytes={smallest}, targetBytes={options.TargetBytes}");
                return new(false, null, smallest, attempts, best);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                Logger.ZLogDebug($"Media optimization canceled: attempts={attempts}");
                throw;
            }
            catch (Exception error)
            {
                Logger.ZLogError(error,
                    $"Media optimization failed: attempts={attempts}, exceptionType={error.GetType().Name}, hResult={error.HResult}");
                throw;
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }
    }
}
