using System.Globalization;
using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;

namespace RitsukageGif.Core
{
    internal static class TableWorkbookReader
    {
        private const long MaximumXmlBytes = 64 * 1024 * 1024;
        private static readonly XNamespace Sheet = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        private static readonly XNamespace Relationships =
            "http://schemas.openxmlformats.org/package/2006/relationships";

        private static readonly XNamespace DocumentRelationships =
            "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

        public static async Task<TableRecognitionResult> ReadAsync(ReadOnlyMemory<byte> data, CancellationToken token)
        {
            await using var stream = new MemoryStream(data.ToArray(), false);
            await using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
            if (archive.Entries.Count > 4096 || archive.Entries.Sum(entry => entry.Length) > MaximumXmlBytes)
                throw new InvalidDataException("The workbook is too large.");
            var entries = archive.Entries.ToDictionary(entry => entry.FullName, StringComparer.Ordinal);
            var package = await LoadAsync(entries, "_rels/.rels", token);
            var workbookPath = ResolveRelationship(package, "", null, "officeDocument");
            var workbook = await LoadAsync(entries, workbookPath, token);
            var slash = workbookPath.LastIndexOf('/');
            var folder = slash < 0 ? "" : workbookPath[..(slash + 1)];
            var file = workbookPath[(slash + 1)..];
            var relationships = await LoadAsync(entries, folder + "_rels/" + file + ".rels", token);
            var worksheetIds = relationships.Root?.Elements(Relationships + "Relationship")
                .Where(element =>
                    ((string?)element.Attribute("Type"))?.EndsWith("/worksheet", StringComparison.Ordinal) == true)
                .Select(element => (string?)element.Attribute("Id")).ToHashSet() ?? [];
            var sheet = workbook.Root?.Element(Sheet + "sheets")?.Elements(Sheet + "sheet")
                            .FirstOrDefault(element =>
                                worksheetIds.Contains((string?)element.Attribute(DocumentRelationships + "id")))
                        ?? throw new InvalidDataException("The workbook has no worksheets.");
            var sheetId = (string?)sheet.Attribute(DocumentRelationships + "id")
                          ?? throw new InvalidDataException("The worksheet relationship is missing.");
            var sheetPath = ResolveRelationship(relationships, folder, sheetId, "worksheet");
            var sharedRelationship = relationships.Root?.Elements(Relationships + "Relationship")
                .FirstOrDefault(element =>
                    ((string?)element.Attribute("Type"))?.EndsWith("/sharedStrings", StringComparison.Ordinal) == true);
            string[] strings = [];
            if (sharedRelationship is not null)
            {
                var path = ResolveRelationship(relationships, folder, (string?)sharedRelationship.Attribute("Id"),
                    "sharedStrings");
                strings = (await LoadAsync(entries, path, token)).Root?.Elements(Sheet + "si")
                    .Select(RichText).ToArray() ?? [];
            }

            return ReadSheet(await LoadAsync(entries, sheetPath, token), strings, token);
        }

        private static async Task<XDocument> LoadAsync(Dictionary<string, ZipArchiveEntry> entries, string path,
            CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (!entries.TryGetValue(path, out var entry))
                throw new InvalidDataException("A workbook part is missing.");
            await using var stream = await entry.OpenAsync(token);
            using var reader = XmlReader.Create(stream, new()
            {
                Async = true,
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = MaximumXmlBytes,
                IgnoreComments = true,
                IgnoreProcessingInstructions = true,
            });
            var document = await XDocument.LoadAsync(reader, LoadOptions.None, token);
            token.ThrowIfCancellationRequested();
            return document;
        }

        private static string ResolveRelationship(XDocument document, string folder, string? id, string type)
        {
            var relationship = document.Root?.Elements(Relationships + "Relationship").FirstOrDefault(element =>
                                   (id is null || (string?)element.Attribute("Id") == id) &&
                                   ((string?)element.Attribute("Type"))?.EndsWith("/" + type,
                                       StringComparison.Ordinal) == true)
                               ?? throw new InvalidDataException("A workbook relationship is missing.");
            var target = (string?)relationship.Attribute("Target") ?? "";
            if (((string?)relationship.Attribute("TargetMode") is { } mode && mode != "Internal") ||
                target.Length == 0 || target.Contains('\\') || target.Contains(':') || target.Contains('?') ||
                target.Contains('#'))
                throw new InvalidDataException("Only internal workbook relationships are supported.");
            List<string> parts = [];
            foreach (var part in (target.StartsWith('/') ? target[1..] : folder + target).Split('/'))
                switch (part)
                {
                    case "" or ".": continue;
                    case "..":
                        if (parts.Count == 0)
                            throw new InvalidDataException("The workbook relationship escapes the package.");
                        parts.RemoveAt(parts.Count - 1);
                        break;
                    default: parts.Add(part); break;
                }

            return string.Join('/', parts);
        }

        private static TableRecognitionResult ReadSheet(XDocument document, string[] strings, CancellationToken token)
        {
            var root = document.Root;
            if (root?.Name != Sheet + "worksheet")
                throw new InvalidDataException("The workbook part is not a worksheet.");
            Dictionary<(int Row, int Column), TableCell> cells = [];
            var rows = 1;
            var columns = 1;
            foreach (var element in root.Element(Sheet + "sheetData")?.Elements(Sheet + "row").Elements(Sheet + "c") ??
                                    [])
            {
                token.ThrowIfCancellationRequested();
                var address = Address((string?)element.Attribute("r") ?? "");
                var cell = ReadCell(element, address.Row, address.Column, strings);
                if (!cells.TryAdd(address, cell))
                    throw new InvalidDataException("The worksheet contains duplicate cells.");
                rows = Math.Max(rows, address.Row + 1);
                columns = Math.Max(columns, address.Column + 1);
                CheckSize(rows, columns);
            }

            if ((string?)root.Element(Sheet + "dimension")?.Attribute("ref") is { } dimension)
            {
                var last = Address(dimension.Split(':')[^1]);
                rows = Math.Max(rows, last.Row + 1);
                columns = Math.Max(columns, last.Column + 1);
                CheckSize(rows, columns);
            }

            List<TableCell> merged = [];
            var covered = new HashSet<(int Row, int Column)>();
            foreach (var element in root.Element(Sheet + "mergeCells")?.Elements(Sheet + "mergeCell") ?? [])
            {
                token.ThrowIfCancellationRequested();
                var range = ((string?)element.Attribute("ref") ?? "").Split(':');
                if (range.Length != 2) throw new InvalidDataException("The merged range is invalid.");
                var first = Address(range[0]);
                var last = Address(range[1]);
                if (last.Row < first.Row || last.Column < first.Column)
                    throw new InvalidDataException("The merged range is invalid.");
                rows = Math.Max(rows, last.Row + 1);
                columns = Math.Max(columns, last.Column + 1);
                CheckSize(rows, columns);
                for (var row = first.Row; row <= last.Row; row++)
                for (var column = first.Column; column <= last.Column; column++)
                    if (!covered.Add((row, column)))
                        throw new InvalidDataException("Merged worksheet ranges overlap.");
                var owner = cells.GetValueOrDefault(first) ?? new TableCell(first.Row, first.Column, 1, 1, "", default);
                merged.Add(owner with
                {
                    RowSpan = last.Row - first.Row + 1, ColumnSpan = last.Column - first.Column + 1,
                });
            }

            var result = new TableRecognitionResult(rows, columns, 0, 0,
                [.. cells.Where(pair => !covered.Contains(pair.Key)).Select(pair => pair.Value), .. merged]);
            return new TableRecognitionDocument(result).Table;
        }

        private static void CheckSize(int rows, int columns)
        {
            if ((long)rows * columns > 1_000_000)
                throw new InvalidDataException("The worksheet dimensions are too large.");
        }

        private static (int Row, int Column) Address(string value)
        {
            var index = 0;
            var column = 0;
            while (index < value.Length && value[index] is >= 'A' and <= 'Z')
            {
                column = column * 26 + value[index++] - 'A' + 1;
                if (column > 16384) throw new InvalidDataException("The worksheet column is invalid.");
            }

            if (column == 0 || !int.TryParse(value.AsSpan(index), NumberStyles.None, CultureInfo.InvariantCulture,
                    out var row) ||
                row is < 1 or > 1048576)
                throw new InvalidDataException("The worksheet cell address is invalid.");
            return (row - 1, column - 1);
        }

        private static TableCell ReadCell(XElement element, int row, int column, string[] strings)
        {
            var type = (string?)element.Attribute("t");
            var raw = element.Element(Sheet + "v")?.Value ?? "";
            var value = default(TableCellValue);
            string text;
            switch (type)
            {
                case "inlineStr": text = element.Element(Sheet + "is") is { } inline ? RichText(inline) : ""; break;
                case "s":
                    if (!int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var index) ||
                        (uint)index >= strings.Length)
                        throw new InvalidDataException("The shared string index is invalid.");
                    text = strings[index];
                    break;
                case "b":
                    if (raw is not ("0" or "1") && !bool.TryParse(raw, out _))
                        throw new InvalidDataException("The boolean worksheet value is invalid.");
                    value = new(TableCellValueKind.Boolean,
                        raw == "1" || string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase));
                    text = value.ToDisplayText();
                    break;
                case "str" or "d" or "e": text = raw; break;
                default:
                    if (long.TryParse(raw, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture,
                            out var integer))
                        value = new(TableCellValueKind.Int64, Integer: integer);
                    else if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) &&
                             double.IsFinite(number))
                        value = new(TableCellValueKind.Number, Number: number);
                    text = value.Kind == TableCellValueKind.Text ? raw : value.ToDisplayText();
                    break;
            }

            return new(row, column, 1, 1, text, default) { Value = value };
        }

        private static string RichText(XElement element)
        {
            return string.Concat(element.Elements().Select(child => child.Name == Sheet + "t"
                ? child.Value
                : child.Name == Sheet + "r"
                    ? child.Element(Sheet + "t")?.Value ?? ""
                    : ""));
        }
    }
}
