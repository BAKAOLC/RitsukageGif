using System.Globalization;
using Avalonia.Controls;
using Avalonia.Layout;

namespace RitsukageGif.Desktop
{
    internal sealed class ActionScreenshotGeometryEditor : StackPanel
    {
        private readonly CompactNumberInput _height = Number("ActionHeight", 1, 500);

        private readonly CheckBox _position = new()
            { Name = "ActionFixedPosition", Content = UiText.Get("GlobalActionFixedPosition") };

        private readonly CheckBox _ratio = new()
            { Name = "ActionLockRatio", Content = UiText.Get("SelectionGeometryLockRatio") };

        private readonly CheckBox _size = new()
            { Name = "ActionFixedSize", Content = UiText.Get("SelectionGeometryFixedSize") };

        private readonly CompactNumberInput _width = Number("ActionWidth", 1, 500);
        private readonly CompactNumberInput _x = Number("ActionX", -99999, 0);
        private readonly CompactNumberInput _y = Number("ActionY", -99999, 0);
        private double? _lockedRatio;
        private bool _updating;

        internal ActionScreenshotGeometryEditor(GlobalActionBinding action)
        {
            Spacing = 6;
            var geometry = GlobalActions.ScreenshotGeometry(action);
            _size.IsChecked = geometry.Width.HasValue;
            _position.IsChecked = geometry.X.HasValue;
            _ratio.IsChecked = geometry.AspectRatio.HasValue;
            _width.Value = geometry.Width ?? 500;
            _height.Value = geometry.Height ?? 500;
            _x.Value = geometry.X ?? 0;
            _y.Value = geometry.Y ?? 0;
            _lockedRatio = geometry.AspectRatio;
            var sizeRow = Row(_size, Label("SelectionGeometryWidth"), _width, Label("SelectionGeometryHeight"),
                _height);
            var swap = new Button { Name = "ActionSwapSize", Padding = new(6, 0), MinHeight = 24 };
            swap.Content = ToolbarIcon.Create(swap, ToolbarIcon.GetDefinition("SelectionGeometrySwapDimensions"));
            ToolTip.SetTip(swap, UiText.Get("SelectionGeometrySwap"));
            sizeRow.Children.Add(swap);
            Children.Add(sizeRow);
            Children.Add(Row(_position, new TextBlock { Text = "X", VerticalAlignment = VerticalAlignment.Center }, _x,
                new TextBlock { Text = "Y", VerticalAlignment = VerticalAlignment.Center }, _y));
            Children.Add(_ratio);
            _size.IsCheckedChanged += (_, _) => RefreshEnabled();
            _position.IsCheckedChanged += (_, _) => RefreshEnabled();
            _ratio.IsCheckedChanged += (_, _) => _lockedRatio = _ratio.IsChecked == true
                ? (double)(_width.Value ?? 500) / (double)(_height.Value ?? 500)
                : null;
            _width.ValueChanged += (_, _) => UpdateRatio(true);
            _height.ValueChanged += (_, _) => UpdateRatio(false);
            swap.Click += (_, _) =>
            {
                _updating = true;
                (_width.Value, _height.Value) = (_height.Value, _width.Value);
                if (_lockedRatio is { } ratio) _lockedRatio = 1 / ratio;
                _updating = false;
            };
            RefreshEnabled();
        }

        internal void WriteArguments(Dictionary<string, string> arguments)
        {
            foreach (var key in new[] { "x", "y", "width", "height", "ratio" }) arguments.Remove(key);
            if (_size.IsChecked == true)
            {
                Write(arguments, "width", _width);
                Write(arguments, "height", _height);
            }

            if (_position.IsChecked == true)
            {
                Write(arguments, "x", _x);
                Write(arguments, "y", _y);
            }

            if (_ratio.IsChecked == true && _lockedRatio is { } ratio)
                arguments["ratio"] = ratio.ToString("R", CultureInfo.InvariantCulture);
        }

        private static void Write(Dictionary<string, string> arguments, string key, CompactNumberInput input)
        {
            if (!input.TryGetInputValue(out var value) || value != decimal.Truncate(value))
                throw new ArgumentException(UiText.Get("GlobalActionInvalidGeometry"));
            arguments[key] = value.ToString(CultureInfo.InvariantCulture);
        }

        private void RefreshEnabled()
        {
            _width.IsEnabled = _height.IsEnabled = _size.IsChecked == true;
            _x.IsEnabled = _y.IsEnabled = _position.IsChecked == true;
        }

        private void UpdateRatio(bool widthChanged)
        {
            if (_updating || _lockedRatio is not { } ratio) return;
            _updating = true;
            try
            {
                if (widthChanged)
                    _height.Value =
                        Math.Clamp((decimal)Math.Clamp(Math.Round((double)(_width.Value ?? 500) / ratio), 1, 99999), 1,
                            99999);
                else
                    _width.Value =
                        Math.Clamp((decimal)Math.Clamp(Math.Round((double)(_height.Value ?? 500) * ratio), 1, 99999), 1,
                            99999);
            }
            finally
            {
                _updating = false;
            }
        }

        private static CompactNumberInput Number(string name, int minimum, int value)
        {
            return new() { Name = name, Minimum = minimum, Maximum = 99999, Value = value, Width = 72, Height = 26 };
        }

        private static TextBlock Label(string key)
        {
            return new() { Text = UiText.Get(key), VerticalAlignment = VerticalAlignment.Center };
        }

        private static StackPanel Row(params Control[] controls)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            foreach (var control in controls) row.Children.Add(control);
            return row;
        }
    }
}
