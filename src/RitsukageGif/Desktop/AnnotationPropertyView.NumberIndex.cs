using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;

namespace RitsukageGif.Desktop
{
    internal sealed partial class AnnotationPropertyView
    {
        private Window? _numberDialog;
        private Avalonia.PixelPoint? _numberIndexPointer;

        private StackPanel NextNumber()
        {
            var index = Button("AnnotationNextNumber");
            index.MinWidth = 34;
            index.PointerMoved += (_, args) => _numberIndexPointer = index.PointToScreen(args.GetPosition(index));
            index.PointerPressed += (_, args) => _numberIndexPointer = index.PointToScreen(args.GetPosition(index));
            index.Click += async (_, _) => await ShowNumberIndexAsync(index);
            var increase = NumberIndexStep(true);
            var decrease = NumberIndexStep(false);
            increase.Click += (_, _) => _session.SetNextNumber(_session.NextNumber + 1);
            decrease.Click += (_, _) => _session.SetNextNumber(_session.NextNumber - 1);
            var steps = new StackPanel { Children = { increase, decrease } };
            var row = new StackPanel
            {
                Orientation = Orientation.Horizontal, Margin = new(2, 0),
                Children = { index, steps },
            };
            row.PointerWheelChanged += (_, args) =>
            {
                if (_disposed || args.Delta.Y == 0) return;
                args.Handled = true;
                _session.SetNextNumber(_session.NextNumber + Math.Sign(args.Delta.Y));
            };
            _refresh.Add(() =>
            {
                index.Content = _session.NextNumber.ToString(CultureInfo.CurrentCulture);
                increase.IsEnabled = _session.NextNumber < 999999;
                decrease.IsEnabled = _session.NextNumber > _session.MinimumNumber;
            });
            return row;
        }

        private static RepeatButton NumberIndexStep(bool increase)
        {
            var button = new RepeatButton
            {
                Width = 18, Height = 14, MinWidth = 0, MinHeight = 0,
                Padding = new(0), Focusable = false,
            };
            button.Content = NumericChevron.Create(button, increase);
            ToolTip.SetTip(button, UiText.Get(increase ? "AnnotationNumberIncrease" : "AnnotationNumberDecrease"));
            return button;
        }

        private async Task ShowNumberIndexAsync(Control anchor)
        {
            if (_disposed || _numberDialog is not null || TopLevel.GetTopLevel(anchor) is not Window owner) return;
            var minimum = _session.MinimumNumber;
            var input = new TextBox
            {
                Text = _session.NextNumber.ToString(CultureInfo.InvariantCulture),
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center, FontSize = 24,
            };
            var apply = new Button { Content = UiText.Get("AnnotationNumberConfirm"), IsDefault = true, MinWidth = 72 };
            var cancel = new Button
            {
                Content = UiText.Get(_session.GlobalNumberCounter ? "AnnotationNumberReset" : "Cancel"),
                MinWidth = 72,
            };
            var footer = new StackPanel
            {
                Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
                Spacing = 6, Children = { cancel, apply },
            };
            var layout = new DockPanel { Margin = new(16) };
            DockPanel.SetDock(footer, Dock.Bottom);
            layout.Children.Add(footer);
            layout.Children.Add(input);
            var dialog = new Window
            {
                Title = UiText.Get("ScreenshotAnnotationSerial"), Width = 408, Height = 216,
                CanResize = false, Content = layout, ShowInTaskbar = false,
                WindowStartupLocation = WindowStartupLocation.Manual, Topmost = owner.Topmost,
                Background = AppTheme.Surface, Foreground = AppTheme.Ink,
            };
            _numberDialog = dialog;
            input.TextChanged += (_, _) => apply.IsEnabled = ReadNumber() is not null;
            apply.Click += (_, _) =>
            {
                if (ReadNumber() is { } value) dialog.Close(value);
            };
            cancel.Click += (_, _) => dialog.Close(_session.GlobalNumberCounter ? minimum : null);
            dialog.KeyDown += (_, args) =>
            {
                if (args.Key != Key.Escape) return;
                args.Handled = true;
                dialog.Close(null);
            };
            dialog.Opened += (_, _) =>
            {
                var center = _numberIndexPointer ?? anchor.PointToScreen(new(anchor.Bounds.Width / 2,
                    anchor.Bounds.Height / 2));
                var screen = owner.Screens.ScreenFromPoint(center) ?? owner.Screens.Primary;
                var scale = screen?.Scaling ?? owner.RenderScaling;
                var width = (int)Math.Round(dialog.Width * scale);
                var height = (int)Math.Round(dialog.Height * scale);
                var x = center.X - width / 2;
                var y = center.Y - height / 2;
                if (screen is not null)
                {
                    var area = screen.WorkingArea;
                    x = Math.Clamp(x, area.X, Math.Max(area.X, area.Right - width));
                    y = Math.Clamp(y, area.Y, Math.Max(area.Y, area.Bottom - height));
                }

                dialog.Position = new(x, y);
                input.Focus();
                input.SelectAll();
            };
            using var scope = _dialogScopeProvider?.Invoke();
            try
            {
                var selected = await dialog.ShowDialog<int?>(owner);
                if (!_disposed && selected is { } value) _session.SetNextNumber(value);
            }
            finally
            {
                _numberDialog = null;
            }

            return;

            int? ReadNumber()
            {
                return int.TryParse(input.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var value) &&
                       value >= minimum && value <= 999999
                    ? value
                    : null;
            }
        }
    }
}
