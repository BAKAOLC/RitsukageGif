namespace RitsukageGif.Desktop
{
    internal static partial class PinnedRichRender
    {
        private static IEnumerable<PinnedRichImage> Images(IReadOnlyList<PinnedRichBlock> blocks)
        {
            foreach (var block in blocks)
                switch (block)
                {
                    case PinnedRichParagraph paragraph:
                        foreach (var run in paragraph.Runs)
                            if (run.Image is { } image)
                                yield return image;
                        break;
                    case PinnedRichContainer container:
                        foreach (var image in Images(container.Blocks)) yield return image;
                        break;
                    case PinnedRichTable table:
                        foreach (var cell in table.Cells)
                        foreach (var image in Images(cell.Blocks))
                            yield return image;
                        break;
                }
        }
    }
}
