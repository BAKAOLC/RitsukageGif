using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    internal sealed class ColorFormatEditorWindow : Window
    {
        public ColorFormatEditorWindow(ColorFormatDefinition original)
        {
            Title = UiText.Get("CaptureColorFormatEdit");
            Width = 580;
            Height = 440;
            MinWidth = 420;
            MinHeight = 300;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = AppTheme.Workspace;
            Foreground = AppTheme.Ink;
            var panel = new Grid
            {
                RowDefinitions = new("Auto,Auto,Auto,*,Auto"), ColumnDefinitions = new("Auto,*"), Margin = new(12),
                RowSpacing = 8, ColumnSpacing = 8,
            };
            var name = new TextBox { Text = original.Name, MinHeight = 28 };
            var template = new TextBox { Text = original.Template, MinHeight = 28 };
            var preview = new TextBlock
                { TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
            var error = new TextBlock { Foreground = AppTheme.Error, TextWrapping = TextWrapping.Wrap, FontSize = 12 };
            AddRow(0, "CaptureColorFormatName", name);
            AddRow(1, "CaptureColorFormatTemplate", template);
            AddRow(2, "CaptureColorFormatPreview", preview);
            var parameters = new WrapPanel { ItemWidth = 68, ItemHeight = 28, Orientation = Orientation.Horizontal };
            foreach (var token in SampleColorTokenRegistry.Default.Tokens)
            {
                var button = new Button
                {
                    Content = "%" + token.Id, MinHeight = 24, Padding = new(5, 2),
                    HorizontalContentAlignment = HorizontalAlignment.Left,
                };
                ToolTip.SetTip(button, UiText.Get("CaptureColorToken" + token.Id));
                button.Click += (_, _) =>
                {
                    var start = Math.Min(template.SelectionStart, template.SelectionEnd);
                    var end = Math.Max(template.SelectionStart, template.SelectionEnd);
                    var value = template.Text ?? "";
                    template.Text = value[..start] + "%" + token.Id + value[end..];
                    template.SelectionStart = template.SelectionEnd = start + 3;
                    template.Focus();
                };
                parameters.Children.Add(button);
            }

            var help = new StackPanel { Spacing = 4 };
            help.Children.Add(new TextBlock
                { Text = UiText.Get("CaptureColorFormatParameters"), FontWeight = FontWeight.SemiBold });
            help.Children.Add(new TextBlock
            {
                Text = UiText.Get("CaptureColorFormatParameterHint"), TextWrapping = TextWrapping.Wrap,
                Foreground = AppTheme.Muted, FontSize = 12,
            });
            help.Children.Add(parameters);
            var scroll = new ScrollViewer { Content = help };
            Grid.SetRow(scroll, 3);
            Grid.SetColumnSpan(scroll, 2);
            panel.Children.Add(scroll);
            var footer = new Grid { ColumnDefinitions = new("*,Auto,Auto"), ColumnSpacing = 6 };
            footer.Children.Add(error);
            var cancel = ToolbarDragHandle.Command("Cancel", () => Close(null));
            Grid.SetColumn(cancel, 1);
            footer.Children.Add(cancel);
            var save = new Button { Content = UiText.Get("ImageSave"), MinHeight = 28, Padding = new(10, 4) };
            save.Click += (_, _) =>
            {
                if (!Validate()) return;
                Close(original with { Name = name.Text!.Trim(), Template = template.Text! });
            };
            save.Classes.Add("primary");
            Grid.SetColumn(save, 2);
            footer.Children.Add(save);
            Grid.SetRow(footer, 4);
            Grid.SetColumnSpan(footer, 2);
            panel.Children.Add(footer);
            Content = panel;
            name.TextChanged += (_, _) => Validate();
            template.TextChanged += (_, _) => Validate();
            Validate();
            AddHandler(KeyDownEvent, (_, args) =>
            {
                if (args is { Handled: false, Key: Key.Escape })
                {
                    Close(null);
                    args.Handled = true;
                }
            }, RoutingStrategies.Bubble);
            return;

            void AddRow(int row, string key, Control control)
            {
                var label = new TextBlock { Text = UiText.Get(key), VerticalAlignment = VerticalAlignment.Center };
                Grid.SetRow(label, row);
                panel.Children.Add(label);
                Grid.SetRow(control, row);
                Grid.SetColumn(control, 1);
                panel.Children.Add(control);
            }

            bool Validate()
            {
                if (string.IsNullOrWhiteSpace(name.Text) || string.IsNullOrEmpty(template.Text))
                {
                    save.IsEnabled = false;
                    error.Text = UiText.Get("CaptureColorFormatRequired");
                    preview.Text = "";
                    return false;
                }

                if (!SampleColorTokenRegistry.Default.TryCompile(template.Text, out var compiled, out var diagnostic))
                {
                    save.IsEnabled = false;
                    error.Text = $"{diagnostic!.Message} ({diagnostic.Offset + 1})";
                    preview.Text = "";
                    return false;
                }

                save.IsEnabled = true;
                error.Text = "";
                preview.Text = compiled!.Format(new(84, 139, 209, 255, new(120, 173), new(0, 0, 640, 480)));
                return true;
            }
        }
    }
}
