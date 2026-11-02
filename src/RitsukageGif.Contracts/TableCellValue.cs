using System.Globalization;

namespace RitsukageGif.Contracts
{
    public readonly record struct TableCellValue(
        TableCellValueKind Kind,
        bool Boolean = false,
        long Integer = 0,
        double Number = 0)
    {
        public string ToDisplayText()
        {
            return Kind switch
            {
                TableCellValueKind.Boolean => Boolean ? "true" : "false",
                TableCellValueKind.Int64 => Integer.ToString(CultureInfo.InvariantCulture),
                TableCellValueKind.Number => double.IsFinite(Number)
                    ? Number.ToString("R", CultureInfo.InvariantCulture)
                    : throw new ArgumentException("A typed cell value must be a finite number."),
                TableCellValueKind.Text => throw new InvalidOperationException(
                    "Text is stored in the cell text property."),
                _ => throw new ArgumentOutOfRangeException(nameof(Kind)),
            };
        }
    }
}
