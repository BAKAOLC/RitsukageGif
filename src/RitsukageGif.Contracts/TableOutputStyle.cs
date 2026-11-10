namespace RitsukageGif.Contracts
{
    public sealed record TableOutputStyle
    {
        public static TableOutputStyle Default { get; } = new();
        public TableBorderStyle Borders { get; init; }
        public TableContentAlignment Alignment { get; init; }

        public TableOutputStyle Normalize()
        {
            return new()
            {
                Borders = Enum.IsDefined(Borders) ? Borders : TableBorderStyle.None,
                Alignment = Enum.IsDefined(Alignment) ? Alignment : TableContentAlignment.Auto,
            };
        }

        public TableOutputStyle MergeChanges(TableOutputStyle baseline, TableOutputStyle edited)
        {
            return new()
            {
                Borders = baseline.Borders == edited.Borders ? Borders : edited.Borders,
                Alignment = baseline.Alignment == edited.Alignment ? Alignment : edited.Alignment,
            };
        }
    }
}
