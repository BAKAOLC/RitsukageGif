using System.Text;

namespace RitsukageGif.Core
{
    public sealed class TableOutputService : ITableOutputService
    {
        public Task<ReadOnlyMemory<byte>> EncodeAsync(TableRecognitionResult table, TableOutputFormat format,
            CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            var snapshot = Validate(table, token);
            return Task.Run<ReadOnlyMemory<byte>>(() => format switch
            {
                TableOutputFormat.Xlsx => TableRecognitionExport.ToXlsx(snapshot, token),
                TableOutputFormat.Csv => Encoding.UTF8.GetBytes(TableRecognitionExport.ToCsv(snapshot)),
                TableOutputFormat.Tsv => Encoding.UTF8.GetBytes(TableRecognitionExport.ToTsv(snapshot)),
                TableOutputFormat.Html => Encoding.UTF8.GetBytes(TableRecognitionExport.ToHtml(snapshot)),
                _ => throw new ArgumentOutOfRangeException(nameof(format)),
            }, token);
        }

        private static TableRecognitionResult Validate(TableRecognitionResult table, CancellationToken token)
        {
            if (table.Rows is < 1 or > 1048576 || table.Columns is < 1 or > 16384 ||
                (long)table.Rows * table.Columns > 1_000_000)
                throw new ArgumentOutOfRangeException(nameof(table), "The table dimensions are not supported.");
            var occupied = new bool[table.Rows * table.Columns];
            var cells = new List<TableCell>(table.Cells.Count);
            long characters = 0;
            foreach (var cell in table.Cells)
            {
                token.ThrowIfCancellationRequested();
                if (cell.Row < 0 || cell.Row >= table.Rows || cell.Column < 0 || cell.Column >= table.Columns ||
                    cell.RowSpan < 1 || cell.RowSpan > table.Rows - cell.Row || cell.ColumnSpan < 1 ||
                    cell.ColumnSpan > table.Columns - cell.Column || cell.Text is null)
                    throw new ArgumentException("The table contains an invalid cell.", nameof(table));
                characters += cell.Text.Length;
                if (characters > 32_000_000)
                    throw new ArgumentException("The table text is too large to export.", nameof(table));
                for (var row = cell.Row; row < cell.Row + cell.RowSpan; row++)
                for (var column = cell.Column; column < cell.Column + cell.ColumnSpan; column++)
                {
                    var index = row * table.Columns + column;
                    if (occupied[index])
                        throw new ArgumentException("The table contains overlapping cells.", nameof(table));
                    occupied[index] = true;
                }

                cells.Add(cell.Value.Kind == TableCellValueKind.Text
                    ? cell
                    : cell with { Text = cell.Value.ToDisplayText() });
            }

            for (var row = 0; row < table.Rows; row++)
            for (var column = 0; column < table.Columns; column++)
                if (!occupied[row * table.Columns + column])
                    cells.Add(new(row, column, 1, 1, "", default));
            return table with { Cells = cells };
        }
    }
}
