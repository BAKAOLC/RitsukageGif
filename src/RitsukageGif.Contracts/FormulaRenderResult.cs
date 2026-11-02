using System.Text.Json.Serialization;

namespace RitsukageGif.Contracts
{
    public sealed record FormulaRenderResult(
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        byte[]? Png = null,
        string? Svg = null,
        string? ValidationError = null,
        string? Text = null);
}
