using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    internal sealed partial class AnnotationPropertyView
    {
        private static readonly Lazy<string[]> SystemFontNames = new(() =>
        [
            .. FontManager.Current.SystemFonts.Select(font => font.Name)
                .Distinct(StringComparer.CurrentCultureIgnoreCase).Order(StringComparer.CurrentCultureIgnoreCase),
        ]);

        public Control Font()
        {
            var fonts = SystemFontNames.Value;
            string[] requestedFamilies = [FontManager.Current.DefaultFontFamily.Name, _session.Properties.FontFamily];
            foreach (var family in requestedFamilies)
                if (!string.IsNullOrWhiteSpace(family) &&
                    !fonts.Contains(family, StringComparer.CurrentCultureIgnoreCase))
                    fonts = [.. fonts.Append(family).Order(StringComparer.CurrentCultureIgnoreCase)];
            var combo = new ComboBox
            {
                ItemsSource = fonts, Width = 112, Height = 28, MinHeight = 0, Margin = new(2),
                Padding = new(6, 0), VerticalContentAlignment = VerticalAlignment.Center,
            };
            ToolTip.SetTip(combo, UiText.Get("AnimationFontFamily"));
            combo.SelectionChanged += async (_, _) =>
            {
                if (!_refreshing && !_disposed && combo.SelectedItem is string family)
                    await _session.UpdateSelectedPropertiesAsync(_session.Properties with { FontFamily = family });
            };
            _refresh.Add(() =>
            {
                var family = string.IsNullOrWhiteSpace(_session.Properties.FontFamily)
                    ? FontManager.Current.DefaultFontFamily.Name
                    : _session.Properties.FontFamily;
                if (!fonts.Contains(family, StringComparer.CurrentCultureIgnoreCase))
                {
                    fonts = [.. fonts.Append(family).Order(StringComparer.CurrentCultureIgnoreCase)];
                    combo.ItemsSource = fonts;
                }

                combo.SelectedItem = fonts.First(name =>
                    string.Equals(name, family, StringComparison.CurrentCultureIgnoreCase));
            });
            return combo;
        }

        public Control TextStyles()
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal };
            panel.Children.Add(Toggle("AnimationBold", "B", p => p.Bold, (p, value) => p with { Bold = value }));
            panel.Children.Add(Toggle("AnimationItalic", "I", p => p.Italic, (p, value) => p with { Italic = value }));
            panel.Children.Add(Toggle("AnnotationStrikeThrough", "S", p => p.StrikeThrough,
                (p, value) => p with { StrikeThrough = value }));
            return panel;
        }

        public Control TextOutline()
        {
            return TextPaint("AnnotationTextOutline", "AnimationText", p => p.TextOutline,
                (p, color) => p with { TextOutline = color }, true);
        }

        public Control TextBackground()
        {
            return TextPaint("AnnotationTextBackground", "AnimationRectangle", p => p.TextBackground,
                (p, color) => p with { TextBackground = color }, false);
        }

        private StackPanel TextPaint(string key, string icon, Func<ScreenshotAnnotationProperties, Color> read,
            Func<ScreenshotAnnotationProperties, Color, ScreenshotAnnotationProperties> write, bool outline)
        {
            var observed = read(_session.Properties);
            var lastEnabled = observed.A > 0
                ? observed
                : observed == Colors.Transparent
                    ? outline ? Colors.Black : Colors.White
                    : Color.FromArgb(255, observed.R, observed.G, observed.B);
            var panel = new StackPanel { Orientation = Orientation.Horizontal };
            panel.Children.Add(Toggle(key, "", p => read(p).A > 0,
                (p, value) =>
                {
                    var current = read(p);
                    Remember(current);
                    var color = value ? lastEnabled : Color.FromArgb(0, current.R, current.G, current.B);
                    Remember(color);
                    return write(p, color);
                }, icon));
            _refresh.Add(() => Remember(read(_session.Properties)));
            var settings = Popup(key, "ToolbarChevron", () =>
            {
                var content = new StackPanel { Spacing = 6 };
                content.Children.Add(Labeled(key, ColorPicker(key, () => read(_session.Properties),
                    color =>
                    {
                        Remember(color);
                        return _session.UpdateSelectedPropertiesAsync(write(_session.Properties, color));
                    })));
                if (outline)
                    content.Children.Add(Labeled("AnnotationOutlineWidth",
                        PropertyNumber("AnnotationOutlineWidth", 0, 16, p => p.TextOutlineWidth,
                            (p, value) => p with { TextOutlineWidth = value })));
                return content;
            });
            panel.Children.Add(settings);
            return panel;

            void Remember(Color color)
            {
                if (color.A > 0)
                    lastEnabled = color;
                else if (color != observed)
                    lastEnabled = Color.FromArgb(lastEnabled.A, color.R, color.G, color.B);
                observed = color;
            }
        }

        public Control TextLayout()
        {
            return Popup("AnnotationTextPadding", "SettingsAnnotationHeading", () =>
            {
                var panel = new StackPanel { Spacing = 6 };
                panel.Children.Add(Labeled("AnnotationTextPadding", PropertyNumber("AnnotationTextPadding", 0, 128,
                    p => p.TextPadding, (p, value) => p with { TextPadding = value })));
                panel.Children.Add(Labeled("AnnotationWrapWidth", PropertyNumber("AnnotationWrapWidth", 0, 8192,
                    p => p.WrapWidth, (p, value) => p with { WrapWidth = value }, true)));
                return panel;
            });
        }

        public void EditComment()
        {
            if (_disposed || _session.SelectedTool != ScreenshotTool.Arrow) return;
            ShowTextEditor(this);
        }

        private void ShowTextEditor(Control anchor)
        {
            if (_disposed || !_session.BeginTextPropertyEdit()) return;
            var generation = _session.TextEditGeneration;
            var text = new TextBox
            {
                Text = _session.TextEditValue,
                AcceptsReturn = true, Width = 240, MinHeight = 64, MaxHeight = 160, TextWrapping = TextWrapping.Wrap,
            };
            var flyout = new Flyout
            {
                Content = text, Placement = PlacementMode.Bottom, OverlayDismissEventPassThrough = true,
            };
            _flyouts.Add(flyout);
            text.TextChanged += (_, _) =>
            {
                if (!_disposed && OwnsEdit()) _session.UpdateTextEditDraft(text.Text ?? "");
            };
            text.AddHandler(KeyDownEvent, (_, args) =>
            {
                if (args.Handled) return;
                if (args.Key != Key.Escape &&
                    (args.Key != Key.Enter || (args.KeyModifiers & KeyModifiers.Shift) != 0)) return;
                Complete(args.Key != Key.Escape);
                flyout.Hide();
                args.Handled = true;
            }, RoutingStrategies.Tunnel);
            AnnotationTextInput.ProtectComposition(text);
            flyout.Opened += (_, _) =>
            {
                if (_disposed || !OwnsEdit())
                {
                    flyout.Hide();
                    return;
                }

                text.Focus();
                text.SelectAll();
            };
            flyout.Closed += (_, _) =>
            {
                Complete(true);
                flyout.Content = null;
                _flyouts.Remove(flyout);
            };
            flyout.ShowAt(anchor);
            return;

            bool OwnsEdit()
            {
                return _session.IsEditingText && _session.TextEditGeneration == generation;
            }

            void Complete(bool commit)
            {
                if (!OwnsEdit()) return;
                if (commit)
                {
                    _session.UpdateTextEditDraft(text.Text ?? "");
                    _session.CommitTextEdit();
                }
                else
                {
                    _session.CancelTextEdit();
                }
            }
        }
    }
}
