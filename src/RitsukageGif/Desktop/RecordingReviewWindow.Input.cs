using System.Globalization;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    // ReSharper disable once ClassCannotBeInstantiated
    internal sealed partial class RecordingReviewWindow
    {
        private readonly Button _cursorButton = new(), _keyboardButton = new() { IsVisible = false };
        private CheckBox? _showCursor;
        private readonly List<Control> _mouseControls = [];
        private Control? _cursorSize;
        private readonly List<HoverPropertyPanel> _propertyPanels = [];

        private void AddInputControls(StackPanel actions)
        {
            ConfigureIcon(_cursorButton, "MouseShortcutsHeading", "RecordingReviewCursor");
            var cursor = new StackPanel { Spacing = 5, MinWidth = 205 };
            _showCursor = Check("RecordingReviewShowCursor", _inputEffects.ShowCursor,
                value =>
                {
                    _inputEffects = _inputEffects with { ShowCursor = value };
                    _document.CaptureCursorChanged?.Invoke(value);
                });
            cursor.Children.Add(_showCursor);
            _cursorSize = Number("RecordingReviewCursorSize", 50, 200, _inputEffects.CursorSize,
                value => _inputEffects = _inputEffects with { CursorSize = (int)value }, labelAbove: true);
            cursor.Children.Add(_cursorSize);
            var buttons = new Grid
            {
                ColumnDefinitions = new("*,*"), RowDefinitions = new("Auto,Auto"), ColumnSpacing = 8, RowSpacing = 3,
            };
            var mouseButtons = new[]
            {
                ("RecordingReviewMouseLeft", 1), ("RecordingReviewMouseRight", 2), ("RecordingReviewMouseMiddle", 4),
            };
            for (var index = 0; index < mouseButtons.Length; index++)
            {
                var (key, bit) = mouseButtons[index];
                var button = Check(key, (_inputEffects.MouseButtons & bit) != 0,
                    value => _inputEffects = _inputEffects with
                    {
                        MouseButtons = value ? _inputEffects.MouseButtons | bit : _inputEffects.MouseButtons & ~bit,
                    });
                Grid.SetRow(button, index / 2);
                Grid.SetColumn(button, index % 2);
                buttons.Children.Add(button);
            }

            var wheel = Check("RecordingReviewMouseWheel", _inputEffects.ShowWheel,
                value => _inputEffects = _inputEffects with { ShowWheel = value });
            Grid.SetRow(wheel, 1);
            Grid.SetColumn(wheel, 1);
            buttons.Children.Add(wheel);
            cursor.Children.Add(buttons);
            _mouseControls.Add(buttons);
            cursor.Children.Add(Number("RecordingReviewAnimationSpeed", 0.2m, 1.8m,
                (decimal)_inputEffects.AnimationSpeed,
                value => _inputEffects = _inputEffects with { AnimationSpeed = (double)value }, 0.05m, true));
            AttachPropertyPanel(_cursorButton, cursor);
            actions.Children.Add(_cursorButton);

            ConfigureIcon(_keyboardButton, "SettingsLocalInput", "RecordingReviewKeyboard");
            var keyboard = new StackPanel { Spacing = 5, MinWidth = 205 };
            var categories = new Grid
            {
                ColumnDefinitions = new("*,*"), RowDefinitions = new("Auto,Auto"), ColumnSpacing = 8, RowSpacing = 3,
            };
            var keyboardCategories = new[]
            {
                ("RecordingReviewKeysModifiers", RecordingKeyboardCategories.Modifiers),
                ("RecordingReviewKeysCharacters", RecordingKeyboardCategories.Characters),
                ("RecordingReviewKeysCombinations", RecordingKeyboardCategories.Combinations),
                ("RecordingReviewKeysOther", RecordingKeyboardCategories.Other),
            };
            for (var index = 0; index < keyboardCategories.Length; index++)
            {
                var (key, category) = keyboardCategories[index];
                var checkbox = Check(key, (_inputEffects.KeyboardCategories & category) != 0,
                    value => _inputEffects = _inputEffects with
                    {
                        KeyboardCategories =
                        value
                            ? _inputEffects.KeyboardCategories | category
                            : _inputEffects.KeyboardCategories & ~category,
                    });
                ToolTip.SetTip(checkbox, UiText.Get(key + "Example"));
                Grid.SetRow(checkbox, index / 2);
                Grid.SetColumn(checkbox, index % 2);
                categories.Children.Add(checkbox);
            }

            keyboard.Children.Add(categories);
            keyboard.Children.Add(Number("RecordingReviewFontSize", 12, 48, _inputEffects.KeyboardFontSize,
                value => _inputEffects = _inputEffects with { KeyboardFontSize = (int)value }));
            keyboard.Children.Add(ColorRow(
                ColorPicker("RecordingReviewKeyText", _inputEffects.KeyboardTextColor,
                    value => _inputEffects = _inputEffects with { KeyboardTextColor = value }),
                ColorPicker("RecordingReviewBackground", _inputEffects.KeyboardBackground,
                    value => _inputEffects = _inputEffects with { KeyboardBackground = value })));
            AttachPropertyPanel(_keyboardButton, keyboard);
            actions.Children.Add(_keyboardButton);

            var progress = new Button();
            ConfigureIcon(progress, "AnimationTiming", "RecordingReviewProgress");
            var panel = new StackPanel { Spacing = 5, MinWidth = 205 };
            panel.Children.Add(Check("RecordingReviewShowProgress", _progressEffects.Enabled,
                value => _progressEffects = _progressEffects with
                {
                    Enabled = value,
                }));
            var foreground = ColorPicker("RecordingReviewForeground",
                _progressEffects.Bar ? _progressEffects.BarForeground : _progressEffects.TextForeground,
                value => _progressEffects = _progressEffects.Bar
                    ? _progressEffects with { BarForeground = value }
                    : _progressEffects with { TextForeground = value });
            var background = ColorPicker("RecordingReviewBackground",
                _progressEffects.Bar ? _progressEffects.BarBackground : _progressEffects.TextBackground,
                value => _progressEffects = _progressEffects.Bar
                    ? _progressEffects with { BarBackground = value }
                    : _progressEffects with { TextBackground = value });
            var styles = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            foreach (var (key, bar) in new[]
                         { ("RecordingReviewProgressText", false), ("RecordingReviewProgressBar", true) })
            {
                var style = new RadioButton
                {
                    Content = UiText.Get(key), GroupName = "ReviewProgressStyle",
                    IsChecked = _progressEffects.Bar == bar,
                };
                style.IsCheckedChanged += (_, _) =>
                {
                    if (style.IsChecked != true) return;
                    _progressEffects = _progressEffects with
                    {
                        Bar = bar,
                    };
                    foreground.Color =
                        Color.FromUInt32(bar ? _progressEffects.BarForeground : _progressEffects.TextForeground);
                    background.Color =
                        Color.FromUInt32(bar ? _progressEffects.BarBackground : _progressEffects.TextBackground);
                    EffectsChanged();
                };
                styles.Children.Add(style);
            }

            panel.Children.Add(new TextBlock { Text = UiText.Get("RecordingReviewCountdownStyle") });
            panel.Children.Add(styles);
            panel.Children.Add(Number("RecordingReviewFontSize", 12, 48, _progressEffects.FontSize,
                value => _progressEffects = _progressEffects with
                {
                    FontSize = (int)value,
                }));
            panel.Children.Add(ColorRow(foreground, background));
            AttachPropertyPanel(progress, panel);
            actions.Children.Add(progress);
        }

        private HoverPropertyPanel AttachPropertyPanel(Control target, Control content)
        {
            var attached = new HoverPropertyPanel(target, () => content, _ =>
            {
                foreach (var panel in _propertyPanels)
                    if (!ReferenceEquals(panel.Target, target))
                        panel.Hide();
            });
            _propertyPanels.Add(attached);
            return attached;
        }

        private void RefreshInputCapabilities()
        {
            _cursorButton.IsEnabled = _trace is { HasCursor: true } or { HasMouse: true };
            _keyboardButton.IsVisible = _trace?.HasKeyboard == true;
            _keyboardButton.IsEnabled = _trace?.HasKeyboard == true;
            _showCursor?.IsEnabled = _cursorButton.IsEnabled;
            _cursorSize?.IsEnabled = _trace?.HasCursor == true;
            foreach (var control in _mouseControls)
            {
                control.IsVisible = _cursorButton.IsEnabled;
                control.IsEnabled = _trace?.HasMouse == true;
            }
        }

        private void EffectsChanged()
        {
            if (_inputEffects != _persistedInputEffects)
            {
                _persistedInputEffects = _inputEffects;
                _document.InputEffectsChanged?.Invoke(_inputEffects);
            }

            if (_progressEffects != _persistedProgressEffects)
            {
                _persistedProgressEffects = _progressEffects;
                _document.ProgressEffectsChanged?.Invoke(_progressEffects);
            }

            _seekRevision++;
            RequestFrame(CurrentPosition());
        }

        private CheckBox Check(string key, bool value, Action<bool> changed)
        {
            var control = new CheckBox { Content = UiText.Get(key), IsChecked = value, MinHeight = 26 };
            control.IsCheckedChanged += (_, _) =>
            {
                changed(control.IsChecked == true);
                EffectsChanged();
            };
            return control;
        }

        private Control Number(string key, decimal minimum, decimal maximum, decimal value,
            Action<decimal> changed, decimal increment = 1, bool labelAbove = false)
        {
            var slider = new Slider
            {
                Minimum = (double)minimum, Maximum = (double)maximum, Value = (double)value,
                TickFrequency = (double)increment, IsSnapToTickEnabled = true, Width = 105, MinHeight = 26,
            };
            var text = new TextBlock
            {
                Text = value.ToString("0.##", CultureInfo.InvariantCulture),
                VerticalAlignment = VerticalAlignment.Center, MinWidth = 28, TextAlignment = TextAlignment.Right,
            };
            slider.PropertyChanged += (_, args) =>
            {
                if (args.Property != RangeBase.ValueProperty) return;
                var number = (decimal)slider.Value;
                text.Text = number.ToString("0.##", CultureInfo.InvariantCulture);
                changed(number);
                EffectsChanged();
            };
            var control = new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 4 };
            control.Children.Add(slider);
            Grid.SetColumn(text, 1);
            control.Children.Add(text);
            if (!labelAbove) return Row(key, control);
            slider.Width = double.NaN;
            control.HorizontalAlignment = HorizontalAlignment.Stretch;
            return new StackPanel { Spacing = 3, Children = { new TextBlock { Text = UiText.Get(key) }, control } };
        }

        private AppearanceColorPicker ColorPicker(string key, uint value, Action<uint> changed)
        {
            var control = new AppearanceColorPicker
            {
                Color = Color.FromUInt32(value), Width = 74, Height = 28, MinHeight = 0, MinWidth = 0,
                Padding = new(4), HorizontalAlignment = HorizontalAlignment.Left,
                IsAlphaEnabled = true, IsAlphaVisible = true, IsColorPaletteVisible = false,
            };
            control.Styles.Add(new AnnotationColorPickerStyles());
            ToolTip.SetTip(control, UiText.Get(key));
            var swatch = new Border
            {
                Width = 40, Height = 16, Margin = new(2), Background = new SolidColorBrush(control.Color),
                BorderBrush = AppTheme.Muted, BorderThickness = new(1),
            };
            control.Content = swatch;
            control.ColorChanged += (_, _) =>
            {
                swatch.Background = new SolidColorBrush(control.Color);
                changed(control.Color.ToUInt32());
                EffectsChanged();
            };
            return control;
        }

        private static Grid ColorRow(Control foreground, Control background)
        {
            var row = new Grid { ColumnDefinitions = new("*,*"), ColumnSpacing = 6 };
            row.Children.Add(foreground);
            Grid.SetColumn(background, 1);
            row.Children.Add(background);
            return row;
        }

        private static Grid Row(string key, Control control)
        {
            var grid = new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 8 };
            grid.Children.Add(new TextBlock { Text = UiText.Get(key), VerticalAlignment = VerticalAlignment.Center });
            Grid.SetColumn(control, 1);
            grid.Children.Add(control);
            return grid;
        }
    }
}
