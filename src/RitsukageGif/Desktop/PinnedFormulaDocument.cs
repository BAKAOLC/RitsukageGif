using System.Text.Json.Serialization;

namespace RitsukageGif.Desktop
{
    internal sealed record PinnedFormulaDocument(string[] Sources, float FontSize = 28, uint TextColor = 0xff000000)
        : PinnedDocument
    {
        [JsonIgnore] public override string PlainText => PinnedFormulaText.Serialize(Sources);
    }
}
