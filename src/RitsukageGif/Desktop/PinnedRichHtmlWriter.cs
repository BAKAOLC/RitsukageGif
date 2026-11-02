using System.Globalization;
using System.Net;
using System.Text;
using Avalonia;
using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    internal static class PinnedRichHtmlWriter
    {
        internal static string Write(PinnedRichDocument document, int start, int length)
        {
            var result = new StringBuilder("<html><body style=\"").Append(BlockStyle(document.Style)).Append("\">");
            WriteBlocks(result, document.Blocks, start, start + length);
            return result.Append("</body></html>").ToString();
        }

        private static void WriteBlocks(StringBuilder target, IReadOnlyList<PinnedRichBlock> blocks, int start, int end)
        {
            foreach (var block in blocks)
            {
                if (block.Start >= end || block.Start + block.Length <= start) continue;
                switch (block)
                {
                    case PinnedRichParagraph paragraph:
                        target.Append("<p style=\"").Append(BlockStyle(paragraph.Style)).Append("\">");
                        foreach (var run in paragraph.Runs)
                        {
                            var left = Math.Max(start, run.Start);
                            var right = Math.Min(end, run.Start + run.Text.Length);
                            if (right <= left) continue;
                            if (run.Image is { } image)
                            {
                                target.Append("<img src=\"").Append(Escape(image.Source)).Append("\" alt=\"")
                                    .Append(Escape(image.Alternative)).Append('"');
                                LengthAttribute(target, "width", image.Width);
                                LengthAttribute(target, "height", image.Height);
                                target.Append('>');
                                continue;
                            }

                            if (run.Style.Link is { } link)
                                target.Append("<a href=\"").Append(Escape(link)).Append("\">");
                            target.Append("<span style=\"").Append(TextStyle(run.Style)).Append("\">")
                                .Append(Escape(run.Text[(left - run.Start)..(right - run.Start)])
                                    .Replace("\n", "<br>", StringComparison.Ordinal))
                                .Append("</span>");
                            if (run.Style.Link is not null) target.Append("</a>");
                        }

                        target.Append("</p>");
                        break;
                    case PinnedRichContainer container:
                        target.Append("<div style=\"").Append(BlockStyle(container.Style)).Append("\">");
                        WriteBlocks(target, container.Blocks, start, end);
                        target.Append("</div>");
                        break;
                    case PinnedRichTable table:
                        target.Append("<table style=\"").Append(BlockStyle(table.Style)).Append(";border-spacing:")
                            .Append(Number(table.CellSpacing)).Append("px\">");
                        for (var row = 0; row < table.Rows; row++)
                        {
                            target.Append("<tr>");
                            foreach (var cell in table.Cells.Where(cell => cell.Row == row))
                            {
                                target.Append("<td rowspan=\"").Append(cell.RowSpan).Append("\" colspan=\"")
                                    .Append(cell.ColumnSpan)
                                    .Append("\" style=\"").Append(BlockStyle(cell.Style)).Append(";vertical-align:")
                                    .Append(Escape(cell.VerticalAlignment)).Append("\">");
                                if (cell.Start < end && cell.Start + cell.Length > start)
                                    WriteBlocks(target, cell.Blocks, start, end);
                                target.Append("</td>");
                            }

                            target.Append("</tr>");
                        }

                        target.Append("</table>");
                        break;
                    case PinnedRichRule: target.Append("<hr>"); break;
                }
            }
        }

        private static void LengthAttribute(StringBuilder target, string name, PinnedRichLength length)
        {
            if (double.IsNaN(length.Value)) return;
            target.Append(' ').Append(name).Append("=\"").Append(Number(length.Value));
            if (length.Percent) target.Append('%');
            target.Append('"');
        }

        private static string TextStyle(PinnedRichStyle style)
        {
            var result = new StringBuilder("font-size:").Append(Number(style.FontSize)).Append("px;font-weight:")
                .Append((int)style.Weight).Append(";font-style:").Append(style.Slant.ToString().ToLowerInvariant());
            if (style.FontFamily is { } family) result.Append(";font-family:").Append(Escape(family));
            if (style.Foreground is { } foreground) result.Append(";color:").Append(ColorValue(foreground));
            if (style.Background is { } background) result.Append(";background-color:").Append(ColorValue(background));
            if (style.Underline || style.Strike)
                result.Append(";text-decoration:")
                    .Append(style.Underline ? "underline " : "").Append(style.Strike ? "line-through" : "");
            if (style.PreserveWhitespace || style.NoWrap)
                result.Append(";white-space:")
                    .Append(style.PreserveWhitespace ? style.NoWrap ? "pre" : "pre-wrap" : "nowrap");
            if (style.Baseline is BaselineAlignment.Subscript or BaselineAlignment.Superscript)
                result.Append(";vertical-align:")
                    .Append(style.Baseline == BaselineAlignment.Subscript ? "sub" : "super");
            return result.ToString();
        }

        private static string BlockStyle(PinnedRichBlockStyle style)
        {
            var result = new StringBuilder("text-align:").Append(style.Alignment.ToString().ToLowerInvariant())
                .Append(";direction:").Append(style.Direction == FlowDirection.RightToLeft ? "rtl" : "ltr");
            if (style.Background is { } background) result.Append(";background-color:").Append(ColorValue(background));
            result.Append(";padding:").Append(Thickness(style.Padding)).Append(";margin:")
                .Append(Thickness(style.Margin))
                .Append(";border-style:solid;border-width:").Append(Thickness(style.Border)).Append(";border-color:")
                .Append(ColorValue(style.BorderColor));
            if (!double.IsNaN(style.Width.Value))
                result.Append(";width:").Append(Number(style.Width.Value)).Append(style.Width.Percent ? "%" : "px");
            if (!double.IsNaN(style.Height.Value))
                result.Append(";height:").Append(Number(style.Height.Value)).Append(style.Height.Percent ? "%" : "px");
            return result.ToString();
        }

        private static string ColorValue(Color value)
        {
            return value.A == 255
                ? $"#{value.R:X2}{value.G:X2}{value.B:X2}"
                : $"rgba({value.R},{value.G},{value.B},{Number(value.A / 255d)})";
        }

        private static string Thickness(Thickness value)
        {
            return $"{Number(value.Top)}px {Number(value.Right)}px {Number(value.Bottom)}px {Number(value.Left)}px";
        }

        private static string Number(double value)
        {
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private static string Escape(string value)
        {
            return WebUtility.HtmlEncode(value);
        }
    }
}
