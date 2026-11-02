using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    internal sealed partial class SettingsWindow
    {
        private StackPanel PinsPage(PinnedImagePreferences preferences,
            out Func<PinnedImagePreferences> read)
        {
            preferences = preferences.Normalize();
            var page = Page();
            var supportedKinds = preferences.SupportedContentKinds;
            var supportedChoices = new StackPanel { Spacing = 2, Margin = new(6) };
            var supportedSummary = new TextBlock { TextTrimming = TextTrimming.CharacterEllipsis };
            var supported = new Button
            {
                Width = 190, MinHeight = 28, HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Flyout = new Flyout { Content = supportedChoices },
            };
            var supportedContent = new Grid { ColumnDefinitions = new("*,16"), ColumnSpacing = 8 };
            supportedContent.Children.Add(supportedSummary);
            var supportedArrow = DropDownChevron.Create(supported);
            Grid.SetColumn(supportedArrow, 1);
            supportedContent.Children.Add(supportedArrow);
            supported.Content = supportedContent;
            var kindOptions = new[]
            {
                (PinnedContentKinds.Images, "PinnedContentImages"),
                (PinnedContentKinds.Text, "PinnedContentText"),
                (PinnedContentKinds.Files, "PinnedContentFiles"),
                (PinnedContentKinds.Color, "PinnedContentColor"),
                (PinnedContentKinds.Formula, "PinnedContentFormula"),
            };
            foreach (var (kind, key) in kindOptions)
            {
                var choice = new CheckBox { IsChecked = (supportedKinds & kind) != 0 };
                choice.Bind(ContentProperty, LiveText.Resource(key));
                choice.IsCheckedChanged += (_, _) =>
                {
                    supportedKinds = choice.IsChecked == true ? supportedKinds | kind : supportedKinds & ~kind;
                    RefreshSupportedTypes();
                    RequestLiveApply();
                };
                supportedChoices.Children.Add(choice);
            }

            SubscribeContentLanguage(RefreshSupportedTypes);
            RefreshSupportedTypes();
            var supportedRow = new StackPanel();
            Row(supportedRow, "PinnedContentSupportedTypes", supported);
            Setting(supported, static settings => settings.PinnedImages.SupportedContentKinds);
            page.Children.Add(MouseSettingsCard(supportedRow));
            var display = Group("PinnedImageDisplay");
            var shadow = Setting(Check(display, "PinnedImageWindowShadow", preferences.ShowWindowShadow),
                static settings => settings.PinnedImages.ShowWindowShadow);
            var topmost = Setting(Check(display, "PinnedImageDefaultTopmost", preferences.Topmost),
                static settings => settings.PinnedImages.Topmost);
            var fit = Setting(Check(display, "PinnedImageAutoFit", preferences.AutoFit),
                static settings => settings.PinnedImages.AutoFit);
            var taskbar =
                Setting(Check(display, "PinnedImageTaskbarWhenNotTopmost", preferences.ShowTaskbarWhenNotTopmost),
                    static settings => settings.PinnedImages.ShowTaskbarWhenNotTopmost);
            var opacity = Setting(Number(display, "PinnedImageDefaultOpacity", preferences.Opacity * 100, 15, 100),
                static settings => settings.PinnedImages.Opacity);
            var titlePosition = Setting(Choice(display, "PinnedTitlePosition", (int)preferences.TitlePosition,
                "PinnedTitleBottom", "PinnedTitleTop"), static settings => settings.PinnedImages.TitlePosition);
            var maximumWindowSize = Setting(Number(display, "PinnedMaximumWindowSize", preferences.MaximumWindowSize,
                    PinnedViewportGeometry.MinimumWindowSizeSetting, PinnedViewportGeometry.MaximumWindowSizeSetting),
                static settings => settings.PinnedImages.MaximumWindowSize);
            var committedMaximum = preferences.MaximumWindowSize;
            maximumWindowSize.LostFocus += (_, _) =>
            {
                if (!maximumWindowSize.IsKeyboardFocusWithin) CommitMaximum();
            };
            maximumWindowSize.KeyDown += (_, args) =>
            {
                if (args.Key == Key.Enter) CommitMaximum();
            };
            var behavior = Group("PinnedImageBehavior");
            var replacementAction = Setting(Choice(behavior, "PinnedImageReplacementAction",
                    (int)preferences.ReplacementAction, "PinnedImageReplacementKeepSource",
                    "PinnedImageReplacementCloseSource", "PinnedImageReplacementDestroySource"),
                static settings => settings.PinnedImages.ReplacementAction);
            var selectable = Setting(Check(behavior, "PinnedImageSelectableText", preferences.SelectableText),
                static settings => settings.PinnedImages.SelectableText);
            var border = Setting(Check(behavior, "PinnedImageBorderResize", preferences.ResizeFromBorder),
                static settings => settings.PinnedImages.ResizeFromBorder);
            var lockHint = Setting(Check(behavior, "PinnedImageLockHint", preferences.ShowLockIndicator),
                static settings => settings.PinnedImages.ShowLockIndicator);
            var lockedOptions = new WrapPanel { Orientation = Orientation.Horizontal, MaxWidth = 380 };
            var lockMove = LockedChoice("PinnedImageLockMove", PinnedLockRestrictions.Move);
            var lockPointerClose = LockedChoice("PinnedImageLockPointerClose", PinnedLockRestrictions.PointerClose);
            var lockKeyClose = LockedChoice("PinnedImageLockKeyboardClose", PinnedLockRestrictions.KeyboardClose);
            var lockDrop = LockedChoice("PinnedImageLockIncomingDrop", PinnedLockRestrictions.IncomingDrop);
            Row(behavior, "PinnedImageLockedOperations", lockedOptions);
            Setting(lockedOptions, static settings => settings.PinnedImages.LockRestrictions);
            var shortcuts = Setting(Check(behavior, "PinnedImageMenuShortcuts", preferences.ShowMenuShortcuts),
                static settings => settings.PinnedImages.ShowMenuShortcuts);
            var shake = Setting(Check(behavior, "PinnedHideOthersOnShake", preferences.HideOthersOnShake),
                static settings => settings.PinnedImages.HideOthersOnShake);
            var keepOthers = Setting(
                Check(behavior, "PinnedKeepOthersHiddenOnNewPin", preferences.KeepOthersHiddenOnNewPin),
                static settings => settings.PinnedImages.KeepOthersHiddenOnNewPin);
            var attention0 = Setting(Check(behavior, "PinnedActivateOnNewPin", preferences.ActivateOnNewPin),
                static settings => settings.PinnedImages.ActivateOnNewPin);
            var attention1 = Setting(Check(behavior, "PinnedFlashOnNewPin", preferences.FlashOnNewPin),
                static settings => settings.PinnedImages.FlashOnNewPin);
            var attention2 = Setting(Check(behavior, "PinnedFlashAfterGroupSwitch", preferences.FlashAfterGroupSwitch),
                static settings => settings.PinnedImages.FlashAfterGroupSwitch);
            var attention3 = Setting(
                Check(behavior, "PinnedTranslucentOnAttention", preferences.TranslucentOnAttention),
                static settings => settings.PinnedImages.TranslucentOnAttention);
            var current = Setting(Check(behavior, "PinnedImageCopyVisibleRegion", preferences.CopyVisibleRegion),
                static settings => settings.PinnedImages.CopyVisibleRegion);
            var outputContent = Setting(Choice(behavior, "PinnedImageOutputContent", (int)preferences.OutputContent,
                    "PinnedImageOutputCurrent", "PinnedImageOutputOriginal"),
                static settings => settings.PinnedImages.OutputContent);
            var thumbnailWheel =
                Setting(Check(behavior, "PinnedImageThumbnailWheelZoom", preferences.ThumbnailWheelZoom),
                    static settings => settings.PinnedImages.ThumbnailWheelZoom);
            var thumbnailText = Setting(Check(behavior, "PinnedImageThumbnailSelectableText",
                preferences.ThumbnailSelectableText), static settings => settings.PinnedImages.ThumbnailSelectableText);
            const int thumbnailFactory = PinnedViewportGeometry.DefaultThumbnailSourceSize;
            const int thumbnailMaximum = int.MaxValue;
            var quickWidth = new CompactNumberInput
            {
                Name = "PinnedQuickThumbnailWidth", Minimum = 1, Maximum = thumbnailMaximum,
                Value = preferences.QuickThumbnailWidth ?? thumbnailFactory, Width = 104, Height = 28,
            };
            var quickHeight = new CompactNumberInput
            {
                Name = "PinnedQuickThumbnailHeight", Minimum = 1, Maximum = thumbnailMaximum,
                Value = preferences.QuickThumbnailHeight ?? thumbnailFactory, Width = 104, Height = 28,
            };
            quickWidth.Bind(ToolTip.TipProperty, LiveText.Resource("PinnedQuickThumbnailWidth"));
            quickHeight.Bind(ToolTip.TipProperty, LiveText.Resource("PinnedQuickThumbnailHeight"));
            var quickSize = new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 6,
                Children =
                {
                    quickWidth, new TextBlock { Text = "×", VerticalAlignment = VerticalAlignment.Center }, quickHeight,
                },
            };
            Row(behavior, "PinnedQuickThumbnailSize", quickSize);
            Setting(quickSize, settings => (settings.PinnedImages.QuickThumbnailWidth ?? thumbnailFactory,
                settings.PinnedImages.QuickThumbnailHeight ?? thumbnailFactory));
            var quickWidthEdited = false;
            var quickHeightEdited = false;
            quickWidth.ValueChanged += (_, _) => quickWidthEdited = true;
            quickHeight.ValueChanged += (_, _) => quickHeightEdited = true;
            var zooming = Group("PinnedImageZoom");
            var zoom = Setting(Number(zooming, "PinnedImageZoomStep", preferences.ZoomStep * 100, 1, 100),
                static settings => settings.PinnedImages.ZoomStep);
            var fine = new CompactNumberInput
            {
                Minimum = 0.1m, Maximum = 10, Increment = 0.1m, DecimalPlaces = 1,
                Value = (decimal)(preferences.FineZoomStep * 100), Width = 104, Height = 28,
            };
            Row(zooming, "PinnedImageFineZoomStep", fine);
            Setting(fine, static settings => settings.PinnedImages.FineZoomStep);
            var opacityStep = Setting(Number(zooming, "PinnedImageOpacityStep", preferences.OpacityStep * 100, 1, 50),
                static settings => settings.PinnedImages.OpacityStep);
            var through =
                Setting(Number(zooming, "PinnedImageThroughOpacity", preferences.MouseThroughOpacity * 100, 15, 100),
                    static settings => settings.PinnedImages.MouseThroughOpacity);
            var wheelAnchor = Setting(Choice(zooming, "PinnedImageWheelAnchor", (int)preferences.WheelZoomAnchor,
                    "PinnedImageAnchorTopLeft", "PinnedImageAnchorPointer", "PinnedImageAnchorCenter"),
                static settings => settings.PinnedImages.WheelZoomAnchor);
            var resetAnchor = Setting(Choice(zooming, "PinnedImageResetAnchor", (int)preferences.ResetZoomAnchor,
                    "PinnedImageAnchorTopLeft", "PinnedImageAnchorPointer", "PinnedImageAnchorCenter"),
                static settings => settings.PinnedImages.ResetZoomAnchor);
            var history = Group("PinnedImageHistory");
            var count = Setting(Number(history, "PinnedImageHistoryLimit", preferences.HistoryLimit, 0, int.MaxValue),
                static settings => settings.PinnedImages.HistoryLimit);
            var duplicate = Setting(Choice(history, "PinnedImageDuplicateBehavior", (int)preferences.DuplicateBehavior,
                "PinnedImageDuplicateFocus", "PinnedImageDuplicateIgnore", "PinnedImageDuplicateRestore",
                "PinnedImageDuplicateCreate"), static settings => settings.PinnedImages.DuplicateBehavior);
            var order = Setting(Choice(history, "PinnedImageRestoreOrder", (int)preferences.RestoreOrder,
                    "PinnedImageOrderClosed", "PinnedImageOrderCreated"),
                static settings => settings.PinnedImages.RestoreOrder);
            read = () => (preferences with
            {
                SupportedContentKinds = supportedKinds,
                ShowWindowShadow = shadow.IsChecked == true,
                TitlePosition = (PinnedTitlePosition)titlePosition.SelectedIndex,
                HideOthersOnShake = shake.IsChecked == true,
                KeepOthersHiddenOnNewPin = keepOthers.IsChecked == true,
                ActivateOnNewPin = attention0.IsChecked == true,
                FlashOnNewPin = attention1.IsChecked == true,
                FlashAfterGroupSwitch = attention2.IsChecked == true,
                TranslucentOnAttention = attention3.IsChecked == true,
                Topmost = topmost.IsChecked == true, AutoFit = fit.IsChecked == true,
                ShowTaskbarWhenNotTopmost = taskbar.IsChecked == true,
                MaximumWindowSize = committedMaximum,
                QuickThumbnailWidth = quickWidthEdited
                    ? decimal.ToInt32(ReadSettingsNumber(quickWidth))
                    : preferences.QuickThumbnailWidth,
                QuickThumbnailHeight = quickHeightEdited
                    ? decimal.ToInt32(ReadSettingsNumber(quickHeight))
                    : preferences.QuickThumbnailHeight,
                Opacity = (double)ReadSettingsNumber(opacity) / 100,
                SelectableText = selectable.IsChecked == true, ResizeFromBorder = border.IsChecked == true,
                ShowLockIndicator = lockHint.IsChecked == true,
                LockRestrictions = (lockMove.IsChecked == true ? PinnedLockRestrictions.Move : 0) |
                                   (lockPointerClose.IsChecked == true ? PinnedLockRestrictions.PointerClose : 0) |
                                   (lockKeyClose.IsChecked == true ? PinnedLockRestrictions.KeyboardClose : 0) |
                                   (lockDrop.IsChecked == true ? PinnedLockRestrictions.IncomingDrop : 0),
                ShowMenuShortcuts = shortcuts.IsChecked == true, CopyVisibleRegion = current.IsChecked == true,
                OutputContent = (PinnedImageOutputContent)Math.Max(0, outputContent.SelectedIndex),
                ReplacementAction = (PinnedReplacementAction)Math.Max(0, replacementAction.SelectedIndex),
                ThumbnailWheelZoom = thumbnailWheel.IsChecked == true,
                ThumbnailSelectableText = thumbnailText.IsChecked == true,
                ZoomStep = (double)ReadSettingsNumber(zoom) / 100,
                FineZoomStep = (double)ReadSettingsNumber(fine) / 100,
                OpacityStep = (double)ReadSettingsNumber(opacityStep) / 100,
                MouseThroughOpacity = (double)ReadSettingsNumber(through) / 100,
                WheelZoomAnchor = (PinnedZoomAnchor)Math.Max(0, wheelAnchor.SelectedIndex),
                ResetZoomAnchor = (PinnedZoomAnchor)Math.Max(0, resetAnchor.SelectedIndex),
                HistoryLimit = decimal.ToInt32(ReadSettingsNumber(count)),
                DuplicateBehavior = (PinnedDuplicateBehavior)Math.Max(0, duplicate.SelectedIndex),
                RestoreOrder = (PinnedRestoreOrder)Math.Max(0, order.SelectedIndex),
            }).Normalize();
            return page;

            void CommitMaximum()
            {
                if (!maximumWindowSize.TryGetInputValue(out var value)) return;
                committedMaximum = decimal.ToInt32(value);
                RequestLiveApply();
            }

            void RefreshSupportedTypes()
            {
                var labels = kindOptions.Where(option => (supportedKinds & option.Item1) != 0)
                    .Select(option => UiText.Get(option.Item2));
                supportedSummary.Text = string.Join("; ", labels);
                if (supportedSummary.Text.Length == 0) supportedSummary.Text = UiText.Get("CaptureContentsNone");
                ToolTip.SetTip(supported, supportedSummary.Text);
            }

            CheckBox LockedChoice(string key, PinnedLockRestrictions operation)
            {
                var checkbox = new CheckBox
                {
                    IsChecked = (preferences.LockRestrictions & operation) != 0, Margin = new(0, 0, 12, 0),
                };
                checkbox.Bind(ContentProperty, LiveText.Resource(key));
                lockedOptions.Children.Add(checkbox);
                return checkbox;
            }

            StackPanel Group(string key)
            {
                var content = new StackPanel { Spacing = 2 };
                var title = new TextBlock { FontSize = 15, FontWeight = FontWeight.SemiBold, Margin = new(0, 8, 0, 4) };
                title.Bind(TextBlock.TextProperty, LiveText.Resource(key));
                page.Children.Add(title);
                page.Children.Add(new Border
                {
                    Background = AppTheme.SurfaceTint, BorderBrush = AppTheme.Outline, BorderThickness = new(1),
                    CornerRadius = new(6), Padding = new(10, 6), Child = content,
                });
                return content;
            }

            CheckBox Check(StackPanel parent, string key, bool value)
            {
                var checkbox = new CheckBox { IsChecked = value, MinHeight = 28 };
                checkbox.Bind(ContentProperty, LiveText.Resource(key));
                parent.Children.Add(checkbox);
                return checkbox;
            }

            CompactNumberInput Number(StackPanel parent, string key, double value, int minimum, int maximum)
            {
                var number = new CompactNumberInput
                {
                    Name = key, Minimum = minimum, Maximum = maximum,
                    Value = Math.Clamp((int)Math.Round(value), minimum, maximum), Width = 104, Height = 28,
                };
                Row(parent, key, number);
                return number;
            }

            ComboBox Choice(StackPanel parent, string key, int value, params string[] labels)
            {
                var items = labels.Select(label =>
                {
                    var item = new ComboBoxItem();
                    item.Bind(ContentProperty, LiveText.Resource(label));
                    return item;
                }).ToArray();
                var combo = new ComboBox { ItemsSource = items, SelectedIndex = value, Width = 190, MinHeight = 28 };
                Row(parent, key, combo);
                return combo;
            }

            void Row(StackPanel parent, string key, Control control)
            {
                var label = new TextBlock
                    { VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
                label.Bind(TextBlock.TextProperty, LiveText.Resource(key));
                var row = new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 12, MinHeight = 32 };
                row.Children.Add(label);
                control.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(control, 1);
                row.Children.Add(control);
                parent.Children.Add(row);
            }
        }
    }
}
