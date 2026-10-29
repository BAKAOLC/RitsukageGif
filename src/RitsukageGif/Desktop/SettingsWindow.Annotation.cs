using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    internal sealed partial class SettingsWindow
    {
        private StackPanel AnnotationPage(AppSettings draft, out Action apply)
        {
            var page = Page();
            var value = draft.Annotation.Normalize();
            var palette = value.ColorPalette.Select(Color.Parse).ToList();
            var common = new StackPanel { Spacing = 0 };
            var sharedColor = Check("AnnotationSharedColor", value.SharedColor);
            var paletteButton = PaletteEditor(palette);
            var paletteRow = new Grid { ColumnDefinitions = new("*,Auto"), MinHeight = 38 };
            paletteRow.Children.Add(sharedColor);
            Grid.SetColumn(paletteButton, 1);
            paletteRow.Children.Add(paletteButton);
            common.Children.Add(paletteRow);
            var secondary = Choice("AnnotationSecondaryEditing", value.SecondaryEditingEnabled ? 0 : 1,
                "AnnotationEnabled", "AnnotationDisabled");
            Add(common, "AnnotationSecondaryEditing", secondary);
            var quickMark = Choice("AnnotationQuickSettings", value.QuickSettingsEnabled ? 0 : 1,
                "AnnotationEnabled", "AnnotationDisabled");
            Add(common, "AnnotationQuickSettings", quickMark);
            var cursor = Choice("AnnotationPencilCursor", (int)value.PencilCursor,
                "AnnotationCursorCrosshairRing", "AnnotationCursorCrosshair", "AnnotationCursorRing");
            Add(common, "AnnotationPencilCursor", cursor);
            var maximum = Number("AnnotationMaximumBrushThickness", value.MaximumBrushThickness, 1, 1024, 2);
            Add(common, "AnnotationMaximumBrushThickness", maximum);
            var rememberPinned = Check("AnnotationRememberPinnedTool", value.RememberPinnedTool);
            Add(common, "AnnotationRememberPinnedTool", rememberPinned);
            var rememberTool = Check("AnnotationRememberTool", value.RememberLastTool);
            Add(common, "AnnotationRememberTool", rememberTool);
            var limit = Check("AnnotationLimitSelection", value.LimitToSelection);
            Add(common, "AnnotationLimitSelection", limit);
            var wheel = Check("AnnotationDisableWheelAdjustment", value.DisableWheelToolAdjustment);
            Add(common, "AnnotationDisableWheelAdjustment", wheel);
            var quickMargin = Number("AnnotationQuickMarkMargin", value.QuickSettingsPadding, 0, 64, 0);
            Add(common, "AnnotationQuickMarkMargin", quickMargin);
            Add(common, "ToolbarToolsTitle", ToolbarCustomizationButton(draft, true));
            page.Children.Add(MouseSettingsCard(common));

            var geometry = Section("AnnotationGeometryBehavior");
            var controlPoints = Choice("AnnotationControlPointVisibility", (int)value.ControlPointVisibility,
                "AnnotationControlPointsHover", "AnnotationControlPointsAlways", "AnnotationControlPointsHidden");
            Add(geometry, "AnnotationControlPointVisibility", controlPoints);

            var numbers = Section("AnnotationNumberBehavior");
            var renumber = Check("AnnotationRenumberOnDelete", value.RenumberOnDelete);
            var fromZero = Check("AnnotationNumberStartsAtZero", value.NumberStartsAtZero);
            var globalNumbers = Check("AnnotationGlobalNumberCounter", value.GlobalNumberCounter);
            Add(numbers, "AnnotationRenumberOnDelete", renumber);
            Add(numbers, "AnnotationNumberStartsAtZero", fromZero);
            Add(numbers, "AnnotationGlobalNumberCounter", globalNumbers);

            var pencil = Section("AnnotationPencilBehavior");
            var shift = Choice("AnnotationPencilShiftMode", (int)value.PencilShiftMode,
                "AnnotationShiftFreeAngle", "AnnotationShiftSnapAngle");
            Add(pencil, "AnnotationPencilShiftMode", shift);
            var smooth = Check("AnnotationSmoothBrush", value.SmoothBrush);
            Add(pencil, "AnnotationSmoothBrush", smooth);

            var polyline = Section("AnnotationToolPolyline");
            var joins = new[] { PenLineJoin.Round, PenLineJoin.Bevel, PenLineJoin.Miter };
            var caps = new[] { PenLineCap.Round, PenLineCap.Flat, PenLineCap.Square };
            var join = Choice("AnnotationPolylineJoin", Array.IndexOf(joins, value.PolylineJoin),
                "AnnotationJoinRound", "AnnotationJoinBevel", "AnnotationJoinMiter");
            var cap = Choice("AnnotationPolylineCap", Array.IndexOf(caps, value.PolylineCap),
                "AnnotationCapRound", "AnnotationCapFlat", "AnnotationCapSquare");
            Add(polyline, "AnnotationPolylineJoin", join);
            Add(polyline, "AnnotationPolylineCap", cap);

            var arrow = Section("ScreenshotArrow");
            var suppressComment = Check("AnnotationArrowSuppressComment", !value.ArrowDoubleClickCommentEnabled);
            var hideCurveControls = Check("AnnotationArrowHideCurveControls", !value.ArrowCurveControlPoints);
            Add(arrow, "AnnotationArrowSuppressComment", suppressComment);
            Add(arrow, "AnnotationArrowHideCurveControls", hideCurveControls);

            var text = Section("AnnotationTextBehavior");
            var wrapping = Choice("AnnotationTextWrapMode", (int)value.TextWrapMode,
                "AnnotationWrapWordOrCharacter", "AnnotationWrapNone");
            Add(text, "AnnotationTextWrapMode", wrapping);

            var eraser = Section("AnimationEraser");
            var eraserEditing = Check("AnnotationEraserSecondaryEditing", value.EraserSecondaryEditingEnabled);
            Add(eraser, "AnnotationEraserSecondaryEditing", eraserEditing);
            var privacy = Section("AnnotationAutoMosaic");
            var autoPrivacy = Check("AnnotationAutoPrivacy", value.AutoSelectPrivacyMosaic);
            Add(privacy, "AnnotationAutoPrivacy", autoPrivacy);
            apply = () => draft.Annotation = value with
            {
                SharedColor = sharedColor.IsChecked == true,
                ColorPalette = [.. palette.Select(color => color.ToString())],
                SecondaryEditingEnabled = secondary.SelectedIndex == 0,
                QuickSettingsEnabled = quickMark.SelectedIndex == 0,
                QuickSettingsPadding = (double)ReadSettingsNumber(quickMargin),
                PencilCursor = (AnnotationPencilCursor)cursor.SelectedIndex,
                MaximumBrushThickness = (double)ReadSettingsNumber(maximum),
                RememberPinnedTool = rememberPinned.IsChecked == true,
                RememberLastTool = rememberTool.IsChecked == true,
                LimitToSelection = limit.IsChecked == true, DisableWheelToolAdjustment = wheel.IsChecked == true,
                ControlPointVisibility = (AnnotationControlPointVisibility)controlPoints.SelectedIndex,
                RenumberOnDelete = renumber.IsChecked == true, NumberStartsAtZero = fromZero.IsChecked == true,
                GlobalNumberCounter = globalNumbers.IsChecked == true,
                PencilShiftMode = (AnnotationPencilShiftMode)shift.SelectedIndex,
                SmoothBrush = smooth.IsChecked == true, PolylineJoin = joins[Math.Max(0, join.SelectedIndex)],
                PolylineCap = caps[Math.Max(0, cap.SelectedIndex)],
                TextWrapMode = (AnnotationTextWrapMode)wrapping.SelectedIndex,
                ArrowDoubleClickCommentEnabled = suppressComment.IsChecked != true,
                ArrowCurveControlPoints = hideCurveControls.IsChecked != true,
                EraserSecondaryEditingEnabled = eraserEditing.IsChecked == true,
                AutoSelectPrivacyMosaic = autoPrivacy.IsChecked == true,
                Toolbar = draft.Annotation.Toolbar,
            };
            return page;

            StackPanel Section(string key)
            {
                var fields = new StackPanel { Spacing = 0 };
                var section = SettingsSection(key, fields);
                section.Name = key;
                page.Children.Add(section);
                return fields;
            }

            static CheckBox Check(string key, bool initial)
            {
                var check = new CheckBox { Name = key, IsChecked = initial };
                check.Bind(ContentProperty, LiveText.Resource(key));
                return check;
            }

            static CompactNumberInput Number(string key, double initial, decimal minimum, decimal maximum, int decimals)
            {
                return new()
                {
                    Name = key, Minimum = minimum, Maximum = maximum, DecimalPlaces = decimals,
                    Increment = 1, Value = (decimal)initial, Width = 100,
                };
            }

            static void Add(StackPanel fields, string key, Control control)
            {
                Grid row;
                if (control is CheckBox check)
                {
                    check.HorizontalAlignment = HorizontalAlignment.Left;
                    row = new() { MinHeight = 38, Children = { check } };
                }
                else
                {
                    row = Labeled(key, control);
                }

                fields.Children.Add(new Border
                {
                    Child = row, Padding = new(0, 4), BorderBrush = AppTheme.Outline,
                    BorderThickness = new(0, 0, 0, 1),
                });
            }

            ComboBox Choice(string key, int selectedIndex, params string[] keys)
            {
                var box = new ComboBox { Name = key };

                Refresh();
                SubscribeContentLanguage(Refresh);
                return box;

                void Refresh()
                {
                    var selected = box.SelectedIndex;
                    box.ItemsSource = keys.Select(UiText.Get).ToArray();
                    box.SelectedIndex = selected < 0 ? Math.Max(0, selectedIndex) : selected;
                }
            }
        }

        private Button PaletteEditor(List<Color> colors)
        {
            var button = new Button { Name = "AnnotationEditPalette" };
            button.Bind(ContentProperty, LiveText.Resource("AnnotationEditPalette"));
            var rows = new StackPanel { Spacing = 6 };
            var content = new StackPanel { Spacing = 8, Width = 224 };
            var scroll = new ScrollViewer
            {
                MaxHeight = 320, Content = rows,
                [!FontFamilyProperty] = new DynamicResourceExtension("ApplicationFont"),
            };
            content.Children.Add(scroll);
            var add = new Button { HorizontalAlignment = HorizontalAlignment.Stretch };
            add.Bind(ContentProperty, LiveText.Resource("AnnotationPaletteAdd"));
            content.Children.Add(add);
            add.Click += (_, _) =>
            {
                if (colors.Count >= 32) return;
                colors.Add(Colors.White);
                Refresh();
                RequestLiveApply();
            };
            Refresh();
            button.Flyout = new Flyout { Content = content };
            return button;

            void Refresh()
            {
                rows.Children.Clear();
                add.IsEnabled = colors.Count < 32;
                for (var index = 0; index < colors.Count; index++)
                {
                    var item = index;
                    var picker = new AppearanceColorPicker
                    {
                        Color = colors[item], Height = 28, MinHeight = 0,
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                        IsAlphaEnabled = true, IsAlphaVisible = true, IsColorPaletteVisible = false,
                        IsHexInputVisible = true, IsComponentTextInputVisible = true,
                        HexInputAlphaPosition = AlphaComponentPosition.Leading,
                    };
                    picker.Styles.Add(new AnnotationColorPickerStyles());
                    var swatch = new Border { Background = new SolidColorBrush(colors[item]), Margin = new(1) };
                    picker.Content = new Panel
                    {
                        Children =
                        {
                            new Border
                            {
                                [!Border.BackgroundProperty] =
                                    new DynamicResourceExtension("ColorControlCheckeredBackgroundBrush"),
                                Margin = new(1),
                            },
                            swatch,
                        },
                    };
                    picker.ColorChanged += (_, _) =>
                    {
                        colors[item] = picker.Color;
                        swatch.Background = new SolidColorBrush(picker.Color);
                        RequestLiveApply();
                    };
                    var remove = new Button
                    {
                        Content = "×", Width = 28, Height = 28, MinHeight = 0, Padding = new(0),
                        IsEnabled = colors.Count > 1,
                    };
                    remove.Bind(AutomationProperties.NameProperty, LiveText.Resource("AnimationDelete"));
                    remove.Click += (_, _) =>
                    {
                        colors.RemoveAt(item);
                        Refresh();
                        RequestLiveApply();
                    };
                    var row = new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 8 };
                    row.Children.Add(picker);
                    Grid.SetColumn(remove, 1);
                    row.Children.Add(remove);
                    rows.Children.Add(row);
                }
            }
        }
    }
}
