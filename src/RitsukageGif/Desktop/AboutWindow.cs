using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using System.Diagnostics;

namespace RitsukageGif.Desktop
{
    internal sealed class AboutWindow : Window
    {
        public AboutWindow(AppSettings settings, ApplicationHost? host = null)
        {
            this.Bind(TitleProperty, LiveText.Resource("About"));
            Width = 560;
            Height = 680;
            MinWidth = 340;
            MinHeight = 300;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = AppTheme.Workspace;
            Foreground = AppTheme.Ink;
            using var resource = typeof(AboutWindow).Assembly.GetManifestResourceStream("RitsukageGif.About.md")!;
            using var reader = new StreamReader(resource);
            var markdown = reader.ReadToEnd();

            markdown = markdown.Replace("$Version$", typeof(AboutWindow).Assembly.GetName().Version!.ToString())
                .Replace("$HotKey_RecordGif$", Shortcut(settings.RecordHotkey))
                .Replace("$HotKey_SelectRegion$", Shortcut(settings.SelectHotkey));
            var content = MarkdownContent.Render(markdown);
            content.Margin = new Thickness(24);
            var close = new Button
            {
                [!ContentControl.ContentProperty] = LiveText.Resource("Close"),
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(24, 10)
            };
            close.Click += (_, _) => Close();
            var cancellation = new CancellationTokenSource();
            var status = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12, Foreground = AppTheme.Muted };
            var check = new Button
                { Name = "CheckUpdate", [!ContentControl.ContentProperty] = LiveText.Resource("CheckUpdate") };
            var download = new Button
            {
                Name = "DownloadUpdate", [!ContentControl.ContentProperty] = LiveText.Resource("DownloadUpdate"),
                IsVisible = false,
            };
            download.Classes.Add("primary");
            UpdateRelease? available = host?.AvailableUpdate;
            string? statusKey = null;

            void RefreshUpdate()
            {
                download.IsVisible = available is not null;
                status.Text = statusKey is not null ? UiText.Get(statusKey)
                    : available is { } release ? UiText.Format("UpdateAvailable", release.Version) : "";
            }

            RefreshUpdate();
            UiText.LanguageChanged += RefreshUpdate;
            check.Click += async (_, _) =>
            {
                check.IsEnabled = false;
                statusKey = "CheckingUpdate";
                RefreshUpdate();
                try
                {
                    available = host is null
                        ? await UpdateChecker.CheckAsync(cancellation.Token)
                        : await host.CheckForUpdatesAsync(cancellation.Token);
                    if (cancellation.IsCancellationRequested) return;
                    statusKey = available is null ? "UpdateCurrent" : null;
                    RefreshUpdate();
                }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
                {
                }
                catch (Exception)
                {
                    if (!cancellation.IsCancellationRequested)
                    {
                        statusKey = "UpdateFailed";
                        RefreshUpdate();
                    }
                }
                finally
                {
                    if (!cancellation.IsCancellationRequested) check.IsEnabled = true;
                }
            };
            download.Click += (_, _) =>
            {
                if (available is null) return;
                try
                {
                    Process.Start(new ProcessStartInfo(available.DownloadUrl) { UseShellExecute = true });
                }
                catch (Exception)
                {
                    statusKey = "UpdateOpenFailed";
                    RefreshUpdate();
                }
            };
            Closed += (_, _) =>
            {
                UiText.LanguageChanged -= RefreshUpdate;
                cancellation.Cancel();
                cancellation.Dispose();
            };
            close.Margin = new Thickness(8, 0, 0, 0);
            close.VerticalAlignment = VerticalAlignment.Bottom;
            var actions = new Grid { ColumnDefinitions = new("*,Auto") };
            actions.Children.Add(new WrapPanel
            {
                Orientation = Orientation.Horizontal,
                Children = { check, download },
            });
            Grid.SetColumn(close, 1);
            actions.Children.Add(close);
            check.Margin = download.Margin = new Thickness(0, 0, 6, 0);
            var footer = new Border
            {
                Background = AppTheme.Surface, BorderBrush = AppTheme.Outline,
                BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(24, 12),
                Child = new StackPanel { Spacing = 6, Children = { status, actions } },
            };
            var root = new DockPanel { Background = Background };
            DockPanel.SetDock(footer, Dock.Bottom);
            root.Children.Add(footer);
            root.Children.Add(new ScrollViewer
                { Content = content, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
            Content = root;
        }

        private static string Shortcut(string shortcut) => string.IsNullOrWhiteSpace(shortcut) ? "—" : shortcut;
    }
}
