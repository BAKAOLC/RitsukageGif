using Microsoft.CodeAnalysis.Text;

namespace RitsukageGif.IconGenerator
{
    internal sealed class SvgInput(string path, SourceText? text)
    {
        internal string Path { get; } = path;
        internal SourceText? Text { get; } = text;
    }
}
