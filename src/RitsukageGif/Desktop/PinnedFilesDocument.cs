using System.Text.Json.Serialization;

namespace RitsukageGif.Desktop
{
    internal sealed record PinnedFilesDocument(string[] Paths) : PinnedDocument
    {
        [JsonIgnore] public override string PlainText => string.Join(Environment.NewLine, Paths);
    }
}
