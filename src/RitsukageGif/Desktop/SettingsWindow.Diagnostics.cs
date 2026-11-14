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
            var status = new TextBlock
                { IsVisible = false, TextWrapping = TextWrapping.Wrap, Foreground = AppTheme.Muted };
            var buttons = new WrapPanel { Orientation = Orientation.Horizontal };
            Add("DiagnosticsOpenLogs", () =>
            {
                DiagnosticsActions.OpenLogs();
                return Task.CompletedTask;
            });
            var pinRecovery = Path.Combine(ApplicationStorage.DataDirectory, "pins.recovery");
            if (Directory.Exists(pinRecovery))
                Add("DiagnosticsOpenPinRecovery", () =>
                {
                    PlatformShell.OpenDirectory(pinRecovery);
                    return Task.CompletedTask;
                });
            Add("DiagnosticsExport", async () =>
            {
                if (await DiagnosticsActions.ExportAsync(this, token)) ShowStatus(UiText.Get("DiagnosticsExported"));
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

            void ShowStatus(string message)
            {
                status.Text = message;
                status.IsVisible = true;
            }

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
                        ShowStatus(error.Message);
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
