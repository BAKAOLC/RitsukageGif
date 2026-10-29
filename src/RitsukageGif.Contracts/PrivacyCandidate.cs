namespace RitsukageGif.Contracts
{
    public sealed record PrivacyCandidate(
        // ReSharper disable once NotAccessedPositionalProperty.Global
        string ProviderId,
        PrivacyCandidateKind Kind,
        PixelRect Bounds,
        // ReSharper disable once NotAccessedPositionalProperty.Global
        string Text,
        // ReSharper disable once NotAccessedPositionalProperty.Global
        bool BoundsApproximate = false)
    {
        public bool IsSensitive => Kind != PrivacyCandidateKind.Text;
    }
}
