using System.Text.Json.Serialization;

namespace RitsukageGif.Desktop
{
    internal sealed record PinnedColorDocument(string Value, string Text = "", string Html = "") : PinnedDocument
    {
        [JsonIgnore] public override string PlainText => Text.Length == 0 ? Value : Text;
    }
}
