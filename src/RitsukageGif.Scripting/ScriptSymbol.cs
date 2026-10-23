namespace RitsukageGif.Scripting
{
    public sealed record ScriptSymbol(string Name, string Kind, int Line, int Column, string? File = null);
}
