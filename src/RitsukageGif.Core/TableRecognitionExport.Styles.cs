using System.Globalization;
using System.Xml;

namespace RitsukageGif.Core
{
    public static partial class TableRecognitionExport
    {
        private static bool HasOutputStyle(TableOutputStyle style)
        {
            return style.Borders != TableBorderStyle.None || style.Alignment != TableContentAlignment.Auto;
        }

        private static void WriteStyles(XmlWriter writer, TableOutputStyle style)
        {
            writer.WriteStartElement("styleSheet", SpreadsheetNamespace);
            Start("fonts", 1);
            writer.WriteStartElement("font", SpreadsheetNamespace);
            writer.WriteEndElement();
            writer.WriteEndElement();
            Start("fills", 2);
            foreach (var pattern in new[] { "none", "gray125" })
            {
                writer.WriteStartElement("fill", SpreadsheetNamespace);
                writer.WriteStartElement("patternFill", SpreadsheetNamespace);
                writer.WriteAttributeString("patternType", pattern);
                writer.WriteEndElement();
                writer.WriteEndElement();
            }

            writer.WriteEndElement();
            Start("borders", 16);
            for (var mask = 0; mask < 16; mask++)
            {
                writer.WriteStartElement("border", SpreadsheetNamespace);
                Edge("left", 1);
                Edge("right", 2);
                Edge("top", 4);
                Edge("bottom", 8);
                Edge("diagonal", 0);
                writer.WriteEndElement();
                continue;

                void Edge(string name, int bit)
                {
                    writer.WriteStartElement(name, SpreadsheetNamespace);
                    if ((mask & bit) != 0)
                    {
                        writer.WriteAttributeString("style", "thin");
                        writer.WriteStartElement("color", SpreadsheetNamespace);
                        writer.WriteAttributeString("auto", "1");
                        writer.WriteEndElement();
                    }

                    writer.WriteEndElement();
                }
            }

            writer.WriteEndElement();
            Start("cellStyleXfs", 1);
            Format(0, false);
            writer.WriteEndElement();
            Start("cellXfs", 16);
            for (var border = 0; border < 16; border++) Format(border, true);
            writer.WriteEndElement();
            writer.WriteEndElement();
            return;

            void Start(string name, int count)
            {
                writer.WriteStartElement(name, SpreadsheetNamespace);
                writer.WriteAttributeString("count", count.ToString(CultureInfo.InvariantCulture));
            }

            void Format(int border, bool cell)
            {
                writer.WriteStartElement("xf", SpreadsheetNamespace);
                writer.WriteAttributeString("numFmtId", "0");
                writer.WriteAttributeString("fontId", "0");
                writer.WriteAttributeString("fillId", "0");
                writer.WriteAttributeString("borderId", border.ToString(CultureInfo.InvariantCulture));
                if (cell)
                {
                    writer.WriteAttributeString("xfId", "0");
                    writer.WriteAttributeString("applyFont", "0");
                    writer.WriteAttributeString("applyBorder", "1");
                    if (style.Alignment != TableContentAlignment.Auto)
                    {
                        writer.WriteAttributeString("applyAlignment", "1");
                        writer.WriteStartElement("alignment", SpreadsheetNamespace);
                        writer.WriteAttributeString("horizontal", style.Alignment switch
                        {
                            TableContentAlignment.Center => "center",
                            TableContentAlignment.Right => "right",
                            _ => "left",
                        });
                        writer.WriteEndElement();
                    }
                }

                writer.WriteEndElement();
            }
        }

        private static void WriteSheetData(XmlWriter writer, TableRecognitionResult table, CancellationToken token)
        {
            var style = (table.OutputStyle ?? TableOutputStyle.Default).Normalize();
            var hasStyle = HasOutputStyle(style);
            if (style.Borders == TableBorderStyle.None ||
                !table.Cells.Any(cell => cell.RowSpan > 1 || cell.ColumnSpan > 1))
            {
                foreach (var row in table.Cells.GroupBy(cell => cell.Row).OrderBy(group => group.Key))
                {
                    token.ThrowIfCancellationRequested();
                    writer.WriteStartElement("row", SpreadsheetNamespace);
                    writer.WriteAttributeString("r", (row.Key + 1).ToString(CultureInfo.InvariantCulture));
                    foreach (var cell in row.OrderBy(cell => cell.Column))
                        WriteSpreadsheetCell(writer, cell, cell.Row, cell.Column,
                            SpreadsheetBorder(table, style, cell, cell.Row, cell.Column), hasStyle);
                    writer.WriteEndElement();
                }

                return;
            }

            var rows = new SortedDictionary<int, SortedDictionary<int, (TableCell? Source, int Border)>>();
            foreach (var cell in table.Cells)
            {
                token.ThrowIfCancellationRequested();
                Add(cell.Row, cell.Column, cell);
                if (cell is { RowSpan: 1, ColumnSpan: 1 }) continue;
                var bottom = cell.Row + cell.RowSpan - 1;
                var right = cell.Column + cell.ColumnSpan - 1;
                for (var column = cell.Column; column <= right; column++)
                {
                    if ((column & 127) == 0) token.ThrowIfCancellationRequested();
                    Add(cell.Row, column, null, cell);
                    Add(bottom, column, null, cell);
                }

                for (var row = cell.Row + 1; row < bottom; row++)
                {
                    if ((row & 127) == 0) token.ThrowIfCancellationRequested();
                    Add(row, cell.Column, null, cell);
                    Add(row, right, null, cell);
                }
            }

            foreach (var (row, cells) in rows)
            {
                token.ThrowIfCancellationRequested();
                writer.WriteStartElement("row", SpreadsheetNamespace);
                writer.WriteAttributeString("r", (row + 1).ToString(CultureInfo.InvariantCulture));
                foreach (var (column, entry) in cells)
                    WriteSpreadsheetCell(writer, entry.Source, row, column, entry.Border, hasStyle);
                writer.WriteEndElement();
            }

            return;

            void Add(int row, int column, TableCell? source, TableCell? merged = null)
            {
                var mask = SpreadsheetBorder(table, style, source ?? merged!, row, column);
                if (source is null && mask == 0) return;
                if (!rows.TryGetValue(row, out var cells)) rows.Add(row, cells = new());
                if (source is not null || !cells.ContainsKey(column)) cells[column] = (source, mask);
            }
        }

        private static int SpreadsheetBorder(TableRecognitionResult table, TableOutputStyle style,
            TableCell owner, int row, int column)
        {
            if (style.Borders == TableBorderStyle.None) return 0;
            var top = style.Borders == TableBorderStyle.All ? owner.Row : 0;
            var left = style.Borders == TableBorderStyle.All ? owner.Column : 0;
            var bottom = style.Borders == TableBorderStyle.All ? owner.Row + owner.RowSpan - 1 : table.Rows - 1;
            var right = style.Borders == TableBorderStyle.All ? owner.Column + owner.ColumnSpan - 1 : table.Columns - 1;
            return (column == left ? 1 : 0) | (column == right ? 2 : 0) |
                   (row == top ? 4 : 0) | (row == bottom ? 8 : 0);
        }

        private static void WriteSpreadsheetCell(XmlWriter writer, TableCell? cell, int row, int column,
            int border, bool hasStyle)
        {
            writer.WriteStartElement("c", SpreadsheetNamespace);
            writer.WriteAttributeString("r", Address(row, column));
            if (hasStyle) writer.WriteAttributeString("s", border.ToString(CultureInfo.InvariantCulture));
            if (cell is not null)
            {
                if (cell.Value.Kind == TableCellValueKind.Text)
                {
                    writer.WriteAttributeString("t", "inlineStr");
                    writer.WriteStartElement("is", SpreadsheetNamespace);
                    writer.WriteStartElement("t", SpreadsheetNamespace);
                    writer.WriteAttributeString("xml", "space", "http://www.w3.org/XML/1998/namespace", "preserve");
                    writer.WriteString(cell.Text);
                    writer.WriteEndElement();
                    writer.WriteEndElement();
                }
                else
                {
                    writer.WriteAttributeString("t", cell.Value.Kind == TableCellValueKind.Boolean ? "b" : "n");
                    writer.WriteElementString("v", SpreadsheetNamespace, cell.Value.Kind == TableCellValueKind.Boolean
                        ? cell.Value.Boolean ? "1" : "0"
                        : NumericText(cell.Value));
                }
            }

            writer.WriteEndElement();
        }
    }
}
