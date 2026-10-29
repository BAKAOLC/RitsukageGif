namespace RitsukageGif.Desktop
{
    internal static class PinnedImageArrangementCommands
    {
        internal static readonly PinnedImageArrangementCommand[] Catalog =
        [
            new(PinnedImageArrangement.AlignLeft, "selection-alignleft", "PinnedSelectionAlignLeft"),
            new(PinnedImageArrangement.AlignCenter, "selection-aligncenter", "PinnedSelectionAlignCenter"),
            new(PinnedImageArrangement.AlignRight, "selection-alignright", "PinnedSelectionAlignRight"),
            new(PinnedImageArrangement.AlignTop, "selection-aligntop", "PinnedSelectionAlignTop"),
            new(PinnedImageArrangement.AlignMiddle, "selection-alignmiddle", "PinnedSelectionAlignMiddle"),
            new(PinnedImageArrangement.AlignBottom, "selection-alignbottom", "PinnedSelectionAlignBottom"),
            new(PinnedImageArrangement.DistributeHorizontal, "selection-distributehorizontal",
                "PinnedSelectionDistributeHorizontal"),
            new(PinnedImageArrangement.DistributeVertical, "selection-distributevertical",
                "PinnedSelectionDistributeVertical"),
            new(PinnedImageArrangement.CompactHorizontal, "selection-compacthorizontal",
                "PinnedSelectionCompactHorizontal"),
            new(PinnedImageArrangement.CompactVertical, "selection-compactvertical", "PinnedSelectionCompactVertical"),
            new(PinnedImageArrangement.Compact, "selection-compact", "PinnedSelectionCompact"),
        ];

        internal static bool TryResolve(string action, out PinnedImageArrangement arrangement)
        {
            foreach (var command in Catalog)
                if (command.Action == action)
                {
                    arrangement = command.Arrangement;
                    return true;
                }

            arrangement = default;
            return false;
        }
    }
}
