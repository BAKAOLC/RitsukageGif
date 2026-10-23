namespace RitsukageGif.Scripting
{
    public readonly record struct ScriptStackFrame(string Name, string? Source, int Line, int Column);
}
