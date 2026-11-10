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
            var combo = new AnnotationEditableCombo(fonts) { Width = 112 };
            ToolTip.SetTip(combo, UiText.Get("AnimationFontFamily"));
            combo.TextChanged += async (_, _) =>
            {
                if (!_refreshing && !_disposed && combo.Text is { } family &&
                    FontFamilyForDisplay(family) != FontFamilyForDisplay(_session.Properties.FontFamily))
                    await _session.UpdateSelectedPropertiesAsync(_session.Properties with { FontFamily = family });
            };
            _refresh.Add(() =>
            {
                var family = FontFamilyForDisplay(_session.Properties.FontFamily);
                if (!combo.IsKeyboardFocusWithin && combo.Text != family) combo.Text = family;
            });
            combo.LostFocus += (_, _) => Refresh();
            return combo;
        }

        private static string FontFamilyForDisplay(string? value)
        {
            return value?.Trim() is { Length: > 0 and <= 256 } family
                ? family
                : FontManager.Current.DefaultFontFamily.Name;
        }

        public Control TextStyles()
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal };
            panel.Children.Add(Toggle("AnimationBold", "B", p => p.Bold, (p, value) => p with { Bold = value }));
            panel.Children.Add(Toggle("AnimationItalic", "I", p => p.Italic, (p, value) => p with { Italic = value }));
            return panel;
        }

        public Control TextOutline()
        {
            return TextPaint("AnnotationTextOutline", "AnnotationTextOutline", true);
        }

        public Control TextBackground()
        {
            return TextPaint("AnnotationTextBackground", "AnimationRectangle", false);
        }

        private Button TextPaint(string key, string icon, bool outline)
        {
            var button = Toggle(key, "",
                properties => outline ? properties.TextOutlineEnabled : properties.TextBackgroundEnabled,
                (properties, enabled) =>
                    outline
                        ? properties with { TextOutlineEnabled = enabled }
                        : properties with { TextBackgroundEnabled = enabled }, icon);
            AttachHoverPanel(button, () =>
            {
                var content = new StackPanel { Spacing = 3 };
                if (outline)
                {
                    content.Children.Add(PropertySlider("AnnotationOutlineWidth", 1, 50,
                        () => _session.Properties.TextOutlineWidth,
                        value => _session.UpdateSelectedPropertiesAsync(_session.Properties with
                        {
                            TextOutlineWidth = value,
                        })));
                }
                else
                {
                    content.Children.Add(PropertySlider("AnnotationOpacity", 0, 100,
                        () => _session.Properties.TextBackgroundOpacity * 100,
                        value => _session.UpdateSelectedPropertiesAsync(_session.Properties with
                        {
                            TextBackgroundOpacity = value / 100,
                        }), "%"));
                    content.Children.Add(PropertySlider("AnnotationCornerRadius", 0, 30,
                        () => _session.Properties.TextBackgroundRadius,
                        value => _session.UpdateSelectedPropertiesAsync(_session.Properties with
                        {
                            TextBackgroundRadius = value,
                        })));
                    content.Children.Add(PropertySlider("AnnotationTextPadding", 0, 50,
                        () => _session.Properties.TextPadding,
                        value => _session.UpdateSelectedPropertiesAsync(
                            _session.Properties with { TextPadding = value })));
                }

                content.Children.Add(Palette(key,
                    () => outline ? _session.Properties.TextOutline : _session.Properties.TextBackground,
                    color => _session.UpdateSelectedPropertiesAsync(outline
                        ? _session.Properties with { TextOutline = color }
                        : _session.Properties with { TextBackground = color }), false));
                return content;
            }, () => outline ? _session.Properties.TextOutlineEnabled : _session.Properties.TextBackgroundEnabled);
            return button;
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
                Content = WatermarkTextEditorContent(text), Placement = PlacementMode.Bottom,
                OverlayDismissEventPassThrough = true,
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
