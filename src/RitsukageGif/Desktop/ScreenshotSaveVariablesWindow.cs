using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace RitsukageGif.Desktop
{
    internal sealed class ScreenshotSaveVariablesWindow : Window
    {
        private readonly TextBlock _feedback = new()
        {
            Name = "ScreenshotSaveVariablesFeedback", Foreground = AppTheme.Muted,
            VerticalAlignment = VerticalAlignment.Center,
        };

        private IDisposable? _feedbackTimer;

        public ScreenshotSaveVariablesWindow()
        {
            Bind(TitleProperty, LiveText.Resource("ScreenshotSaveVariablesTitle"));
            Width = 500;
            Height = 552;
            MinWidth = 330;
            MinHeight = 240;
            ShowInTaskbar = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = AppTheme.Workspace;

            var entries = new StackPanel { Spacing = 2 };
            Add("$yyyy-MM-dd_HH-mm-ss$", "ScreenshotSaveVariableDate");
            Add("%title%", "ScreenshotSaveVariableTitle");
            Add("%process%", "ScreenshotSaveVariableProcess");
            Add("%width%", "ScreenshotSaveVariableWidth");
            Add("%height%", "ScreenshotSaveVariableHeight");
            Add("%os%", "ScreenshotSaveVariableOs");
            Add("%MYDOCUMENTS%", "ScreenshotSaveVariableDocuments");
            Add("%MYDESKTOP%", "ScreenshotSaveVariableDesktop");
            Add("%ONEDRIVE%", "ScreenshotSaveVariableOneDrive");
            Add("%COMPUTERNAME%", "ScreenshotSaveVariableComputer");
            Add("%USERNAME%", "ScreenshotSaveVariableUser");
            Add("%uuid%", "ScreenshotSaveVariableUuid");
            Add("%uuidv7%", "ScreenshotSaveVariableUuidV7");
            Add("%timestampS%", "ScreenshotSaveVariableSeconds");
            Add("%timestampMS%", "ScreenshotSaveVariableMilliseconds");
            entries.Children.Add(new Separator { Margin = new(26, 4, 0, 4) });
            entries.Children.Add(Note("ScreenshotSaveVariableModifiers"));
            Add("$yyyy$/$MM$/Screenshot_$yyyy-MM-dd_HH-mm-ss$", "ScreenshotSaveVariablePath", true);
            entries.Children.Add(Note("ScreenshotSaveVariableInvalidName"));

            var scroll = new ScrollViewer
            {
                Content = entries, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            };
            var close = new Button
            {
                Name = "ScreenshotSaveVariablesClose", IsCancel = true,
                [!ContentProperty] = LiveText.Resource("Close"), HorizontalAlignment = HorizontalAlignment.Right,
            };
            close.Click += (_, _) => Close();
            var footer = new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 8 };
            footer.Children.Add(_feedback);
            Grid.SetColumn(close, 1);
            footer.Children.Add(close);
            var layout = new Grid { RowDefinitions = new("*,Auto"), RowSpacing = 8, Margin = new(12) };
            layout.Children.Add(scroll);
            Grid.SetRow(footer, 1);
            layout.Children.Add(footer);
            Content = layout;
            Closed += (_, _) => _feedbackTimer?.Dispose();
            return;

            void Add(string token, string description, bool fullWidth = false)
            {
                var copy = new Button
                {
                    Classes = { "icon-button" }, Tag = token, Width = 22, Height = 22, MinWidth = 0, MinHeight = 0,
                    Padding = new(3), VerticalAlignment = VerticalAlignment.Top,
                };
                copy.Content = ToolbarIcon.Create(copy, ToolbarIcon.GetDefinition("ScreenshotCopy"));
                copy.Bind(AutomationProperties.NameProperty, LiveText.Resource("ScreenshotCopy"));
                copy.Bind(ToolTip.TipProperty, LiveText.Resource("ScreenshotCopy"));
                copy.Click += async (_, _) => await CopyAsync(token);
                var value = new TextBlock
                {
                    Text = token, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center,
                };
                var label = new TextBlock
                {
                    [!TextBlock.TextProperty] = LiveText.Resource(description), Foreground = AppTheme.Muted,
                    TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center,
                };
                var row = new Grid
                {
                    ColumnDefinitions = new(fullWidth ? "22,*" : "22,168,*"), ColumnSpacing = 6,
                    RowDefinitions = new(fullWidth ? "Auto,Auto" : "Auto"),
                };
                row.Children.Add(copy);
                Grid.SetColumn(value, 1);
                row.Children.Add(value);
                Grid.SetColumn(label, fullWidth ? 1 : 2);
                if (fullWidth) Grid.SetRow(label, 1);
                row.Children.Add(label);
                entries.Children.Add(row);
            }
        }

        private static TextBlock Note(string key)
        {
            return new()
            {
                [!TextBlock.TextProperty] = LiveText.Resource(key), Foreground = AppTheme.Muted,
                TextWrapping = TextWrapping.Wrap, Margin = new(28, 2, 0, 2),
            };
        }

        private async Task CopyAsync(string token)
        {
            string feedback;
            try
            {
                var clipboard = Clipboard ?? throw new InvalidOperationException();
                await clipboard.SetTextAsync(token);
                feedback = "ScreenshotSaveVariableCopied";
            }
            catch (Exception)
            {
                feedback = "ClipboardFailed";
            }

            if (!IsVisible) return;
            _feedback.Text = UiText.Get(feedback);
            _feedbackTimer?.Dispose();
            _feedbackTimer = DispatcherTimer.RunOnce(() => _feedback.Text = null, TimeSpan.FromMilliseconds(1500));
        }
    }
}
