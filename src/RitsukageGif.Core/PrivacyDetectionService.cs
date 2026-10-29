namespace RitsukageGif.Core
{
    public sealed partial class PrivacyDetectionService : IPrivacyDetectionService
    {
        private static readonly ILogger Logger = LogHub.GetLogger("Core.Recognition");
        private readonly IPrivacyCandidateProvider[] _providers;

        public PrivacyDetectionService(IEnumerable<IPrivacyCandidateProvider> providers)
        {
            ArgumentNullException.ThrowIfNull(providers);
            _providers = [.. providers];
            if (_providers.Length > 32 ||
                // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
                _providers.Any(provider => provider is null || string.IsNullOrWhiteSpace(provider.Id)) ||
                _providers.Select(provider => provider.Id).Distinct(StringComparer.Ordinal).Count() !=
                _providers.Length)
                throw new ArgumentException("Privacy providers must have unique, nonempty identifiers.",
                    nameof(providers));
        }

        public async Task<PrivacyDetectionResult> DetectAsync(CapturedFrame frame, CancellationToken token = default)
        {
            ValidateFrame(frame);
            token.ThrowIfCancellationRequested();
            Logger.ZLogDebug(
                $"Privacy candidate detection started: providers={_providers.Length}, width={frame.Width}, height={frame.Height}");
            var candidates = new List<PrivacyCandidate>();
            var failures = new List<PrivacyProviderFailure>();
            var seen = new HashSet<(PrivacyCandidateKind Kind, PixelRect Bounds)>();
            var limit = new PixelRect(0, 0, frame.Width, frame.Height);
            foreach (var provider in _providers)
            {
                token.ThrowIfCancellationRequested();
                IReadOnlyList<PrivacyCandidate> detected;
                try
                {
                    detected = await provider.DetectAsync(frame, token).ConfigureAwait(false);
                    if (detected is null)
                        throw new InvalidOperationException("Privacy provider returned no result collection.");
                    token.ThrowIfCancellationRequested();
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    Logger.ZLogDebug($"Privacy candidate detection canceled");
                    throw;
                }
                catch (Exception error)
                {
                    Logger.ZLogWarning(error,
                        $"Privacy candidate provider failed: providerType={provider.GetType().Name}, exceptionType={error.GetType().Name}, hResult={error.HResult}");
                    failures.Add(new(provider.Id, error.Message));
                    continue;
                }

                foreach (var candidate in detected.Take(4096))
                {
                    token.ThrowIfCancellationRequested();
                    if (candidates.Count >= 4096) break;
                    // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
                    if (candidate is null || !Enum.IsDefined(candidate.Kind) || candidate.Bounds.IsEmpty ||
                        (long)candidate.Bounds.X + candidate.Bounds.Width > int.MaxValue ||
                        (long)candidate.Bounds.Y + candidate.Bounds.Height > int.MaxValue) continue;
                    var bounds = candidate.Bounds.Intersect(limit);
                    if (bounds.IsEmpty || !seen.Add((candidate.Kind, bounds))) continue;
                    candidates.Add(candidate with { ProviderId = provider.Id, Bounds = bounds });
                }
            }

            Logger.ZLogDebug(
                $"Privacy candidate detection completed: candidates={candidates.Count}, failedProviders={failures.Count}");
            return new(candidates.AsReadOnly(), failures.AsReadOnly());
        }

        internal static void ValidateFrame(CapturedFrame frame)
        {
            ArgumentNullException.ThrowIfNull(frame);
            if (frame.Width <= 0 || frame.Height <= 0 || frame.Width > 32768 || frame.Height > 32768 ||
                (long)frame.Width * frame.Height > 16 * 1024 * 1024 ||
                frame.PixelFormat != CapturePixelFormat.Bgra8Srgb || !Enum.IsDefined(frame.AlphaMode) ||
                frame.Stride < (long)frame.Width * 4 || (long)frame.Stride * frame.Height > frame.Pixels.Length)
                throw new ArgumentException("Privacy detection requires a valid BGRA8 frame.", nameof(frame));
        }
    }
}
