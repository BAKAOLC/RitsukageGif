namespace RitsukageGif.Contracts
{
    public sealed record PrivacyDetectionResult(
        IReadOnlyList<PrivacyCandidate> Candidates,
        IReadOnlyList<PrivacyProviderFailure> Failures);
}
