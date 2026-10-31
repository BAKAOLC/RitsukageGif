using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    internal sealed partial class SettingsWindow
    {
        private StackPanel BuildDiagnosticsSettings()
        {
            var lifetime = new CancellationTokenSource();
            RegisterContentCleanup(() =>
            {
                lifetime.Cancel();
                lifetime.Dispose();
            });
            var token = lifetime.Token;
            var panel = Page();
            var status = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = AppTheme.Muted };
            var buttons = new WrapPanel { Orientation = Orientation.Horizontal };
            Add("DiagnosticsOpenLogs", () =>
            {
                DiagnosticsActions.OpenLogs();
                return Task.CompletedTask;
            });
            Add("DiagnosticsExport", async () =>
            {
                if (await DiagnosticsActions.ExportAsync(this, token)) status.Text = UiText.Get("DiagnosticsExported");
            });
            Add("DiagnosticsReportIssue", () =>
            {
                DiagnosticsActions.ReportIssue();
                return Task.CompletedTask;
            });
            panel.Children.Add(buttons);
            panel.Children.Add(new TextBlock
            {
                Text = DiagnosticsActions.LogDirectory, TextWrapping = TextWrapping.Wrap,
                Foreground = AppTheme.Muted,
            });
            panel.Children.Add(status);
            return SettingsSection("DiagnosticsHeading", panel);

            void Add(string key, Func<Task> action)
            {
                var button = new Button
                {
                    Name = key, [!ContentProperty] = LiveText.Resource(key), Margin = new(0, 0, 8, 0),
                };
                button.Click += async (_, _) =>
                {
                    button.IsEnabled = false;
                    try
                    {
                        await action();
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested)
                    {
                    }
                    catch (Exception error)
                    {
                        LogHub.GetLogger("Desktop.Diagnostics")
                            .ZLogError(error, $"Diagnostic action failed. Action={key}");
                        status.Text = error.Message;
                    }
                    finally
                    {
                        button.IsEnabled = true;
                    }
                };
                buttons.Children.Add(button);
            }
        }
    }
}
