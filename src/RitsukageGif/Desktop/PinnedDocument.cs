using System.Text.Json.Serialization;

namespace RitsukageGif.Desktop
{
    [JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
    [JsonDerivedType(typeof(PinnedTextDocument), "text")]
    [JsonDerivedType(typeof(PinnedHtmlDocument), "html")]
    [JsonDerivedType(typeof(PinnedColorDocument), "color")]
    [JsonDerivedType(typeof(PinnedFilesDocument), "files")]
    [JsonDerivedType(typeof(PinnedFormulaDocument), "formula")]
    internal abstract record PinnedDocument
    {
        [JsonIgnore] public abstract string PlainText { get; }
    }
}
