using Avalonia.Media;
using HtmlAgilityPack;

namespace RitsukageGif.Desktop
{
    internal sealed partial class PinnedRichParser
    {
        private PinnedRichTable ParseTable(HtmlNode table, PinnedRichStyle inherited, PinnedRichBlockStyle parent)
        {
            var start = _plain.Length;
            var style = BlockStyle(table, parent);
            var textStyle = TextStyle(table, inherited);
            var rows = table.ChildNodes.SelectMany(node => node.Name switch
            {
                "tr" => [node],
                "thead" or "tbody" or "tfoot" => node.ChildNodes.Where(child => child.Name == "tr"),
                _ => [],
            }).ToArray();
            var occupied = new List<int>();
            var cells = new List<PinnedRichCell>();
            var spacing = Math.Max(0, Number(table.GetAttributeValue("cellspacing", "")) ?? 2);
            var padding = Math.Max(0, Number(table.GetAttributeValue("cellpadding", "")) ?? 0);
            foreach (var (key, value) in Declarations(table))
                spacing = key switch
                {
                    "border-collapse" when value == "collapse" => 0,
                    "border-spacing" => Math.Max(0, Number(value) ?? spacing),
                    _ => spacing,
                };
            var columns = 0;
            for (var row = 0; row < rows.Length; row++)
            {
                var rowNode = rows[row];
                var rowStyle = BlockStyle(rowNode, style);
                var rowTextStyle = TextStyle(rowNode, textStyle);
                var column = 0;
                var previousRight = 0;
                var first = true;
                foreach (var cell in rowNode.ChildNodes.Where(node => node.Name is "td" or "th"))
                {
                    var columnSpan = Math.Clamp(cell.GetAttributeValue("colspan", 1), 1, 1000);
                    var requestedRows = cell.GetAttributeValue("rowspan", 1);
                    var rowSpan = requestedRows == 0
                        ? rows.Length - row
                        : Math.Clamp(requestedRows, 1, rows.Length - row);
                    while (true)
                    {
                        while (occupied.Count < column + columnSpan) occupied.Add(0);
                        var overlap = false;
                        for (var slot = column; slot < column + columnSpan; slot++)
                            overlap |= occupied[slot] > row;
                        if (!overlap) break;
                        column++;
                    }

                    for (var slot = column; slot < column + columnSpan; slot++) occupied[slot] = row + rowSpan;
                    if (!first) _plain.Append('\t');
                    _plain.Append('\t', Math.Max(0, column - previousRight));
                    var cellStart = _plain.Length;
                    var content = new List<PinnedRichBlock>();
                    var cellStyle = BlockStyle(cell, rowStyle);
                    var declarations = Declarations(cell).ToArray();
                    cellStyle = cellStyle with
                    {
                        Background = cellStyle.Background ?? rowStyle.Background,
                        Padding = declarations.Any(entry => entry.Key.StartsWith("padding", StringComparison.Ordinal))
                            ? cellStyle.Padding
                            : new(padding),
                        Border = declarations.Any(entry => entry.Key.StartsWith("border", StringComparison.Ordinal))
                            ? cellStyle.Border
                            : style.Border,
                        BorderColor = declarations.Any(entry => entry.Key == "border-color")
                            ? cellStyle.BorderColor
                            : style.BorderColor,
                    };
                    if (cell.Name == "th" && !cell.Attributes.Contains("align") &&
                        declarations.All(entry => entry.Key != "text-align"))
                        cellStyle = cellStyle with { Alignment = TextAlignment.Center };
                    ParseContainer(cell, TextStyle(cell, rowTextStyle), ContentBlockStyle(cellStyle), content);
                    if (_plain.Length > cellStart && _plain[^1] == '\n') _plain.Length--;
                    cells.Add(new(row, column, rowSpan, columnSpan, cellStart, _plain.Length - cellStart,
                        cellStyle, cell.GetAttributeValue("valign", "middle"), content));
                    previousRight = column + columnSpan;
                    columns = Math.Max(columns, previousRight);
                    column = previousRight;
                    first = false;
                }

                _plain.Append('\n');
            }

            return new(start, _plain.Length - start, style, rows.Length, columns, spacing, cells);
        }
    }
}
