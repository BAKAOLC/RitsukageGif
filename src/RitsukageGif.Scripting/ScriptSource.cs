namespace RitsukageGif.Scripting
{
    public static class ScriptSource
    {
        public static bool IsPresetDocument(string source)
        {
            ArgumentNullException.ThrowIfNull(source);
            var first = PresetLexer.Scan(source)[0];
            return first is { Quoted: false, Text: "preset" };
        }
    }
}
