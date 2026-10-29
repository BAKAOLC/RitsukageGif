using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    internal sealed partial class SettingsWindow
    {
        private static StackPanel PinsPage(PinnedImagePreferences preferences,
            out Func<PinnedImagePreferences> read)
        {
            preferences = preferences.Normalize();
            var page = Page();
            var display = Group("PinnedImageDisplay");
            var shadow = Check(display, "PinnedImageWindowShadow", preferences.ShowWindowShadow);
            var topmost = Check(display, "PinnedImageDefaultTopmost", preferences.Topmost);
            var fit = Check(display, "PinnedImageAutoFit", preferences.AutoFit);
            var taskbar = Check(display, "PinnedImageTaskbarWhenNotTopmost", preferences.ShowTaskbarWhenNotTopmost);
            var opacity = Number(display, "PinnedImageDefaultOpacity", preferences.Opacity * 100, 15, 100);
            var behavior = Group("PinnedImageBehavior");
            var selectable = Check(behavior, "PinnedImageSelectableText", preferences.SelectableText);
            var border = Check(behavior, "PinnedImageBorderResize", preferences.ResizeFromBorder);
            var lockHint = Check(behavior, "PinnedImageLockHint", preferences.ShowLockIndicator);
            var lockedOptions = new WrapPanel { Orientation = Orientation.Horizontal, MaxWidth = 380 };
            var lockMove = LockedChoice("PinnedImageLockMove", PinnedLockedOperations.Move);
            var lockResize = LockedChoice("PinnedImageLockResize", PinnedLockedOperations.Resize);
            var lockClose = LockedChoice("PinnedImageLockClose", PinnedLockedOperations.Close);
            var lockKeys = LockedChoice("PinnedImageLockShortcuts", PinnedLockedOperations.Shortcuts);
            var lockAnnotation = LockedChoice("PinnedImageLockAnnotation", PinnedLockedOperations.Annotation);
            Row(behavior, "PinnedImageLockedOperations", lockedOptions);
            var shortcuts = Check(behavior, "PinnedImageMenuShortcuts", preferences.ShowMenuShortcuts);
            var current = Check(behavior, "PinnedImageCopyVisibleRegion", preferences.CopyVisibleRegion);
            var thumbnailWheel = Check(behavior, "PinnedImageThumbnailWheelZoom", preferences.ThumbnailWheelZoom);
            var thumbnailText = Check(behavior, "PinnedImageThumbnailSelectableText",
                preferences.ThumbnailSelectableText);
            var zooming = Group("PinnedImageZoom");
            var zoom = Number(zooming, "PinnedImageZoomStep", preferences.ZoomStep * 100, 1, 100);
            var fine = new CompactNumberInput
            {
                Minimum = 0.1m, Maximum = 10, Increment = 0.1m, DecimalPlaces = 1,
                Value = (decimal)(preferences.FineZoomStep * 100), Width = 104, Height = 28,
            };
            Row(zooming, "PinnedImageFineZoomStep", fine);
            var opacityStep = Number(zooming, "PinnedImageOpacityStep", preferences.OpacityStep * 100, 1, 50);
            var through = Number(zooming, "PinnedImageThroughOpacity", preferences.MouseThroughOpacity * 100, 15, 100);
            var wheelAnchor = Choice(zooming, "PinnedImageWheelAnchor", (int)preferences.WheelZoomAnchor,
                "PinnedImageAnchorTopLeft", "PinnedImageAnchorPointer", "PinnedImageAnchorCenter");
            var resetAnchor = Choice(zooming, "PinnedImageResetAnchor", (int)preferences.ResetZoomAnchor,
                "PinnedImageAnchorTopLeft", "PinnedImageAnchorPointer", "PinnedImageAnchorCenter");
            var history = Group("PinnedImageHistory");
            var count = Number(history, "PinnedImageHistoryLimit", preferences.HistoryLimit, 0, int.MaxValue);
            var duplicate = Choice(history, "PinnedImageDuplicateBehavior", (int)preferences.DuplicateBehavior,
                "PinnedImageDuplicateFocus", "PinnedImageDuplicateIgnore", "PinnedImageDuplicateRestore",
                "PinnedImageDuplicateCreate");
            var order = Choice(history, "PinnedImageRestoreOrder", (int)preferences.RestoreOrder,
                "PinnedImageOrderClosed", "PinnedImageOrderCreated");
            read = () => (preferences with
            {
                ShowWindowShadow = shadow.IsChecked == true,
                Topmost = topmost.IsChecked == true, AutoFit = fit.IsChecked == true,
                ShowTaskbarWhenNotTopmost = taskbar.IsChecked == true,
                Opacity = (double)ReadSettingsNumber(opacity) / 100,
                SelectableText = selectable.IsChecked == true, ResizeFromBorder = border.IsChecked == true,
                ShowLockIndicator = lockHint.IsChecked == true,
                LockedOperations = (lockMove.IsChecked == true ? PinnedLockedOperations.Move : 0) |
                                   (lockResize.IsChecked == true ? PinnedLockedOperations.Resize : 0) |
                                   (lockClose.IsChecked == true ? PinnedLockedOperations.Close : 0) |
                                   (lockKeys.IsChecked == true ? PinnedLockedOperations.Shortcuts : 0) |
                                   (lockAnnotation.IsChecked == true ? PinnedLockedOperations.Annotation : 0),
                ShowMenuShortcuts = shortcuts.IsChecked == true, CopyVisibleRegion = current.IsChecked == true,
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

            CheckBox LockedChoice(string key, PinnedLockedOperations operation)
            {
                var checkbox = new CheckBox
                {
                    IsChecked = (preferences.LockedOperations & operation) != 0, Margin = new(0, 0, 12, 0),
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
                    CornerRadius = new(6), Padding = new(12, 8), Child = content,
                });
                return content;
            }

            CheckBox Check(StackPanel parent, string key, bool value)
            {
                var checkbox = new CheckBox { IsChecked = value, MinHeight = 32 };
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
                var row = new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 16, MinHeight = 36 };
                row.Children.Add(label);
                control.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(control, 1);
                row.Children.Add(control);
                parent.Children.Add(row);
            }
        }
    }
}
