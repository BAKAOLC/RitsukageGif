namespace RitsukageGif.Contracts
{
    public sealed record RecognitionProviderCapabilities(bool Available, string? UnavailableReason = null);
}
