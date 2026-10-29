using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;

namespace RitsukageGif.Desktop
{
    internal sealed class SelectionOptionsBar : Border
    {
        private static readonly IBrush Active = new SolidColorBrush(Color.FromRgb(48, 108, 184));
        private readonly StackPanel? _commands;
        private readonly ToggleButton _controls;
        private readonly Control _controlsCheck;
        private readonly ToggleButton _cursor;
        private readonly Control _cursorCheck;
        private readonly StackPanel _rows = new() { Spacing = 4 };
        private readonly ToggleButton _source;
        private readonly TextBlock _sourceLabel;
        private SelectionGeometryPanel? _geometry;
        private ToggleButton? _geometryButton;
        private PixelRect _placedRegion;
        private Size _placedSize;
        private Rect _placedToolbar;
        private Size _placedViewport;

        public SelectionOptionsBar(Action<SelectionCommand> execute,
            IReadOnlyList<SelectionCommandDefinition> shortcuts)
        {
            Background = new SolidColorBrush(Color.FromRgb(32, 34, 38));
            BorderBrush = new SolidColorBrush(Color.FromRgb(74, 78, 84));
            BorderThickness = new(1);
            CornerRadius = new(6);
            Padding = new(4);
            HorizontalAlignment = HorizontalAlignment.Left;
            VerticalAlignment = VerticalAlignment.Top;
            _cursor = Create("SelectionOptionCursor", "CaptureCursor", SelectionCommand.ToggleCursor,
                out _, out _cursorCheck);
            _controls = Create("SelectionOptionControls", "SelectionDetectControls", SelectionCommand.ToggleControls,
                out _, out _controlsCheck);
            _source = Create("SelectionOptionScreen", "CaptureSource", SelectionCommand.ToggleSource,
                out _sourceLabel, out _);
            _commands = new()
            {
                Orientation = Orientation.Horizontal, Spacing = 3,
                Children = { _cursor, _controls, _source },
            };
            _rows.Children.Add(_commands);
            Child = new ThemeVariantScope
            {
                RequestedThemeVariant = ThemeVariant.Dark,
                Child = _rows,
            };
            return;

            ToggleButton Create(string labelKey, string descriptionKey, SelectionCommand command,
                out TextBlock label, out Control check)
            {
                var button = new ToggleButton
                {
                    Name = command.ToString(), Focusable = false, IsTabStop = false,
                    MinWidth = 0, MinHeight = 0,
                    Height = AppTheme.CurrentAppearance.ToolbarIconSize switch
                    {
                        ToolbarIconSize.Small => 26, ToolbarIconSize.Large => 38, _ => 30,
                    },
                    Padding = new(8, 4),
                    Foreground = Brushes.White, Background = Brushes.Transparent, BorderThickness = new(0),
                };
                label = new() { [!TextBlock.TextProperty] = LiveText.Resource(labelKey), FontSize = 12 };
                check = ToolbarIcon.Create(button, command == SelectionCommand.ToggleSource
                    ? "M2,3 L22,3 22,17 2,17 Z M8,22 L16,22 M12,17 L12,22"
                    : "M3,12 L9,18 21,5");
                check.Width = check.Height = AppTheme.CurrentAppearance.ToolbarIconSize switch
                {
                    ToolbarIconSize.Small => 10, ToolbarIconSize.Large => 20, _ => 12,
                };
                button.Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal, Spacing = 5,
                    VerticalAlignment = VerticalAlignment.Center, Children = { check, label },
                };
                button.Bind(AutomationProperties.NameProperty, LiveText.Resource(descriptionKey));
                var gesture = shortcuts.FirstOrDefault(item => item.Command == command)?.Gesture;
                ToolTip.SetTip(button, UiText.Get(descriptionKey) +
                                       (string.IsNullOrEmpty(gesture) ? string.Empty : " (" + gesture + ")") +
                                       (command == SelectionCommand.ToggleControls
                                           ? "\n" + UiText.Get("SelectionCandidateHierarchy")
                                           : ""));
                button.Click += (_, _) => execute(command);
                return button;
            }
        }

        internal Rect OccupiedBounds { get; private set; }

        internal void AttachGeometry(SelectionCoordinator coordinator)
        {
            if (_geometry is not null) return;
            _geometry = new(coordinator) { IsVisible = false, Width = 280, Margin = new(4) };
            _geometryButton = new()
            {
                Focusable = false, IsTabStop = false, Height = 30, MinHeight = 0, Padding = new(6, 3), FontSize = 12,
                Foreground = Brushes.White,
            };
            var geometryIcon = ToolbarIcon.Create(_geometryButton,
                "M3,5 L21,5 21,19 3,19 Z M7,5 L7,10 M12,5 L12,8 M17,5 L17,10");
            geometryIcon.Width = geometryIcon.Height = 14;
            _geometryButton.Content = new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 4,
                Children =
                {
                    geometryIcon,
                    new TextBlock
                    {
                        [!TextBlock.TextProperty] = LiveText.Resource("SelectionGeometry"),
                        VerticalAlignment = VerticalAlignment.Center,
                    },
                },
            };
            _geometryButton.Click += (_, _) =>
            {
                _geometry.IsVisible = _geometryButton.IsChecked == true;
                _geometry.Update(coordinator.Snapshot);
                coordinator.RefreshGeometryOptions();
            };
            _commands!.Children.Add(_geometryButton);
            _rows.Children.Add(_geometry);
            var placementRefreshPending = false;
            SizeChanged += (_, _) =>
            {
                if (placementRefreshPending || Bounds.Size == OccupiedBounds.Size) return;
                placementRefreshPending = true;
                Dispatcher.UIThread.Post(() =>
                {
                    placementRefreshPending = false;
                    if (Bounds.Size != OccupiedBounds.Size) coordinator.RefreshGeometryOptions();
                }, DispatcherPriority.Background);
            };
        }

        internal void Update(SelectionSnapshot snapshot, bool controls, bool source, bool cursor, bool drawing)
        {
            _geometryButton?.IsEnabled = snapshot.Phase == SelectionPhase.Selected && !drawing;
            _geometry?.Update(snapshot);
            Set(_cursor, _cursorCheck, snapshot.Cursor, cursor, SelectionCommand.ToggleCursor);
            Set(_controls, _controlsCheck, snapshot.Controls, controls, SelectionCommand.ToggleControls);
            _source.IsVisible = source;
            _source.IsChecked = snapshot.WindowSource;
            _source.Background = snapshot.WindowSource ? Active : Brushes.Transparent;
            _sourceLabel.Text = UiText.Get(snapshot.WindowSource ? "SelectionOptionWindow" : "SelectionOptionScreen");
            _source.IsEnabled = SelectionCommands.IsAvailable(SelectionCommand.ToggleSource, snapshot,
                controls, source, cursor, drawing);
            IsVisible = controls || source || cursor || _geometryButton is not null;
            IsHitTestVisible = snapshot.Phase is not (SelectionPhase.Pressed or SelectionPhase.Dragging);
            return;

            void Set(ToggleButton button, Control check, bool enabled, bool supported, SelectionCommand command)
            {
                button.IsVisible = supported;
                button.IsChecked = enabled;
                button.Background = enabled ? Active : Brushes.Transparent;
                check.Opacity = enabled ? 1 : 0;
                button.IsEnabled = SelectionCommands.IsAvailable(command, snapshot, controls, source, cursor, drawing);
            }
        }

        internal void Place(PixelRect monitor, double scale, PixelRect selected, Rect toolbar)
        {
            if (!IsVisible)
            {
                OccupiedBounds = default;
                return;
            }

            var viewport = new Size(monitor.Width / scale, monitor.Height / scale);
            MaxWidth = Math.Max(1, viewport.Width - 16);
            Child!.Measure(new(Math.Max(1, MaxWidth - 10), Math.Max(1, viewport.Height - 10)));
            var size = new Size(Child.DesiredSize.Width + 10, Child.DesiredSize.Height + 10);
            if (OccupiedBounds.Width > 0 && _placedRegion == selected && _placedViewport == viewport &&
                _placedSize == size && _placedToolbar == toolbar) return;
            var region = selected.IsEmpty
                ? default
                : new Rect((selected.X - monitor.X) / scale,
                    (selected.Y - monitor.Y) / scale, selected.Width / scale, selected.Height / scale);
            Span<Point> anchors =
            [
                new((viewport.Width - size.Width) / 2, 12), new(12, 12),
                new(viewport.Width - size.Width - 12, 12),
                new((viewport.Width - size.Width) / 2, viewport.Height - size.Height - 12),
                new(12, viewport.Height - size.Height - 12),
                new(viewport.Width - size.Width - 12, viewport.Height - size.Height - 12),
            ];
            var best = default(Rect);
            var bestScore = double.PositiveInfinity;
            foreach (var anchor in anchors)
            {
                var frame = new Rect(Math.Clamp(anchor.X, 0, Math.Max(0, viewport.Width - size.Width)),
                    Math.Clamp(anchor.Y, 0, Math.Max(0, viewport.Height - size.Height)), size.Width, size.Height);
                var collision = frame.Inflate(12).Intersect(region);
                var blocked = frame.Inflate(6).Intersect(toolbar);
                var score = blocked.Width * blocked.Height * 1e8 + collision.Width * collision.Height;
                if (!selected.IsEmpty)
                    for (var y = 0; y < 3; y++)
                    for (var x = 0; x < 3; x++)
                    {
                        if (x == 1 && y == 1) continue;
                        if (frame.Inflate(10).Contains(new Point(region.X + region.Width * x / 2,
                                region.Y + region.Height * y / 2))) score += 1e9;
                    }

                if (score >= bestScore) continue;
                bestScore = score;
                best = frame;
            }

            OccupiedBounds = best;
            Margin = new(best.X, best.Y, 0, 0);
            _placedRegion = selected;
            _placedViewport = viewport;
            _placedSize = size;
            _placedToolbar = toolbar;
        }
    }
}
