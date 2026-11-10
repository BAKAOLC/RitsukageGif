using System.Globalization;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    internal sealed partial class AnnotationPropertyView : WrapPanel, IDisposable
    {
        private readonly Func<IDisposable>? _dialogScopeProvider;
        private readonly List<Flyout> _flyouts = [];
        private readonly List<Action> _refresh = [];
        private readonly ScreenshotAnnotationSession _session;
        private readonly ScreenshotTool _tool;
        private bool _disposed;
        private bool _refreshing;

        public AnnotationPropertyView(ScreenshotAnnotationSession session, ScreenshotToolDefinition definition,
            Func<IDisposable>? dialogScopeProvider = null)
        {
            _session = session;
            _tool = definition.Tool;
            _dialogScopeProvider = dialogScopeProvider;
            Orientation = Orientation.Horizontal;
            MaxWidth = 640;
            foreach (var property in definition.Properties)
            {
                var control = property.Create(this);
                control.Tag = property.Key;
                Children.Add(control);
            }

            Refresh();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _watermarkDialog?.Close(false);
            _numberDialog?.Close(null);
            foreach (var flyout in _flyouts.ToArray())
            {
                flyout.Hide();
                flyout.Content = null;
            }

            _flyouts.Clear();
            foreach (var panel in _hoverPanels) panel.Dispose();
            _hoverPanels.Clear();
            _refresh.Clear();
            Children.Clear();
        }

        public void HideFlyouts()
        {
            foreach (var flyout in _flyouts) flyout.Hide();
            foreach (var panel in _hoverPanels) panel.Hide();
        }

        public void Refresh()
        {
            if (_disposed) return;
            _refreshing = true;
            try
            {
                foreach (var refresh in _refresh) refresh();
            }
            finally
            {
                _refreshing = false;
            }
        }

        private static Button Button(string key, string? glyph = null, string? icon = null)
        {
            var button = new Button
            {
                Width = 26, Height = 28, MinWidth = 0, MinHeight = 0, Padding = new(3), Margin = new(1, 2),
                Focusable = false, VerticalAlignment = VerticalAlignment.Center, Foreground = AppTheme.Ink,
            };
            button.Content = ToolbarIcon.FindDefinition(icon ?? key) is { } geometry
                ? ToolbarIcon.Create(button, geometry)
                : new TextBlock
                {
                    Text = glyph ?? "…", VerticalAlignment = VerticalAlignment.Center,
                    FontWeight = key == "AnimationBold" ? FontWeight.Bold : FontWeight.Normal,
                    FontStyle = key == "AnimationItalic" ? FontStyle.Italic : FontStyle.Normal,
                    TextDecorations = key == "AnnotationStrikeThrough" ? TextDecorations.Strikethrough : null,
                    [!TextBlock.ForegroundProperty] = button[!TemplatedControl.ForegroundProperty],
                };
            if (icon == "ToolbarChevron")
            {
                button.Content = DropDownChevron.Create(button);
                MinimalChrome(button);
            }

            ToolTip.SetTip(button, UiText.Get(key));
            AutomationProperties.SetName(button, UiText.Get(key));
            return button;
        }

        private static void MinimalChrome(Button button)
        {
            button.Classes.Add("tool");
            button.Background = Brushes.Transparent;
            button.BorderBrush = Brushes.Transparent;
            button.BorderThickness = new(1);
            button.Template = new FuncControlTemplate<Button>((owner, _) => new Grid
            {
                Background = Brushes.Transparent,
                Children =
                {
                    new Border
                    {
                        Background = Brushes.Transparent, BorderThickness = new(1), CornerRadius = new(2),
                        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                        [!Border.BorderBrushProperty] = owner[!TemplatedControl.BorderBrushProperty],
                        Child = new ContentPresenter
                        {
                            [!ContentPresenter.ContentProperty] = owner[!ContentControl.ContentProperty],
                            HorizontalContentAlignment = HorizontalAlignment.Center,
                            VerticalContentAlignment = VerticalAlignment.Center,
                        },
                    },
                },
            });
            button.PointerEntered += (_, _) => RefreshMinimalChrome(button);
            button.PointerExited += (_, _) => RefreshMinimalChrome(button);
            button.PropertyChanged += (_, args) =>
            {
                if (args.Property == IsEffectivelyEnabledProperty) RefreshMinimalChrome(button);
            };
        }

        private static void RefreshMinimalChrome(Button button)
        {
            if (!button.IsEffectivelyEnabled)
            {
                button.ClearValue(OpacityProperty);
                return;
            }

            if (button.Classes.Contains("palette-color"))
                button.BorderBrush = button.Classes.Contains("selected-color") ? AppTheme.Accent
                    : button.IsPointerOver ? AppTheme.Outline
                    : Brushes.Transparent;
            else
                button.Opacity = button.IsPointerOver ? .75 : 1;
        }

        private Button Toggle(string key, string glyph, Func<ScreenshotAnnotationProperties, bool> read,
            Func<ScreenshotAnnotationProperties, bool, ScreenshotAnnotationProperties> write, string? icon = null)
        {
            var button = Button(key, glyph, icon);
            button.Click += async (_, _) =>
            {
                if (!_disposed && !_refreshing)
                    await _session.UpdateSelectedPropertiesAsync(write(_session.Properties,
                        !read(_session.Properties)));
            };
            _refresh.Add(() =>
            {
                var active = read(_session.Properties);
                button.Classes.Set("active-tool", active);
                button.Classes.Set("primary", active);
                button.Background = active ? AppTheme.Accent : Brushes.Transparent;
                button.Foreground = active ? Brushes.White : AppTheme.Ink;
            });
            return button;
        }

        private CompactNumberInput NumberInput(string key, double minimum, double maximum, Func<double> read,
            Func<double, Task> write, double width = 64, bool integral = false, decimal? increment = null)
        {
            var input = new CompactNumberInput
            {
                Minimum = (decimal)minimum, Maximum = (decimal)maximum, DecimalPlaces = integral ? 0 : 1,
                Increment = increment ?? (integral ? 1 : .5m), Width = width, Height = 28, Margin = new(2),
                VerticalAlignment = VerticalAlignment.Center,
            };
            ToolTip.SetTip(input, UiText.Get(key));
            AutomationProperties.SetName(input, UiText.Get(key));
            input.ValueChanged += async (_, _) =>
            {
                if (!_refreshing && !_disposed && input.Value is { } value) await write((double)value);
            };
            _refresh.Add(() => input.Value = (decimal)read());
            return input;
        }

        private CompactNumberInput PropertyNumber(string key, double minimum, double maximum,
            Func<ScreenshotAnnotationProperties, double> read,
            Func<ScreenshotAnnotationProperties, double, ScreenshotAnnotationProperties> write, bool integral = false)
        {
            return NumberInput(key, minimum, maximum, () => read(_session.Properties),
                value => _session.UpdateSelectedPropertiesAsync(write(_session.Properties, value)), integral: integral);
        }

        private Button Popup(string key, string icon, Func<Control> create)
        {
            var button = Button(key, icon: icon);
            var flyout = new Flyout { Placement = PlacementMode.Bottom, OverlayDismissEventPassThrough = true };
            _flyouts.Add(flyout);
            button.Click += (_, _) =>
            {
                if (_disposed) return;
                flyout.Content ??= create();
                Refresh();
                flyout.ShowAt(button);
            };
            return button;
        }

        public Control WidthInput()
        {
            var input = HoverSizeInput("AnnotationThickness", 1, _session.MaximumBrushThickness,
                () => _session.Thickness, value => _session.UpdateSelectedStyleAsync(thickness: value));
            if (ScreenshotToolCatalog.Get(_tool).ObjectKind == "Geometry")
                _refresh.Add(() => input.IsEnabled = !_session.Properties.Filled);
            return input;
        }

        public Control FontSizeInput()
        {
            var input = new AnnotationEditableCombo(
            [
                "5", "6", "7", "8", "9", "10", "11", "12", "14", "16", "18", "20", "22", "26", "28", "36", "48", "56",
                "72",
            ]);
            ToolTip.SetTip(input, UiText.Get("AnimationFontSize"));
            input.TextChanged += async (_, _) =>
            {
                if (_disposed || _refreshing || !double.TryParse(input.Text,
                        NumberStyles.Float, CultureInfo.CurrentCulture,
                        out var value) || !double.IsFinite(value) || value is < 1 or > 1024 ||
                    value.Equals(_session.FontSize)) return;
                await _session.UpdateSelectedStyleAsync(fontSize: value);
            };
            _refresh.Add(() =>
            {
                var text = _session.FontSize.ToString(CultureInfo.CurrentCulture);
                if (!input.IsKeyboardFocusWithin && input.Text != text) input.Text = text;
            });
            input.LostFocus += (_, _) => Refresh();
            return input;
        }

        public Control Filled()
        {
            return PropertyCheck("AnnotationFilled", properties => properties.Filled,
                (properties, value) => properties with { Filled = value });
        }
    }
}
