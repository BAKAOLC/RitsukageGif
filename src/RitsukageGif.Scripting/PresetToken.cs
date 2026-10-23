namespace RitsukageGif.Scripting
{
    internal sealed record PresetToken(string Text, int Line, int Column, bool Quoted = false);
}
