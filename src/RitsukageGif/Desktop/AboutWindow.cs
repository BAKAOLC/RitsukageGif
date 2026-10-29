using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    internal sealed class AboutWindow : Window
    {
        public AboutWindow(AppSettings settings, ApplicationHost? host = null)
        {
            Bind(TitleProperty, LiveText.Resource("About"));
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

            markdown = markdown.Replace("$Version$", ApplicationVersion.Display)
                .Replace("$HotKey_RecordGif$", Shortcut(settings.GetActionShortcut(GlobalActionKind.ToggleRecording)))
                .Replace("$HotKey_SelectRegion$", Shortcut(settings.GetActionShortcut(GlobalActionKind.SelectRegion)));
            var content = MarkdownContent.Render(markdown);
            content.Margin = new(24);
            var close = new Button
            {
                [!ContentProperty] = LiveText.Resource("Close"),
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new(24, 10),
            };
            close.Click += (_, _) => Close();
            var cancellation = new CancellationTokenSource();
            var status = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12, Foreground = AppTheme.Muted };
            var check = new Button
                { Name = "CheckUpdate", [!ContentProperty] = LiveText.Resource("CheckUpdate") };
            var download = new Button
            {
                Name = "DownloadUpdate", [!ContentProperty] = LiveText.Resource("DownloadUpdate"),
                IsVisible = false,
            };
            download.Classes.Add("primary");
            var available = host?.AvailableUpdate;
            string? statusKey = null;

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
                        ? await UpdateChecker.CheckChannelAsync(settings.System.UpdateChannel, cancellation.Token)
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
                    UpdateChecker.OpenDownload(available.DownloadUrl);
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
            close.Margin = new(8, 0, 0, 0);
            close.VerticalAlignment = VerticalAlignment.Bottom;
            var actions = new Grid { ColumnDefinitions = new("*,Auto") };
            actions.Children.Add(new WrapPanel
            {
                Orientation = Orientation.Horizontal,
                Children = { check, download },
            });
            Grid.SetColumn(close, 1);
            actions.Children.Add(close);
            check.Margin = download.Margin = new(0, 0, 6, 0);
            var footer = new Border
            {
                Background = AppTheme.Surface, BorderBrush = AppTheme.Outline,
                BorderThickness = new(0, 1, 0, 0), Padding = new(24, 12),
                Child = new StackPanel { Spacing = 6, Children = { status, actions } },
            };
            var root = new DockPanel { Background = Background };
            DockPanel.SetDock(footer, Dock.Bottom);
            root.Children.Add(footer);
            root.Children.Add(new ScrollViewer
                { Content = content, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
            Content = root;
            return;

            void RefreshUpdate()
            {
                download.IsVisible = available is not null;
                status.Text = statusKey is not null ? UiText.Get(statusKey)
                    : available is { } release ? UiText.Format("UpdateAvailable", release.Version) : "";
            }
        }

        internal static Control CreateContent(AppSettings settings)
        {
            using var stream = typeof(AboutWindow).Assembly.GetManifestResourceStream("RitsukageGif.About.md")!;
            using var reader = new StreamReader(stream);
            var markdown = reader.ReadToEnd().Replace("$Version$", ApplicationVersion.Display)
                .Replace("$HotKey_RecordGif$", Shortcut(settings.GetActionShortcut(GlobalActionKind.ToggleRecording)))
                .Replace("$HotKey_SelectRegion$", Shortcut(settings.GetActionShortcut(GlobalActionKind.SelectRegion)));
            var check = new Button { Name = "CheckUpdate", [!ContentProperty] = LiveText.Resource("CheckUpdate") };
            var message = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = AppTheme.Muted };
            var download = new Button
            {
                [!ContentProperty] = LiveText.Resource("DownloadUpdate"), IsVisible = false,
            };
            var actions = new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 6, Children = { check, download },
            };
            var panel = new StackPanel { Spacing = 8, Margin = new(12) };
            panel.Children.Add(MarkdownContent.Render(markdown));
            panel.Children.Add(actions);
            panel.Children.Add(message);
            CancellationTokenSource? operation = null;
            string? downloadUrl = null;
            panel.DetachedFromVisualTree += (_, _) => operation?.Cancel();
            check.Click += async (_, _) =>
            {
                using var cancellation = new CancellationTokenSource();
                operation = cancellation;
                check.IsEnabled = false;
                download.IsVisible = false;
                message.Text = UiText.Get("CheckingUpdate");
                try
                {
                    var available =
                        await UpdateChecker.CheckChannelAsync(settings.System.UpdateChannel, cancellation.Token);
                    cancellation.Token.ThrowIfCancellationRequested();
                    message.Text = available is null ? UiText.Get("UpdateCurrent") : available.Version.ToString();
                    downloadUrl = available?.DownloadUrl;
                    download.IsVisible = available is not null;
                }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
                {
                }
                catch (Exception)
                {
                    message.Text = UiText.Get("UpdateFailed");
                }
                finally
                {
                    operation = null;
                    check.IsEnabled = true;
                }
            };
            download.Click += (_, _) =>
            {
                if (downloadUrl is null) return;
                try
                {
                    UpdateChecker.OpenDownload(downloadUrl);
                }
                catch (Exception)
                {
                    message.Text = UiText.Get("UpdateOpenFailed");
                }
            };
            return panel;
        }

        private static string Shortcut(string shortcut)
        {
            return string.IsNullOrWhiteSpace(shortcut) ? "—" : shortcut;
        }
    }
}
