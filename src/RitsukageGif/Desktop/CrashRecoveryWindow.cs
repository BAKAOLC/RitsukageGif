using System.Globalization;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    internal sealed class CrashRecoveryWindow : Window
    {
        internal CrashRecoveryWindow(IReadOnlyList<CrashRecoveryReport> reports)
        {
            Bind(TitleProperty, LiveText.Resource("CrashRecoveryTitle"));
            Width = 620;
            Height = 360;
            MinWidth = 460;
            MinHeight = 280;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = AppTheme.Surface;
            Foreground = AppTheme.Ink;
            var root = new Grid { Margin = new(16), RowSpacing = 12, RowDefinitions = new("Auto,*,Auto,Auto") };
            root.Children.Add(new TextBlock
            {
                [!TextBlock.TextProperty] = LiveText.Resource("CrashRecoveryMessage"), TextWrapping = TextWrapping.Wrap,
            });
            var details = new TextBox
            {
                IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Top,
                Text = string.Join(Environment.NewLine + Environment.NewLine, reports.Select(report =>
                    string.Create(CultureInfo.InvariantCulture,
                        $"Version: {report.Version}\nStarted: {report.StartedUtc:O}\nLast operation: {report.LastOperation}\nReason: {report.Reason}\nExit code: {(report.ExitCode is { } code ? $"0x{code:X8}" : "Unavailable")}\nReport: {report.Path}"))),
            };
            Grid.SetRow(details, 1);
            root.Children.Add(details);
            var status = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = AppTheme.Muted };
            Grid.SetRow(status, 2);
            root.Children.Add(status);
            var buttons = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right };
            Add("DiagnosticsOpenLogs", () =>
            {
                DiagnosticsActions.OpenLogs();
                return Task.CompletedTask;
            });
            Add("DiagnosticsExport", async () => { await DiagnosticsActions.ExportAsync(this); });
            Add("DiagnosticsReportIssue", () =>
            {
                DiagnosticsActions.ReportIssue();
                return Task.CompletedTask;
            });
            Add("Close", () =>
            {
                Close();
                return Task.CompletedTask;
            });
            Grid.SetRow(buttons, 3);
            root.Children.Add(buttons);
            Content = root;
            Opened += (_, _) =>
            {
                foreach (var report in reports) CrashRecoverySession.Acknowledge(report.Path);
            };
            return;

            void Add(string key, Func<Task> action)
            {
                var button = new Button { [!ContentProperty] = LiveText.Resource(key), Margin = new(8, 0, 0, 0) };
                button.Click += async (_, _) =>
                {
                    try
                    {
                        await action();
                    }
                    catch (Exception error)
                    {
                        status.Text = error.Message;
                    }
                };
                buttons.Children.Add(button);
            }
        }
    }
}
