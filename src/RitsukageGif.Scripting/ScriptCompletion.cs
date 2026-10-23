namespace RitsukageGif.Scripting
{
    public sealed record ScriptCompletion(string Label, string InsertText, string Detail, string Kind = "keyword");
}
