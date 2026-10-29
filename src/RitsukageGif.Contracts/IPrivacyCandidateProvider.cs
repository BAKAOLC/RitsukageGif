namespace RitsukageGif.Contracts
{
    public interface IPrivacyCandidateProvider
    {
        string Id { get; }

        // Bounds are in the supplied frame's pixel coordinates; providers do not modify its pixels.
        Task<IReadOnlyList<PrivacyCandidate>> DetectAsync(CapturedFrame frame, CancellationToken token = default);
    }
}
