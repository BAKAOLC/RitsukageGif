using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml;

namespace RitsukageGif.Core
{
    public static class TableRecognitionExport
    {
        private const string SpreadsheetNamespace = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        private const string RelationshipsNamespace = "http://schemas.openxmlformats.org/package/2006/relationships";
        private const string ContentTypesNamespace = "http://schemas.openxmlformats.org/package/2006/content-types";
        private static readonly UTF8Encoding Utf8 = new(false);

        public static string ToTsv(TableRecognitionResult table)
        {
            return DelimitedText(table, '\t', "\n");
        }

        public static string ToCsv(TableRecognitionResult table)
        {
            return DelimitedText(table, ',', "\r\n");
        }

        private static string DelimitedText(TableRecognitionResult table, char separator, string newline)
        {
            var cells = table.Cells.ToDictionary(cell => (cell.Row, cell.Column));
            var result = new StringBuilder();
            for (var row = 0; row < table.Rows; row++)
            {
                if (row != 0) result.Append(newline);
                for (var column = 0; column < table.Columns; column++)
                {
                    if (column != 0) result.Append(separator);
                    if (!cells.TryGetValue((row, column), out var cell)) continue;
                    if (cell.Text.Contains(separator) || cell.Text.IndexOfAny(['"', '\n', '\r']) >= 0)
                        result.Append('"').Append(cell.Text.Replace("\"", "\"\"")).Append('"');
                    else result.Append(cell.Text);
                }
            }

            return result.ToString();
        }

        public static string ToHtml(TableRecognitionResult table)
        {
            var result = new StringBuilder();
            using var writer = XmlWriter.Create(result,
                new() { OmitXmlDeclaration = true, ConformanceLevel = ConformanceLevel.Fragment });
            writer.WriteStartElement("table");
            var rows = table.Cells.GroupBy(cell => cell.Row)
                .ToDictionary(group => group.Key, group => group.OrderBy(cell => cell.Column));
            for (var row = 0; row < table.Rows; row++)
            {
                writer.WriteStartElement("tr");
                if (rows.TryGetValue(row, out var cells))
                    foreach (var cell in cells)
                    {
                        writer.WriteStartElement("td");
                        if (cell.RowSpan > 1)
                            writer.WriteAttributeString("rowspan", cell.RowSpan.ToString(CultureInfo.InvariantCulture));
                        if (cell.ColumnSpan > 1)
                            writer.WriteAttributeString("colspan",
                                cell.ColumnSpan.ToString(CultureInfo.InvariantCulture));
                        var lines = cell.Text.Replace("\r\n", "\n").Split('\n');
                        for (var line = 0; line < lines.Length; line++)
                        {
                            if (line != 0) writer.WriteElementString("br", "");
                            writer.WriteString(lines[line]);
                        }

                        writer.WriteEndElement();
                    }

                writer.WriteEndElement();
            }

            writer.WriteEndElement();
            writer.Flush();
            return result.ToString();
        }

        public static byte[] ToXlsx(TableRecognitionResult table, CancellationToken token = default)
        {
            if (table.Rows is < 1 or > 1048576 || table.Columns is < 1 or > 16384)
                throw new ArgumentOutOfRangeException(nameof(table), "The table exceeds Excel worksheet dimensions.");
            using var memory = new MemoryStream();
            using (var archive = new ZipArchive(memory, ZipArchiveMode.Create, true))
            {
                WriteXml(archive, "[Content_Types].xml", writer =>
                {
                    writer.WriteStartElement("Types", ContentTypesNamespace);
                    Element(writer, "Default", ("Extension", "rels"),
                        ("ContentType", "application/vnd.openxmlformats-package.relationships+xml"));
                    Element(writer, "Default", ("Extension", "xml"), ("ContentType", "application/xml"));
                    Element(writer, "Override", ("PartName", "/xl/workbook.xml"),
                        ("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"));
                    Element(writer, "Override", ("PartName", "/xl/worksheets/sheet1.xml"),
                        ("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"));
                    writer.WriteEndElement();
                });
                WriteXml(archive, "_rels/.rels", writer =>
                {
                    writer.WriteStartElement("Relationships", RelationshipsNamespace);
                    Element(writer, "Relationship", ("Id", "rId1"),
                        ("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument"),
                        ("Target", "xl/workbook.xml"));
                    writer.WriteEndElement();
                });
                WriteXml(archive, "xl/workbook.xml", writer =>
                {
                    writer.WriteStartElement("workbook", SpreadsheetNamespace);
                    writer.WriteStartElement("sheets", SpreadsheetNamespace);
                    writer.WriteStartElement("sheet", SpreadsheetNamespace);
                    writer.WriteAttributeString("name", "Table");
                    writer.WriteAttributeString("sheetId", "1");
                    writer.WriteAttributeString("r", "id",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships", "rId1");
                    writer.WriteEndElement();
                    writer.WriteEndElement();
                    writer.WriteEndElement();
                });
                WriteXml(archive, "xl/_rels/workbook.xml.rels", writer =>
                {
                    writer.WriteStartElement("Relationships", RelationshipsNamespace);
                    Element(writer, "Relationship", ("Id", "rId1"),
                        ("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet"),
                        ("Target", "worksheets/sheet1.xml"));
                    writer.WriteEndElement();
                });
                WriteXml(archive, "xl/worksheets/sheet1.xml", writer => WriteWorksheet(writer, table, token));
            }

            token.ThrowIfCancellationRequested();
            return memory.ToArray();
        }

        public static async Task SaveXlsxAsync(TableRecognitionResult table, string path,
            CancellationToken token = default)
        {
            var data = ToXlsx(table, token);
            await AtomicFileOutput.WriteBytesAsync(path, data, token).ConfigureAwait(false);
        }

        private static void WriteWorksheet(XmlWriter writer, TableRecognitionResult table, CancellationToken token)
        {
            writer.WriteStartElement("worksheet", SpreadsheetNamespace);
            writer.WriteStartElement("dimension", SpreadsheetNamespace);
            writer.WriteAttributeString("ref", "A1:" + Address(table.Rows - 1, table.Columns - 1));
            writer.WriteEndElement();
            writer.WriteStartElement("sheetData", SpreadsheetNamespace);
            foreach (var row in table.Cells.GroupBy(cell => cell.Row).OrderBy(group => group.Key))
            {
                token.ThrowIfCancellationRequested();
                writer.WriteStartElement("row", SpreadsheetNamespace);
                writer.WriteAttributeString("r", (row.Key + 1).ToString(CultureInfo.InvariantCulture));
                foreach (var cell in row.OrderBy(cell => cell.Column))
                {
                    writer.WriteStartElement("c", SpreadsheetNamespace);
                    writer.WriteAttributeString("r", Address(cell.Row, cell.Column));
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
                        writer.WriteElementString("v", SpreadsheetNamespace,
                            cell.Value.Kind == TableCellValueKind.Boolean
                                ? cell.Value.Boolean ? "1" : "0"
                                : NumericText(cell.Value));
                    }

                    writer.WriteEndElement();
                }

                writer.WriteEndElement();
            }

            writer.WriteEndElement();
            var merged = table.Cells.Where(cell => cell.RowSpan > 1 || cell.ColumnSpan > 1).ToArray();
            if (merged.Length != 0)
            {
                writer.WriteStartElement("mergeCells", SpreadsheetNamespace);
                writer.WriteAttributeString("count", merged.Length.ToString(CultureInfo.InvariantCulture));
                foreach (var cell in merged)
                {
                    writer.WriteStartElement("mergeCell", SpreadsheetNamespace);
                    writer.WriteAttributeString("ref",
                        Address(cell.Row, cell.Column) + ":" + Address(cell.Row + cell.RowSpan - 1,
                            cell.Column + cell.ColumnSpan - 1));
                    writer.WriteEndElement();
                }

                writer.WriteEndElement();
            }

            writer.WriteEndElement();
        }

        private static string Address(int row, int column)
        {
            Span<char> letters = stackalloc char[4];
            var index = letters.Length;
            do
            {
                letters[--index] = (char)('A' + column % 26);
                column = column / 26 - 1;
            } while (column >= 0);

            return new string(letters[index..]) + (row + 1).ToString(CultureInfo.InvariantCulture);
        }

        private static string NumericText(TableCellValue value)
        {
            var text = value.ToDisplayText();
            return value.Kind == TableCellValueKind.Number && text.IndexOfAny(['.', 'E', 'e']) < 0 ? text + ".0" : text;
        }

        private static void WriteXml(ZipArchive archive, string path, Action<XmlWriter> write)
        {
            using var stream = archive.CreateEntry(path, CompressionLevel.Fastest).Open();
            using var writer = XmlWriter.Create(stream, new() { Encoding = Utf8, CloseOutput = false });
            write(writer);
        }

        private static void Element(XmlWriter writer, string name, params (string Name, string Value)[] attributes)
        {
            writer.WriteStartElement(name, name == "Relationship" ? RelationshipsNamespace : ContentTypesNamespace);
            foreach (var attribute in attributes) writer.WriteAttributeString(attribute.Name, attribute.Value);
            writer.WriteEndElement();
        }
    }
}
