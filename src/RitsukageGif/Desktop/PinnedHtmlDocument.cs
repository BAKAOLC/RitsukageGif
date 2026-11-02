using System.Text.Json.Serialization;

namespace RitsukageGif.Desktop
{
    internal sealed record PinnedHtmlDocument(string Html, string Text) : PinnedDocument
    {
        public string? SourceUrl { get; init; }
        public PinnedHtmlSelection? Selection { get; init; }
        [JsonIgnore] public override string PlainText => Text;
    }
}
