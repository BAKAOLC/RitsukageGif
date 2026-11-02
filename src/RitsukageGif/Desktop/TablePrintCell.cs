using Avalonia.Media.TextFormatting;

namespace RitsukageGif.Desktop
{
    internal sealed record TablePrintCell(
        TableCell Source,
        int Panel,
        double X,
        IReadOnlyList<TextLine> Lines);
}
