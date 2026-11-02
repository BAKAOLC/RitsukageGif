using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace RitsukageGif.Desktop
{
    internal sealed class PinnedGroupsWindow : Window
    {
        private readonly Stopwatch _clock = new();
        private readonly ListBox _groups = new() { Name = "PinnedGroupsPicker", MinHeight = 32 };
        private readonly bool _holdingModifiers;

        private readonly ProgressBar _progress = new()
        {
            Minimum = 0, Maximum = 100, Value = 100, Height = 3, Margin = new(4, 2, 4, 0),
        };

        private readonly Action<string> _selected;
        private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(40) };
        private readonly IReadOnlyList<PinnedGroupDefinition> _values;
        private bool _finished;

        internal PinnedGroupsWindow(IReadOnlyList<PinnedGroupDefinition> values, string currentId,
            bool holdingModifiers,
            Action<string> selected, Avalonia.PixelPoint? cursorPosition = null)
        {
            _values = values;
            _selected = selected;
            _holdingModifiers = holdingModifiers;
            TransientWindowShell.Configure(this);
            WindowDecorations = WindowDecorations.None;
            CanResize = false;
            Topmost = true;
            SizeToContent = SizeToContent.WidthAndHeight;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Background = AppTheme.Surface;
            var screen = cursorPosition is { } point ? Screens.ScreenFromPoint(point) : null;
            if (screen is not null)
            {
                MaxHeight = screen.Bounds.Height / screen.Scaling / 2;
                _groups.MaxHeight = Math.Max(32, MaxHeight - 22);
            }

            foreach (var value in values)
            {
                var active = value.Id == currentId;
                var icon = new Border { Width = 24, Child = PinnedGroupIcon.Create(this, value, active) };
                _groups.Items.Add(new ListBoxItem
                {
                    Tag = value.Id, MinHeight = 32,
                    Content = new StackPanel
                    {
                        Orientation = Orientation.Horizontal, Spacing = 6,
                        Children =
                        {
                            icon,
                            new TextBlock
                            {
                                Text = value.Name.Length == 0 ? UiText.Get("PinnedGroupDefault") : value.Name,
                                FontWeight = active ? FontWeight.Bold : FontWeight.Normal,
                                VerticalAlignment = VerticalAlignment.Center,
                            },
                        },
                    },
                });
            }

            _groups.SelectedIndex = Math.Max(0, values.ToList().FindIndex(value => value.Id == currentId));
            Content = new Border
            {
                Padding = new(6), CornerRadius = new(6), Background = AppTheme.Surface,
                BorderBrush = AppTheme.Outline, BorderThickness = new(1),
                Child = new StackPanel { Spacing = 2, Children = { _groups, _progress } },
            };
            _progress.IsVisible = !holdingModifiers;
            _groups.PointerReleased += (_, args) =>
            {
                if (args.InitialPressMouseButton != MouseButton.Left || args.Source is not Visual visual) return;
                if (visual is not ListBoxItem && !visual.GetVisualAncestors().OfType<ListBoxItem>().Any()) return;
                Complete(true);
                args.Handled = true;
            };
            KeyUp += (_, args) =>
            {
                if (args is { Key: Key.Escape, KeyModifiers: KeyModifiers.None }) Complete(false);
                else if (_holdingModifiers && args.KeyModifiers == KeyModifiers.None) Complete(true);
            };
            Deactivated += (_, _) => Complete(true);
            Closed += (_, _) => _timer.Stop();
            Opened += (_, _) =>
            {
                if (screen is not null)
                {
                    UpdateLayout();
                    Position = new(screen.Bounds.X + (screen.Bounds.Width - (int)(Bounds.Width * screen.Scaling)) / 2,
                        screen.Bounds.Y + (screen.Bounds.Height - (int)(Bounds.Height * screen.Scaling)) / 2);
                }

                if (!_holdingModifiers) RestartCountdown();
            };
            _timer.Tick += (_, _) =>
            {
                _progress.Value = Math.Max(0, 100 - _clock.Elapsed.TotalMilliseconds / 10);
                if (_clock.ElapsedMilliseconds >= 1000) Complete(true);
            };
        }

        internal void Advance()
        {
            if (_finished || _values.Count == 0) return;
            _groups.SelectedIndex = (_groups.SelectedIndex + 1) % _values.Count;
            _groups.ScrollIntoView(_groups.SelectedItem!);
            if (!_holdingModifiers) RestartCountdown();
        }

        private void RestartCountdown()
        {
            _progress.Value = 100;
            _clock.Restart();
            _timer.Start();
        }

        private void Complete(bool accept)
        {
            if (_finished) return;
            _finished = true;
            _timer.Stop();
            if (accept && _groups.SelectedIndex >= 0) _selected(_values[_groups.SelectedIndex].Id);
            Close();
        }
    }
}
