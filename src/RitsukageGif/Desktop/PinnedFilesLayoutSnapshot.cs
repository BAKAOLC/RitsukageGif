using Avalonia;

namespace RitsukageGif.Desktop
{
    internal sealed record PinnedFilesLayoutSnapshot(
        Size CellSize,
        int Columns,
        int Rows,
        IReadOnlyList<PinnedFileDisplayEntry> Entries)
    {
        private static Size NativeCellSize { get; } = new(250, 58);
        internal Size Extent => new(CellSize.Width * Columns, CellSize.Height * Rows);

        internal static PinnedFilesLayoutSnapshot Create(IReadOnlyList<string> paths,
            PinnedFileListMaximumSize maximumSize)
        {
            var columnCapacity = Math.Max(1, Round(maximumSize.Width / NativeCellSize.Width));
            var rowCapacity = Math.Max(1, Round(maximumSize.Height / NativeCellSize.Height));
            var columns = paths.Count <= columnCapacity ? paths.Count : columnCapacity;
            var rows = paths.Count <= columnCapacity
                ? 1
                : Math.Min((int)Math.Ceiling((double)paths.Count / columnCapacity), rowCapacity);
            var entries = new List<PinnedFileDisplayEntry>();
            for (var index = 0; index < paths.Count; index++)
            {
                if (index % columns == columnCapacity - 1 && index / columns == rowCapacity - 1)
                {
                    entries.Add(new(null, paths.Count));
                    break;
                }

                entries.Add(new(paths[index], 0));
            }

            return new(NativeCellSize, columns, rows, [.. entries]);
        }

        internal static int Round(double value)
        {
            return (int)Math.Floor(value + 0.5);
        }
    }
}
