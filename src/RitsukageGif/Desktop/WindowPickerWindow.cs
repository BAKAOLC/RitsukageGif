using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    internal sealed class WindowPickerWindow : Window
    {
        private readonly TaskCompletionSource<WindowTarget?> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<WindowTarget?> PickAsync(Window? owner)
        {
            if (owner is not null) return ShowDialog<WindowTarget?>(owner);
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Show();
            return _completion.Task;
        }

        public WindowPickerWindow()
        {
            Title = UiText.Get("ChooseCaptureWindow");
            Width = 620;
            Height = 440;
            MinWidth = 340;
            MinHeight = 260;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            var list = new ListBox();
            IReadOnlyList<WindowTarget> targets = [];
            var error = new TextBlock { TextWrapping = TextWrapping.Wrap };
            var refresh = new Button { Content = UiText.Get("RefreshWindows") };
            var closed = false;
            var refreshing = false;

            async Task Refresh()
            {
                if (closed || refreshing) return;
                refreshing = true;
                refresh.IsEnabled = false;
                try
                {
                    if (OperatingSystem.IsMacOS()) await Task.Run(MacWindowSystem.EnsurePermission);
                    if (closed) return;
                    var updated = await Task.Run(WindowLocator.List);
                    if (closed) return;
                    targets = updated;
                    list.SelectedIndex = -1;
                    list.ItemsSource = targets.Select(w => $"{w.Process} — {w.Title}").ToArray();
                    error.Text = UiText.Get("WindowCaptureHint");
                }
                catch (Exception ex)
                {
                    if (!closed) error.Text = ex.Message;
                }
                finally
                {
                    refreshing = false;
                    if (!closed) refresh.IsEnabled = true;
                }
            }

            refresh.Click += async (_, _) => await Refresh();
            var choose = new Button { Content = UiText.Get("SelectArea") };
            choose.Click += (_, _) =>
            {
                if (list.SelectedIndex >= 0 && list.SelectedIndex < targets.Count)
                {
                    var target = targets[list.SelectedIndex];
                    _completion.TrySetResult(target);
                    Close(target);
                }
            };
            var footer = new StackPanel
            {
                Spacing = 8, Children =
                {
                    error, new StackPanel
                        { Orientation = Orientation.Horizontal, Spacing = 8, Children = { refresh, choose } }
                }
            };
            var root = new DockPanel { Margin = new Thickness(16) };
            DockPanel.SetDock(footer, Dock.Bottom);
            root.Children.Add(footer);
            root.Children.Add(list);
            Content = root;
            Opened += async (_, _) => await Refresh();
            Closed += (_, _) =>
            {
                closed = true;
                _completion.TrySetResult(null);
            };
        }
    }
}
