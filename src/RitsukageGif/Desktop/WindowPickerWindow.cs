using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Input;
using Avalonia.VisualTree;
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
            this.Bind(TitleProperty, LiveText.Resource("ChooseCaptureWindow"));
            Width = 620;
            Height = 440;
            MinWidth = 340;
            MinHeight = 260;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = AppTheme.Surface;
            Foreground = AppTheme.Ink;
            var list = new ListBox
            {
                Name = "CaptureWindows", Background = AppTheme.SurfaceTint, BorderBrush = AppTheme.Outline,
                BorderThickness = new(1), CornerRadius = new(6), Margin = new(0, 0, 0, 12)
            };
            IReadOnlyList<WindowTarget> targets = [];
            var error = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = AppTheme.Muted };
            var refresh = new Button { [!ContentControl.ContentProperty] = LiveText.Resource("RefreshWindows") };
            var choose = new Button
            {
                Name = "ChooseCaptureWindow", [!ContentControl.ContentProperty] = LiveText.Resource("SelectArea"),
                IsEnabled = false
            };
            var closed = false;
            var refreshing = false;
            IDisposable? hintBinding = null;

            void UpdateSelection()
            {
                choose.IsEnabled = !closed && !refreshing && list.SelectedIndex >= 0 &&
                                   list.SelectedIndex < targets.Count;
            }

            void ChooseSelection()
            {
                if (!choose.IsEnabled) return;
                var target = targets[list.SelectedIndex];
                _completion.TrySetResult(target);
                Close(target);
            }

            async Task Refresh()
            {
                if (closed || refreshing) return;
                refreshing = true;
                refresh.IsEnabled = false;
                UpdateSelection();
                try
                {
                    if (OperatingSystem.IsMacOS()) await Task.Run(MacWindowSystem.EnsurePermission);
                    if (closed) return;
                    var updated = await Task.Run(WindowLocator.List);
                    if (closed) return;
                    targets = updated;
                    list.SelectedIndex = -1;
                    list.ItemsSource = targets.Select(w => $"{w.Process} — {w.Title}").ToArray();
                    hintBinding?.Dispose();
                    hintBinding = error.Bind(TextBlock.TextProperty, LiveText.Resource("WindowCaptureHint"));
                }
                catch (Exception ex)
                {
                    if (!closed)
                    {
                        hintBinding?.Dispose();
                        hintBinding = null;
                        error.Text = ex.Message;
                    }
                }
                finally
                {
                    refreshing = false;
                    if (!closed) refresh.IsEnabled = true;
                    UpdateSelection();
                }
            }

            refresh.Click += async (_, _) => await Refresh();
            choose.Classes.Add("primary");
            choose.Click += (_, _) => ChooseSelection();
            list.SelectionChanged += (_, _) => UpdateSelection();
            list.KeyDown += (_, e) =>
            {
                if (e.Key != Key.Enter || e.KeyModifiers != KeyModifiers.None || !choose.IsEnabled) return;
                ChooseSelection();
                e.Handled = true;
            };
            list.DoubleTapped += (_, e) =>
            {
                if (e.Source is not Control source) return;
                var item = source as ListBoxItem ?? source.GetVisualAncestors().OfType<ListBoxItem>().FirstOrDefault();
                if (item?.IsSelected != true || !choose.IsEnabled) return;
                ChooseSelection();
                e.Handled = true;
            };
            var footer = new StackPanel
            {
                Spacing = 8, Children =
                {
                    error, new StackPanel
                    {
                        Orientation = Orientation.Horizontal, Spacing = 8,
                        HorizontalAlignment = HorizontalAlignment.Right, Children = { refresh, choose }
                    }
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
                hintBinding?.Dispose();
                _completion.TrySetResult(null);
            };
        }
    }
}
