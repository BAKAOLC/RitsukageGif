using System.Text.Json.Serialization;

namespace RitsukageGif.Desktop
{
    internal sealed record PinnedTextDocument(string Text) : PinnedDocument
    {
        [JsonIgnore] public override string PlainText => Text;
    }
}
