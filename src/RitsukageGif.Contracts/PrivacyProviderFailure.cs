namespace RitsukageGif.Contracts
{
    public sealed record PrivacyProviderFailure(
        // ReSharper disable once NotAccessedPositionalProperty.Global
        string ProviderId,
        string Message);
}
