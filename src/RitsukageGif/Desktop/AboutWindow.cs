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
            Title = UiText.Get("About");
            Width = 560;
            Height = 680;
            MinWidth = 340;
            MinHeight = 300;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = new SolidColorBrush(Color.Parse("#F5F7FA"));
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
                Content = UiText.Get("Close"), HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(24, 10)
            };
            close.Click += (_, _) => Close();
            var cancellation = new CancellationTokenSource();
            var status = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12 };
            var check = new Button { Name = "CheckUpdate", Content = UiText.Get("CheckUpdate") };
            var download = new Button
            {
                Name = "DownloadUpdate", Content = UiText.Get("DownloadUpdate"), IsVisible = false,
            };
            UpdateRelease? available = host?.AvailableUpdate;

            void RefreshUpdate()
            {
                download.IsVisible = available is not null;
                if (available is { } release) status.Text = UiText.Format("UpdateAvailable", release.Version);
            }

            RefreshUpdate();
            check.Click += async (_, _) =>
            {
                check.IsEnabled = false;
                status.Text = UiText.Get("CheckingUpdate");
                try
                {
                    available = host is null
                        ? await UpdateChecker.CheckAsync(cancellation.Token)
                        : await host.CheckForUpdatesAsync(cancellation.Token);
                    if (cancellation.IsCancellationRequested) return;
                    status.Text = UiText.Get("UpdateCurrent");
                    RefreshUpdate();
                }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
                {
                }
                catch (Exception)
                {
                    if (!cancellation.IsCancellationRequested) status.Text = UiText.Get("UpdateFailed");
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
                    status.Text = UiText.Get("UpdateOpenFailed");
                }
            };
            Closed += (_, _) =>
            {
                cancellation.Cancel();
                cancellation.Dispose();
            };
            close.Margin = new Thickness(0);
            var footer = new StackPanel
            {
                Margin = new Thickness(24, 10), Spacing = 6,
                Children =
                {
                    status,
                    new WrapPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Children = { check, download, close },
                    },
                },
            };
            check.Margin = download.Margin = new Thickness(0, 0, 6, 0);
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
