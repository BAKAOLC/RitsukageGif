using System.Globalization;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    internal sealed partial class SettingsWindow
    {
        private StackPanel CapturePreferencesPage(AppSettings draft, StackPanel clipboardOptions, out Action apply)
        {
            var value = draft.CapturePreferences.Normalize();
            var page = Page();
            var fields = new StackPanel { Spacing = 0 };
            var detection = Choice((int)value.DetectionMode, "CaptureDetectionNone", "CaptureDetectionWindows",
                "CaptureDetectionControls");
            var margin = new CompactNumberInput
                { Minimum = -128, Maximum = 128, Value = value.ElementMargin, Width = 100 };
            var expand = Toggle(value.ExpandOnOutsideClick);
            var quitOnForeign = Toggle(value.QuitOnForeignActivation);
            var cycleRegions = Toggle(value.CycleRegionHistory);
            var disableWheel = Toggle(value.DisableMouseWheel);
            var persistHistory = Toggle(value.PersistHistory);
            var blockedKeys = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            var blockLeft = Toggle((value.BlockedWindowsKeys & CaptureBlockedWindowsKeys.Left) != 0);
            var blockRight = Toggle((value.BlockedWindowsKeys & CaptureBlockedWindowsKeys.Right) != 0);
            blockLeft.Bind(ContentProperty, LiveText.Resource("CaptureWindowsKeyLeft"));
            blockRight.Bind(ContentProperty, LiveText.Resource("CaptureWindowsKeyRight"));
            blockLeft.IsEnabled = blockRight.IsEnabled = OperatingSystem.IsWindows();
            blockedKeys.Children.Add(blockLeft);
            blockedKeys.Children.Add(blockRight);
            var doubleClick = Choice((int)value.DoubleClickAction, "CaptureDoubleClickNone", "ScreenshotCopy",
                "CaptureDoubleClickOcrCopy", "ScreenshotQuickSave", "ScreenshotSave", "ScreenshotPin");
            var cursor = Choice((int)value.CursorPolicy, "CaptureCursorHidden", "CaptureCursorVisible");
            var auxiliary = Choice((int)value.AuxiliaryPolicy, "CaptureAuxiliaryHidden", "CaptureAuxiliaryAlt",
                "CaptureAuxiliaryAlways");
            var guideValues = new[]
            {
                value.GuideCursor, value.GuideSelectionCenter, value.GuideSelectionBorder, value.GuideMagnifier,
                value.GuideThirds, value.GuideMeasurements,
            };
            var guides = Contents(guideValues, "CaptureGuideCursor", "CaptureGuideCenter", "CaptureGuideBorder",
                "CaptureGuideMagnifier", "CaptureGuideThirds", "CaptureGuideMeasurements");
            var info = Toggle(value.ShowInformation);
            var magnifier = Toggle(value.ShowMagnifier);
            var detailValues = new[]
            {
                value.MagnifierGrid, value.MagnifierCrosshair, value.MagnifierCenterPixel,
                value.MagnifierSelectionBorder, value.MagnifierSelectionHandles, value.MagnifierCoordinates,
            };
            var details = Contents(detailValues, "CaptureMagnifierGrid", "CaptureMagnifierCrosshair",
                "CaptureMagnifierCenter", "CaptureGuideBorder", "CaptureMagnifierHandles",
                "CaptureMagnifierCoordinates");
            var zoom = new CompactNumberInput { Minimum = 2, Maximum = 32, Value = value.Magnification, Width = 100 };
            var mirrorHints = Toggle(value.MagnifierShortcuts);
            var color = new ColorPicker
            {
                Color = Color.Parse(value.MagnifierGridColor), Width = 120, Height = 28, MinHeight = 0,
                IsAlphaEnabled = true, IsAlphaVisible = true, IsColorPaletteVisible = false,
                IsHexInputVisible = true, HexInputAlphaPosition = AlphaComponentPosition.Leading,
            };
            color.Styles.Add(new AnnotationColorPickerStyles());
            var swatch = new Border { Background = new SolidColorBrush(color.Color), MinHeight = 16, MinWidth = 60 };
            color.Content = new Border
            {
                BorderBrush = AppTheme.Outline, BorderThickness = new(1),
                Child = new Panel
                {
                    Children =
                    {
                        new Border
                        {
                            [!Border.BackgroundProperty] =
                                new DynamicResourceExtension("ColorControlCheckeredBackgroundBrush"),
                        },
                        swatch,
                    },
                },
            };
            color.ColorChanged += (_, _) => swatch.Background = new SolidColorBrush(color.Color);
            var formats = value.ColorFormats;
            var format = ColorFormatsButton(formats);
            var smooth = Toggle(value.SmoothMagnifier);
            var hints = Toggle(value.ShowShortcutHints);
            var codes = Choice((int)value.CodeDetection, "CaptureCodeDisabled", "CaptureCodeManual",
                "CaptureCodeAutomatic", "CaptureCodeContents");
            Add("CaptureDetection", detection, static settings => settings.CapturePreferences.DetectionMode);
            Add("CaptureElementMargin", margin, static settings => settings.CapturePreferences.ElementMargin);
            Add("CaptureExpandOutside", expand, static settings => settings.CapturePreferences.ExpandOnOutsideClick);
            Add("CaptureDoubleClickAction", doubleClick,
                static settings => settings.CapturePreferences.DoubleClickAction);
            Add("", cursor, static settings => settings.CapturePreferences.CursorPolicy);
            var cursorLabel = (TextBlock)((Grid)((Border)fields.Children[^1]).Child!).Children[0];

            RefreshCursorLabel();
            SubscribeContentLanguage(RefreshCursorLabel);
            page.AttachedToVisualTree += (_, _) => RefreshCursorLabel();
            Add("CaptureAuxiliaryTiming", auxiliary, static settings => settings.CapturePreferences.AuxiliaryPolicy);
            Add("CaptureAuxiliaryContents", guides,
                static settings => (settings.CapturePreferences.GuideCursor,
                    settings.CapturePreferences.GuideSelectionCenter, settings.CapturePreferences.GuideSelectionBorder,
                    settings.CapturePreferences.GuideMagnifier, settings.CapturePreferences.GuideThirds,
                    settings.CapturePreferences.GuideMeasurements));
            Add("CaptureInformation", info, static settings => settings.CapturePreferences.ShowInformation);
            Add("CaptureMagnifier", magnifier, static settings => settings.CapturePreferences.ShowMagnifier);
            Add("CaptureMagnifierContents", details,
                static settings => (settings.CapturePreferences.MagnifierGrid,
                    settings.CapturePreferences.MagnifierCrosshair, settings.CapturePreferences.MagnifierCenterPixel,
                    settings.CapturePreferences.MagnifierSelectionBorder,
                    settings.CapturePreferences.MagnifierSelectionHandles,
                    settings.CapturePreferences.MagnifierCoordinates));
            Add("CaptureMagnification", zoom, static settings => settings.CapturePreferences.Magnification);
            Add("CaptureMagnifierShortcuts", mirrorHints,
                static settings => settings.CapturePreferences.MagnifierShortcuts);
            Add("CaptureMagnifierGridColor", color, static settings => settings.CapturePreferences.MagnifierGridColor,
                StringComparer.OrdinalIgnoreCase.Equals);
            Add("CaptureMagnifierColorFormat", format, static settings => settings.CapturePreferences.ColorFormats,
                static (left, right) => left.SequenceEqual(right));
            Add("CaptureMagnifierSmooth", smooth, static settings => settings.CapturePreferences.SmoothMagnifier);
            Add("CaptureShortcutHints", hints, static settings => settings.CapturePreferences.ShowShortcutHints);
            Add("CaptureCodeDetection", codes, static settings => settings.CapturePreferences.CodeDetection);
            var regionHistory = new CompactNumberInput
                { Minimum = 0, Maximum = 1000, Value = value.RegionHistoryCapacity, Width = 100 };
            var screenshotHistory = new CompactNumberInput
                { Minimum = 0, Maximum = 1000, Value = value.ScreenshotHistoryCapacity, Width = 100 };
            var historyMemory = new CompactNumberInput
                { Minimum = 0, Maximum = 512, Value = value.ScreenshotHistoryMaximumMiB, Width = 100 };
            Add("CaptureRegionHistoryCapacity", regionHistory,
                static settings => settings.CapturePreferences.RegionHistoryCapacity);
            Add("CaptureScreenshotHistoryCapacity", screenshotHistory,
                static settings => settings.CapturePreferences.ScreenshotHistoryCapacity);
            Add("CaptureScreenshotHistoryMemory", historyMemory,
                static settings => settings.CapturePreferences.ScreenshotHistoryMaximumMiB);
            Add("CapturePersistHistory", persistHistory, static settings => settings.CapturePreferences.PersistHistory);
            Add("CaptureCycleRegionHistory", cycleRegions,
                static settings => settings.CapturePreferences.CycleRegionHistory);
            Add("CaptureQuitOnForeignActivation", quitOnForeign,
                static settings => settings.CapturePreferences.QuitOnForeignActivation);
            Add("CaptureDisableMouseWheel", disableWheel,
                static settings => settings.CapturePreferences.DisableMouseWheel);
            Add("CaptureBlockedWindowsKeys", blockedKeys,
                static settings => settings.CapturePreferences.BlockedWindowsKeys);
            fields.Children.Add(clipboardOptions);
            Add("ToolbarActionsTitle", ToolbarCustomizationButton(draft, false),
                static settings => settings.Annotation.Toolbar.Actions,
                static (left, right) => left.SequenceEqual(right));
            page.Children.Add(MouseSettingsCard(fields));
            var scroll = draft.ScrollCapture.Normalize();
            var axis = Choice((int)scroll.InitialAxis, "CaptureScrollVertical", "CaptureScrollHorizontal");
            var scrollKeys = Toggle(scroll.RespondShortcuts);
            scrollKeys.Bind(ContentProperty, LiveText.Resource("CaptureScrollShortcuts"));
            var autoCrop = Toggle(scroll.AutoCropOnReverse);
            autoCrop.Bind(ContentProperty, LiveText.Resource("CaptureScrollAutoCrop"));
            var scrollFields = new StackPanel { Spacing = 4 };
            scrollFields.Children.Add(SettingRow("CaptureScrollInitialAxis", axis,
                static settings => settings.ScrollCapture.InitialAxis));
            scrollFields.Children.Add(Setting(autoCrop, static settings => settings.ScrollCapture.AutoCropOnReverse));
            scrollFields.Children.Add(Setting(scrollKeys, static settings => settings.ScrollCapture.RespondShortcuts));
            page.Children.Add(SettingsSection("ScrollCaptureTitle", scrollFields));
            apply = () =>
            {
                draft.CapturePreferences = value with
                {
                    DetectionMode = (CaptureDetectionMode)detection.SelectedIndex,
                    ElementMargin = (int)ReadSettingsNumber(margin), ExpandOnOutsideClick = expand.IsChecked == true,
                    QuitOnForeignActivation = quitOnForeign.IsChecked == true,
                    CycleRegionHistory = cycleRegions.IsChecked == true,
                    DisableMouseWheel = disableWheel.IsChecked == true,
                    PersistHistory = persistHistory.IsChecked == true,
                    BlockedWindowsKeys = (blockLeft.IsChecked == true ? CaptureBlockedWindowsKeys.Left : 0) |
                                         (blockRight.IsChecked == true ? CaptureBlockedWindowsKeys.Right : 0),
                    DoubleClickAction = (CaptureDoubleClickAction)doubleClick.SelectedIndex,
                    CursorPolicy = (CaptureCursorPolicy)cursor.SelectedIndex,
                    AuxiliaryPolicy = (CaptureAuxiliaryPolicy)auxiliary.SelectedIndex,
                    GuideCursor = guideValues[0], GuideSelectionCenter = guideValues[1],
                    GuideSelectionBorder = guideValues[2], GuideMagnifier = guideValues[3],
                    GuideThirds = guideValues[4], GuideMeasurements = guideValues[5],
                    ShowInformation = info.IsChecked == true, ShowMagnifier = magnifier.IsChecked == true,
                    MagnifierGrid = detailValues[0], MagnifierCrosshair = detailValues[1],
                    MagnifierCenterPixel = detailValues[2], MagnifierSelectionBorder = detailValues[3],
                    MagnifierSelectionHandles = detailValues[4], MagnifierCoordinates = detailValues[5],
                    RegionHistoryCapacity = (int)ReadSettingsNumber(regionHistory),
                    ScreenshotHistoryCapacity = (int)ReadSettingsNumber(screenshotHistory),
                    ScreenshotHistoryMaximumMiB = (int)ReadSettingsNumber(historyMemory),
                    Magnification = (int)ReadSettingsNumber(zoom), MagnifierShortcuts = mirrorHints.IsChecked == true,
                    MagnifierGridColor = color.Color.ToString(),
                    ColorFormats = [.. formats],
                    MagnifierColorFormatId = SampleColorFormats.First(formats, value.MagnifierColorFormatId).Id,
                    SmoothMagnifier = smooth.IsChecked == true, ShowShortcutHints = hints.IsChecked == true,
                    CodeDetection = (CaptureCodeDetectionMode)codes.SelectedIndex,
                };
                draft.ScrollCapture = scroll with
                {
                    InitialAxis = (ScrollCaptureAxis)axis.SelectedIndex,
                    RespondShortcuts = scrollKeys.IsChecked == true,
                    AutoCropOnReverse = autoCrop.IsChecked == true,
                };
                draft.Cursor = draft.CapturePreferences.CursorPolicy == CaptureCursorPolicy.Visible;
                draft.SelectionDetectControls = draft.CapturePreferences.DetectionMode == CaptureDetectionMode.Controls;
            };
            return page;

            void RefreshCursorLabel()
            {
                var definition =
                    SelectionCommands.Catalog.First(entry => entry.DescriptionKey == "SelectionCommandCursor");
                var gestures = BuiltinShortcuts.SelectionGestures(definition.Id,
                    definition.DefaultGestures, draft.SelectionShortcuts);
                cursorLabel.Text = gestures.Count > 0
                    ? string.Format(CultureInfo.CurrentCulture, UiText.Get("CaptureCursorToggleLabel"),
                        string.Join(" / ", gestures.Select(KeyGestureDisplay.Format)))
                    : UiText.Get("CaptureCursor");
            }

            void Add<T>(string key, Control control, Func<AppSettings, T> getter, Func<T, T, bool>? equals = null)
            {
                Grid row;
                if (control is CheckBox check)
                {
                    check.Bind(ContentProperty, LiveText.Resource(key));
                    Setting(check, getter, equals);
                    check.HorizontalAlignment = HorizontalAlignment.Left;
                    row = new() { MinHeight = 32 };
                    row.Children.Add(check);
                }
                else
                {
                    row = SettingRow(key, control, getter, equals);
                    if (control is ColorPicker) control.HorizontalAlignment = HorizontalAlignment.Right;
                }

                fields.Children.Add(new Border
                {
                    Child = row, Padding = new(0, 2), BorderBrush = AppTheme.Outline, BorderThickness = new(0, 0, 0, 1),
                });
            }

            ComboBox Choice(int index, params string[] keys)
            {
                var box = new ComboBox { SelectedIndex = index };

                Refresh();
                SubscribeContentLanguage(Refresh);
                return box;

                void Refresh()
                {
                    var selected = box.SelectedIndex;
                    box.ItemsSource = keys.Select(UiText.Get).ToArray();
                    box.SelectedIndex = selected < 0 ? index : selected;
                }
            }

            Button Contents(bool[] values, params string[] keys)
            {
                var button = new Button { HorizontalContentAlignment = HorizontalAlignment.Stretch };
                var options = new StackPanel { Spacing = 4, Margin = new(8) };
                var summary = new TextBlock { TextTrimming = TextTrimming.CharacterEllipsis };
                var content = new Grid { ColumnDefinitions = new("*,16"), ColumnSpacing = 8 };
                content.Children.Add(summary);
                var arrow = DropDownChevron.Create(button);
                Grid.SetColumn(arrow, 1);
                content.Children.Add(arrow);
                button.Content = content;

                for (var index = 0; index < keys.Length; index++)
                {
                    var item = index;
                    var check = Toggle(values[item]);
                    check.Bind(ContentProperty, LiveText.Resource(keys[item]));
                    check.IsCheckedChanged += (_, _) =>
                    {
                        values[item] = check.IsChecked == true;
                        Refresh();
                        RequestLiveApply();
                    };
                    options.Children.Add(check);
                }

                button.Flyout = new Flyout { Content = options };
                Refresh();
                SubscribeContentLanguage(Refresh);
                return button;

                void Refresh()
                {
                    summary.Text = string.Join("; ", keys.Where((_, index) => values[index]).Select(UiText.Get));
                    if (summary.Text.Length == 0) summary.Text = UiText.Get("CaptureContentsNone");
                    ToolTip.SetTip(button, summary.Text);
                }
            }
        }

        private static CheckBox Toggle(bool value)
        {
            return new() { IsChecked = value };
        }
    }
}
