namespace RitsukageGif.Scripting
{
    public sealed record ScriptSpan(int Offset, int Length, string Kind, string Text, int Line, int Column);
}
