using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    internal sealed class SelectionActionBar : Border
    {
        private readonly Button _confirm;

        internal SelectionActionBar(Action<SelectionCommand> execute,
            IReadOnlyList<SelectionCommandDefinition> shortcuts)
        {
            Background = AppTheme.Surface;
            BorderBrush = AppTheme.Outline;
            BorderThickness = new(1);
            CornerRadius = new(6);
            Padding = new(4);
            HorizontalAlignment = HorizontalAlignment.Left;
            VerticalAlignment = VerticalAlignment.Top;
            Cursor = new(StandardCursorType.Arrow);
            var commands = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
            _confirm = Create(SelectionCommand.Confirm, "SelectionCommandConfirm", "ScreenshotApplyText");
            commands.Children.Add(_confirm);
            commands.Children.Add(Create(SelectionCommand.Cancel, "Cancel", "Cancel"));
            Child = commands;
            return;

            Button Create(SelectionCommand command, string key, string icon)
            {
                var button = new Button
                {
                    Name = command.ToString(), Width = 32, Height = 32, MinWidth = 0, MinHeight = 0,
                    Padding = new(6), Focusable = false, IsTabStop = false, Foreground = AppTheme.Ink,
                    Background = Brushes.Transparent, BorderThickness = new(0),
                    HorizontalContentAlignment = HorizontalAlignment.Center,
                    VerticalContentAlignment = VerticalAlignment.Center,
                };
                var image = ToolbarIcon.CreateInViewport(button,
                    ToolbarIcon.TryGeometry(icon) ?? ToolbarIcon.Geometry(icon));
                image.Width = image.Height = 20;
                button.Content = image;
                button.Bind(AutomationProperties.NameProperty, LiveText.Resource(key));
                var tip = new StackPanel { Spacing = 6 };
                tip.Children.Add(new TextBlock
                {
                    [!TextBlock.TextProperty] = button[!AutomationProperties.NameProperty],
                    HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center,
                });
                var gesture = shortcuts.FirstOrDefault(binding => binding.Command == command)?.Gesture;
                if (!string.IsNullOrEmpty(gesture))
                    tip.Children.Add(new Border
                    {
                        BorderBrush = AppTheme.Outline, BorderThickness = new(1), CornerRadius = new(3),
                        Padding = new(5, 2), HorizontalAlignment = HorizontalAlignment.Center,
                        Child = new TextBlock { Text = KeyGestureDisplay.Format(gesture), FontSize = 11 },
                    });
                ToolTip.SetTip(button, tip);
                button.Click += (_, _) => execute(command);
                return button;
            }
        }

        internal Rect OccupiedBounds { get; private set; }

        internal void Update(PixelRect monitor, double scale, SelectionSnapshot snapshot, bool owner, bool manipulating)
        {
            IsVisible = owner && !manipulating &&
                        snapshot is { Phase: SelectionPhase.Selected, Region.IsEmpty: false };
            if (!IsVisible)
            {
                OccupiedBounds = default;
                return;
            }

            _confirm.IsEnabled = snapshot.CanConfirm;
            var viewport = new Size(monitor.Width / scale, monitor.Height / scale);
            Child!.Measure(new(Math.Max(1, viewport.Width - 26), Math.Max(1, viewport.Height - 26)));
            Width = Child.DesiredSize.Width + 10;
            Height = Child.DesiredSize.Height + 10;
            var selected = snapshot.Region;
            var region = new Rect((selected.X - monitor.X) / scale, (selected.Y - monitor.Y) / scale,
                selected.Width / scale, selected.Height / scale);
            var position = ScreenshotToolbarLayout.Place(viewport, region, new(Width, Height));
            OccupiedBounds = new(position, new Size(Width, Height));
            Margin = new(position.X, position.Y, 0, 0);
        }
    }
}
