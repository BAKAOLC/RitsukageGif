using System.Text.Json.Serialization;

namespace RitsukageGif.Contracts
{
    public sealed record TableCell(int Row, int Column, int RowSpan, int ColumnSpan, string Text, OcrBounds Bounds)
    {
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public TableCellValue Value { get; init; }
    }
}
