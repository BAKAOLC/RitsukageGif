namespace RitsukageGif.Core
{
    public sealed record SampleColorToken(
        string Id,
        // ReSharper disable once NotAccessedPositionalProperty.Global
        string Category,
        char DefaultStyle,
        string SupportedStyles,
        Func<SampleColorValues, char, string> Format,
        SampleColorTokenInputs Inputs = SampleColorTokenInputs.Color);
}
