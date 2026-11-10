namespace RitsukageGif.IconGenerator
{
    internal sealed class ParsedIcon(string definition, string[] aliases)
    {
        internal string Definition { get; } = definition;
        internal string[] Aliases { get; } = aliases;
    }
}
