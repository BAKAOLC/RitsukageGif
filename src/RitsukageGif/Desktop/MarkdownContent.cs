using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace RitsukageGif.Desktop
{
    internal static class MarkdownContent
    {
        public static StackPanel Render(string source)
        {
            return Blocks(Markdown.Parse(source));
        }

        private static StackPanel Blocks(ContainerBlock blocks)
        {
            var panel = new StackPanel { Spacing = 9 };
            foreach (var block in blocks)
                switch (block)
                {
                    case HeadingBlock heading:
                        var title = Text(heading.Inline);
                        title.FontSize = heading.Level switch { 1 => 28, 2 => 21, _ => 16 };
                        title.FontWeight = FontWeight.SemiBold;
                        title.Margin = new(0, heading.Level == 1 ? 0 : 14, 0, 3);
                        panel.Children.Add(title);
                        break;
                    case ParagraphBlock paragraph: panel.Children.Add(Text(paragraph.Inline)); break;
                    case ListBlock list:
                        var index = 1;
                        foreach (var item in list.OfType<ListItemBlock>())
                        {
                            var row = new Grid { ColumnDefinitions = new("24,*") };
                            row.Children.Add(new TextBlock
                                { Text = list.IsOrdered ? $"{index++}." : "•", FontSize = 14 });
                            var body = Blocks(item);
                            Grid.SetColumn(body, 1);
                            row.Children.Add(body);
                            panel.Children.Add(row);
                        }

                        break;
                    case CodeBlock code:
                        panel.Children.Add(new Border
                        {
                            Background = AppTheme.SurfaceTint, CornerRadius = new(6),
                            Padding = new(12),
                            Child = new SelectableTextBlock
                            {
                                Text = code.Lines.ToString(), FontFamily = new("Consolas"),
                                TextWrapping = TextWrapping.Wrap,
                            },
                        });
                        break;
                    case QuoteBlock quote:
                        panel.Children.Add(new Border
                        {
                            BorderBrush = AppTheme.Accent, BorderThickness = new(3, 0, 0, 0),
                            Padding = new(12, 4), Child = Blocks(quote),
                        });
                        break;
                    case ThematicBreakBlock:
                        panel.Children.Add(new Border
                            { Height = 1, Background = AppTheme.Outline, Margin = new(0, 8) }); break;
                }

            return panel;
        }

        private static SelectableTextBlock Text(ContainerInline? source)
        {
            var text = new SelectableTextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 13 };
            if (source is not null) AddInlines(text.Inlines!, source);
            return text;
        }

        private static void AddInlines(InlineCollection target, ContainerInline source)
        {
            foreach (var inline in source)
                switch (inline)
                {
                    case LiteralInline literal: target.Add(new Run(literal.Content.ToString())); break;
                    case CodeInline code:
                        target.Add(new Run(code.Content)
                            { FontFamily = new("Consolas"), Background = AppTheme.SurfaceTint }); break;
                    case LineBreakInline line: target.Add(line.IsHard ? new LineBreak() : new Run(" ")); break;
                    case EmphasisInline emphasis:
                        var span = new Span();
                        if (emphasis.DelimiterCount >= 2) span.FontWeight = FontWeight.Bold;
                        else span.FontStyle = FontStyle.Italic;
                        AddInlines(span.Inlines, emphasis);
                        target.Add(span);
                        break;
                    case LinkInline { IsImage: false } link:
                        target.Add(new InlineUIContainer(Link(link)));
                        break;
                    case ContainerInline container: AddInlines(target, container); break;
                }
        }

        private static Button Link(LinkInline link)
        {
            var label = Text(link);
            var button = new Button
            {
                Content = label, Padding = new(0), Background = Brushes.Transparent,
                BorderThickness = new(0), MinHeight = 0,
            };
            label.Foreground = AppTheme.Accent;
            label.IsHitTestVisible = false;
            if (Uri.TryCreate(link.Url, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http")
                button.Click += (_, _) =>
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
                    }
                    catch (Exception ex)
                    {
                        ToolTip.SetTip(button, ex.Message);
                    }
                };
            return button;
        }
    }
}
