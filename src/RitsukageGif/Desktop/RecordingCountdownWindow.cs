using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;

namespace RitsukageGif.Desktop
{
    internal sealed class RecordingCountdownWindow : Window
    {
        private readonly TextBlock _remaining = new() { VerticalAlignment = VerticalAlignment.Center };
        private PixelRect _anchor;

        internal RecordingCountdownWindow(PixelRect anchor)
        {
            _anchor = anchor;
            WindowDecorations = WindowDecorations.None;
            SizeToContent = SizeToContent.WidthAndHeight;
            CanResize = false;
            ShowActivated = false;
            Topmost = true;
            WindowStartupLocation = WindowStartupLocation.Manual;
            TransientWindowShell.Configure(this);
            var cancel = new Button { Focusable = false };
            cancel.Content = ToolbarIcon.Create(cancel, ToolbarIcon.GetDefinition("Cancel"));
            cancel.Bind(AutomationProperties.NameProperty, LiveText.Resource("Cancel"));
            cancel.Bind(ToolTip.TipProperty, LiveText.Resource("Cancel"));
            cancel.Click += (_, _) => Close();
            Content = new Border
            {
                Background = AppTheme.Surface, BorderBrush = AppTheme.Outline, BorderThickness = new(1),
                Padding = new(8, 4), CornerRadius = new(8),
                Child = new StackPanel
                {
                    Orientation = Orientation.Horizontal, Spacing = 8, Children = { _remaining, cancel },
                },
            };
            Opened += (_, _) => PlaceAtAnchor();
            SizeChanged += (_, _) => PlaceAtAnchor();
            ScalingChanged += (_, _) => PlaceAtAnchor();
        }

        internal void UpdateRemaining(int seconds)
        {
            _remaining.Text = UiText.Format("RecordingCountdown", seconds);
        }

        internal void UpdateAnchor(PixelRect anchor)
        {
            if (_anchor == anchor) return;
            _anchor = anchor;
            PlaceAtAnchor();
        }

        private void PlaceAtAnchor()
        {
            var centerX = (int)(((long)_anchor.X + _anchor.Right - 1) / 2);
            var anchorY = _anchor.Bottom - 1;
            var screen = Screens.ScreenFromPoint(new(centerX, anchorY)) ?? Screens.Primary;
            if (screen is null) return;
            var scale = OperatingSystem.IsMacOS() ? 1 : screen.Scaling;
            var width = (int)Math.Ceiling(Bounds.Width * scale);
            var height = (int)Math.Ceiling(Bounds.Height * scale);
            var gap = (int)Math.Floor(8 * scale + .5);
            var area = screen.WorkingArea;
            var x = Math.Clamp(centerX - width / 2, area.X, Math.Max(area.X, area.Right - width));
            var y = anchorY - gap - height;
            Position = new(x, y < area.Y ? anchorY + gap : y);
        }
    }
}
