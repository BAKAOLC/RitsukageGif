using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    internal sealed class FatalErrorWindow : Window
    {
        public FatalErrorWindow(Exception error)
        {
            Bind(TitleProperty, LiveText.Resource("FatalErrorTitle"));
            Width = 620;
            Height = 360;
            MinWidth = 420;
            MinHeight = 240;
            Topmost = true;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = AppTheme.Surface;
            Foreground = AppTheme.Ink;
            var close = new Button
            {
                [!ContentProperty] = LiveText.Resource("Close"),
                HorizontalAlignment = HorizontalAlignment.Right,
            };
            close.Classes.Add("primary");
            close.Click += (_, _) => Close();
            var layout = new Grid { RowDefinitions = new("Auto,*,Auto"), Margin = new(16), RowSpacing = 12 };
            var message = new TextBlock { TextWrapping = TextWrapping.Wrap };

            RefreshLanguage();
            UiText.LanguageChanged += RefreshLanguage;
            Closed += (_, _) => UiText.LanguageChanged -= RefreshLanguage;
            layout.Children.Add(message);
            var details = new TextBox
            {
                Text = $"{ApplicationVersion.Display}{Environment.NewLine}{error}",
                IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Top,
                Background = AppTheme.SurfaceTint,
            };
            Grid.SetRow(details, 1);
            layout.Children.Add(details);
            var buttons = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right };
            var logs = new Button
                { [!ContentProperty] = LiveText.Resource("DiagnosticsOpenLogs"), Margin = new(0, 0, 8, 0) };
            var report = new Button
                { [!ContentProperty] = LiveText.Resource("DiagnosticsReportIssue"), Margin = new(0, 0, 8, 0) };
            logs.Click += (_, _) => Open(DiagnosticsActions.OpenLogs);
            report.Click += (_, _) => Open(DiagnosticsActions.ReportIssue);
            buttons.Children.Add(logs);
            buttons.Children.Add(report);
            buttons.Children.Add(close);
            Grid.SetRow(buttons, 2);
            layout.Children.Add(buttons);
            Content = layout;
            return;

            void RefreshLanguage()
            {
                var path = File.Exists(DiagnosticLog.FilePath)
                    ? DiagnosticLog.FilePath
                    : CrashRecoverySession.Current?.ReportPath;
                message.Text = path is not null && File.Exists(path)
                    ? UiText.Format("FatalErrorMessage", path)
                    : UiText.Get("FatalErrorLogUnavailable");
            }

            void Open(Action action)
            {
                try
                {
                    action();
                }
                catch (Exception exception)
                {
                    message.Text = exception.Message;
                }
            }
        }
    }
}
