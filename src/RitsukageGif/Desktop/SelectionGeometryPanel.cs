using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;

namespace RitsukageGif.Desktop
{
    internal sealed class SelectionGeometryPanel : StackPanel
    {
        private readonly SelectionCoordinator _coordinator;
        private readonly ToggleButton _fixed = new() { Focusable = false, IsTabStop = false };
        private readonly NumericUpDown _height = Number(1);
        private readonly ToggleButton _ratio = new() { Focusable = false, IsTabStop = false };
        private readonly NumericUpDown _width = Number(1);
        private readonly NumericUpDown _x = Number(int.MinValue);
        private readonly NumericUpDown _y = Number(int.MinValue);
        private bool _updating;

        internal SelectionGeometryPanel(SelectionCoordinator coordinator)
        {
            _coordinator = coordinator;
            Spacing = 5;
            var fields = new Grid
            {
                ColumnDefinitions = new("Auto,*,Auto,*"), RowDefinitions = new("Auto,Auto"), ColumnSpacing = 6,
                RowSpacing = 4,
            };
            AddField(_x, "X", 0, 0, false);
            AddField(_y, "Y", 0, 2, false);
            AddField(_width, UiText.Get("SelectionGeometryWidth"), 1, 0, true);
            AddField(_height, UiText.Get("SelectionGeometryHeight"), 1, 2, false);
            _ratio.Bind(ContentControl.ContentProperty, LiveText.Resource("SelectionGeometryLockRatio"));
            _fixed.Bind(ContentControl.ContentProperty, LiveText.Resource("SelectionGeometryFixedSize"));
            _ratio.Click += (_, _) => coordinator.LockGeometryRatio(_ratio.IsChecked == true);
            _fixed.Click += (_, _) => coordinator.FixGeometrySize(_fixed.IsChecked == true);
            var swap = new Button { Focusable = false, IsTabStop = false };
            swap.Content = ToolbarIcon.Create(swap, "M3,7 L20,7 M16,3 L20,7 16,11 M21,17 L4,17 M8,13 L4,17 8,21");
            ToolTip.SetTip(swap, UiText.Get("SelectionGeometrySwap"));
            swap.Click += (_, _) => coordinator.SwapGeometryDimensions();
            var apply = new Button { Focusable = false, IsTabStop = false };
            apply.Bind(ContentControl.ContentProperty, LiveText.Resource("GlobalActionApply"));
            apply.Click += (_, _) => Apply(true);
            Children.Add(fields);
            Children.Add(new WrapPanel
                { Orientation = Orientation.Horizontal, Children = { _ratio, _fixed, swap, apply } });
            return;

            void AddField(NumericUpDown input, string text, int row, int column, bool widthChanged)
            {
                var label = new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center, FontSize = 12 };
                Grid.SetRow(label, row);
                Grid.SetColumn(label, column);
                fields.Children.Add(label);
                Grid.SetRow(input, row);
                Grid.SetColumn(input, column + 1);
                fields.Children.Add(input);
                input.LostFocus += (_, _) => Apply(widthChanged);
                input.KeyDown += (_, args) =>
                {
                    if (args.Key != Key.Enter) return;
                    Apply(widthChanged);
                    args.Handled = true;
                };
            }
        }

        private static NumericUpDown Number(decimal minimum)
        {
            return new()
            {
                Minimum = minimum, Maximum = int.MaxValue, Increment = 1, FormatString = "0", Width = 86, MinHeight = 0,
                Height = 28, FontSize = 12,
            };
        }

        private void Apply(bool widthChanged)
        {
            if (_updating || _x.Value is not { } x || _y.Value is not { } y ||
                _width.Value is not { } width || _height.Value is not { } height) return;
            _coordinator.ChangeGeometry((int)x, (int)y, (int)width, (int)height, widthChanged);
        }

        internal void Update(SelectionSnapshot snapshot)
        {
            IsEnabled = snapshot.Phase == SelectionPhase.Selected;
            if (IsKeyboardFocusWithin) return;
            _updating = true;
            try
            {
                _x.Value = snapshot.Region.X;
                _y.Value = snapshot.Region.Y;
                _width.Value = Math.Max(1, snapshot.Region.Width);
                _height.Value = Math.Max(1, snapshot.Region.Height);
                _ratio.IsChecked = _coordinator.GeometryRatioLocked;
                _fixed.IsChecked = _coordinator.GeometrySizeFixed;
            }
            finally
            {
                _updating = false;
            }
        }
    }
}
