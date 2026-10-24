using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    internal sealed class FatalErrorWindow : Window
    {
        public FatalErrorWindow(Exception error)
        {
            Title = UiText.Get("FatalErrorTitle");
            Width = 620;
            Height = 360;
            MinWidth = 420;
            MinHeight = 240;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            var close = new Button { Content = UiText.Get("Close"), HorizontalAlignment = HorizontalAlignment.Right };
            close.Click += (_, _) => Close();
            var layout = new Grid { RowDefinitions = new("Auto,*,Auto"), Margin = new(16), RowSpacing = 12 };
            layout.Children.Add(new TextBlock
            {
                Text = File.Exists(DiagnosticLog.FilePath)
                    ? UiText.Format("FatalErrorMessage", DiagnosticLog.FilePath)
                    : UiText.Get("FatalErrorLogUnavailable"),
                TextWrapping = TextWrapping.Wrap
            });
            var details = new TextBox
            {
                Text = $"{typeof(App).Assembly.GetName().Version}{Environment.NewLine}{error}",
                IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap
            };
            Grid.SetRow(details, 1);
            layout.Children.Add(details);
            Grid.SetRow(close, 2);
            layout.Children.Add(close);
            Content = layout;
        }
    }
}
