using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    internal sealed class FatalErrorWindow : Window
    {
        public FatalErrorWindow(Exception error)
        {
            this.Bind(TitleProperty, LiveText.Resource("FatalErrorTitle"));
            Width = 620;
            Height = 360;
            MinWidth = 420;
            MinHeight = 240;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = AppTheme.Surface;
            Foreground = AppTheme.Ink;
            var close = new Button
            {
                [!ContentControl.ContentProperty] = LiveText.Resource("Close"),
                HorizontalAlignment = HorizontalAlignment.Right
            };
            close.Classes.Add("primary");
            close.Click += (_, _) => Close();
            var layout = new Grid { RowDefinitions = new("Auto,*,Auto"), Margin = new(16), RowSpacing = 12 };
            var message = new TextBlock { TextWrapping = TextWrapping.Wrap };

            void RefreshLanguage()
            {
                message.Text = File.Exists(DiagnosticLog.FilePath)
                    ? UiText.Format("FatalErrorMessage", DiagnosticLog.FilePath)
                    : UiText.Get("FatalErrorLogUnavailable");
            }

            RefreshLanguage();
            UiText.LanguageChanged += RefreshLanguage;
            Closed += (_, _) => UiText.LanguageChanged -= RefreshLanguage;
            layout.Children.Add(message);
            var details = new TextBox
            {
                Text = $"{typeof(App).Assembly.GetName().Version}{Environment.NewLine}{error}",
                IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
                Background = AppTheme.SurfaceTint
            };
            Grid.SetRow(details, 1);
            layout.Children.Add(details);
            Grid.SetRow(close, 2);
            layout.Children.Add(close);
            Content = layout;
        }
    }
}
