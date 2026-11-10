using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    internal sealed partial class ScreenshotRecordingWindow
    {
        private IReadOnlyList<RecordingCameraDevice> _cameraDevices = [];

        private async Task LoadCameraDevicesAsync()
        {
            if (_closed) return;
            var token = _controller.Lifetime;
            try
            {
                var result = await RecordingCameraCatalog.ListAsync(token);
                if (_closed || token.IsCancellationRequested) return;
                _cameraDevices = result.Devices;
                _camera.IsVisible = _cameraDevices.Count > 0;
                await _controller.InitializeCameraDevicesAsync(_cameraDevices);
                if (result.Failure is { } failure)
                    LogHub.GetLogger("Desktop.ScreenshotRecording")
                        .ZLogWarning($"Camera enumeration failed. Detail={failure}");
                Refresh();
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
            }
        }

        private Control BuildCameraSettings()
        {
            var rows = new StackPanel { Spacing = 5 };
            var edit = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5 };
            var enabled = new CheckBox
                { Content = UiText.Get("RecordingCameraEnable"), IsChecked = _controller.Camera.Enabled };
            rows.Children.Add(enabled);
            var refreshing = false;
            var editingSlot = 0;
            var selectedDevice = new ComboBox { Width = 120, MinHeight = 28 };
            var mirror = ToolButton("RecordingCameraMirror", "AnimationFlipHorizontal", () => Task.CompletedTask);
            var rectangle = new RadioButton
                { Content = UiText.Get("AnimationRectangle"), GroupName = "RecordingCameraShape" };
            var circle = new RadioButton
                { Content = UiText.Get("RecordingCameraCircle"), GroupName = "RecordingCameraShape" };
            var size = new Slider { Minimum = 120, Maximum = 480, TickFrequency = 1, IsSnapToTickEnabled = true };
            var width = new Slider { Minimum = 0, Maximum = 10, TickFrequency = 1, IsSnapToTickEnabled = true };
            var color = new AppearanceColorPicker
            {
                Width = 74, Height = 28, MinHeight = 0, MinWidth = 0, Padding = new(4),
                IsAlphaEnabled = false, IsAlphaVisible = false, IsColorPaletteVisible = false,
                IsHexInputVisible = true, Foreground = AppTheme.Ink,
            };
            color.Styles.Add(new AnnotationColorPickerStyles());
            var swatch = new Border
            {
                Width = 40, Height = 16, Margin = new(2),
                BorderBrush = AppTheme.Muted, BorderThickness = new(1),
            };
            color.Content = swatch;
            var summary = new TextBlock { TextTrimming = TextTrimming.CharacterEllipsis };
            var chooser = new Button { HorizontalContentAlignment = HorizontalAlignment.Stretch, MinHeight = 28 };
            var chooserContent = new Grid { ColumnDefinitions = new("*,16"), ColumnSpacing = 8 };
            chooserContent.Children.Add(summary);
            var chevron = DropDownChevron.Create(chooser);
            Grid.SetColumn(chevron, 1);
            chooserContent.Children.Add(chevron);
            chooser.Content = chooserContent;
            var deviceLabel = new TextBlock { Text = UiText.Get("RecordingCameraDevices") };
            var choices = new StackPanel { Spacing = 3, Margin = new(6) };
            foreach (var device in _cameraDevices)
            {
                var check = new CheckBox
                {
                    Content = device.Name,
                    IsChecked = _controller.Camera.Primary.Device?.Id == device.Id ||
                                _controller.Camera.Secondary.Device?.Id == device.Id,
                };
                check.IsCheckedChanged += (_, _) =>
                {
                    // ReSharper disable once AccessToModifiedClosure
                    if (refreshing) return;
                    var options = _controller.Camera;
                    if (check.IsChecked == true)
                    {
                        if (options.Primary.Device is null)
                        {
                            options = options with { Primary = options.Primary with { Device = device } };
                        }
                        else if (options.Secondary.Device is null)
                        {
                            options = options with { Secondary = options.Secondary with { Device = device } };
                        }
                        else
                        {
                            refreshing = true;
                            check.IsChecked = false;
                            refreshing = false;
                            return;
                        }
                    }
                    else if (options.Primary.Device?.Id == device.Id)
                    {
                        options = options with { Primary = options.Secondary, Secondary = new() };
                    }
                    else if (options.Secondary.Device?.Id == device.Id)
                    {
                        options = options with { Secondary = new() };
                    }

                    if (options.Primary.Device is null) options = options with { Enabled = false };
                    _ = GuardAsync(() => _controller.ConfigureCameraAsync(options, true));
                    RefreshFields();
                };
                choices.Children.Add(check);
            }

            chooser.Flyout = new Flyout { Content = choices };
            rows.Children.Add(deviceLabel);
            rows.Children.Add(chooser);
            edit.Children.Add(selectedDevice);
            edit.Children.Add(ToolButton("RecordingCameraRotate", "AnimationRotate", () =>
            {
                Change(slot => slot with { Rotation = (slot.Rotation + 90) % 360 });
                return Task.CompletedTask;
            }));
            edit.Children.Add(mirror);
            rows.Children.Add(edit);
            rows.Children.Add(new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 12, Children = { rectangle, circle },
            });
            rows.Children.Add(new TextBlock { Text = UiText.Get("RecordingCameraSize") });
            rows.Children.Add(size);
            rows.Children.Add(new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 4,
                Children =
                {
                    new TextBlock
                        { Text = UiText.Get("RecordingCameraBorder"), VerticalAlignment = VerticalAlignment.Center },
                    color,
                },
            });
            rows.Children.Add(width);
            enabled.IsCheckedChanged += (_, _) =>
            {
                if (refreshing) return;
                _ = GuardAsync(() => _controller.ConfigureCameraAsync(_controller.Camera with
                {
                    Enabled = enabled.IsChecked == true,
                }));
            };
            selectedDevice.SelectionChanged += (_, _) =>
            {
                if (refreshing) return;
                editingSlot = _controller.Camera.Primary.Device is null ? 1 : Math.Max(0, selectedDevice.SelectedIndex);
                RefreshFields();
            };
            mirror.Click += (_, _) => Change(slot => slot with { Mirrored = !slot.Mirrored });
            rectangle.IsCheckedChanged += (_, _) =>
            {
                if (rectangle.IsChecked == true) Change(slot => slot with { Shape = RecordingCameraShape.Rectangle });
            };
            circle.IsCheckedChanged += (_, _) =>
            {
                if (circle.IsChecked == true) Change(slot => slot with { Shape = RecordingCameraShape.Circle });
            };
            size.ValueChanged += (_, _) => Change(slot => slot with { Size = (int)Math.Round(size.Value) });
            width.ValueChanged += (_, _) => Change(slot => slot with { BorderWidth = (int)Math.Round(width.Value) });
            color.ColorChanged += (_, _) => Change(slot => slot with
            {
                BorderColor = new(color.Color.R, color.Color.G, color.Color.B, 255),
            });
            rows.AttachedToVisualTree += (_, _) => CameraOptionsChanged += RefreshFields;
            rows.DetachedFromVisualTree += (_, _) => CameraOptionsChanged -= RefreshFields;
            RefreshFields();
            return rows;

            void Change(Func<RecordingCameraSlot, RecordingCameraSlot> change)
            {
                // ReSharper disable once AccessToModifiedClosure
                if (refreshing) return;
                var options = _controller.Camera;
                var next = editingSlot == 0
                    ? options with { Primary = change(options.Primary) }
                    : options with { Secondary = change(options.Secondary) };
                _ = GuardAsync(() => _controller.ConfigureCameraAsync(next));
                RefreshFields();
            }

            void RefreshFields()
            {
                refreshing = true;
                var options = _controller.Camera;
                var devices = new[] { options.Primary.Device, options.Secondary.Device }
                    .OfType<RecordingCameraDevice>().ToArray();
                selectedDevice.ItemsSource = devices.Select(device => device.Name).ToArray();
                if (options.Primary.Device is null && options.Secondary.Device is not null) editingSlot = 1;
                else if (editingSlot == 1 && options.Secondary.Device is null) editingSlot = 0;
                selectedDevice.SelectedIndex = devices.Length == 0 ? -1 :
                    options.Primary.Device is null ? 0 : editingSlot;
                summary.Text = devices.Length == 0
                    ? UiText.Get("None")
                    : string.Join("; ", devices.Select(device => device.Name));
                enabled.IsChecked = options.Enabled;
                enabled.IsEnabled = _cameraDevices.Count > 0;
                deviceLabel.IsVisible = chooser.IsVisible = _cameraDevices.Count > 1;
                chooser.IsEnabled = options.Enabled;
                edit.IsEnabled = options.Enabled && devices.Length > 0;
                selectedDevice.IsVisible = devices.Length > 1;
                rectangle.IsEnabled = circle.IsEnabled = size.IsEnabled = color.IsEnabled = width.IsEnabled =
                    options.Enabled && devices.Length > 0;
                var slot = editingSlot == 0 ? options.Primary : options.Secondary;
                mirror.Classes.Set("active-tool", slot.Mirrored);
                mirror.Background = slot.Mirrored ? AppTheme.Accent : Brushes.Transparent;
                mirror.Foreground = slot.Mirrored ? Brushes.White : AppTheme.Ink;
                rectangle.IsChecked = slot.Shape == RecordingCameraShape.Rectangle;
                circle.IsChecked = slot.Shape == RecordingCameraShape.Circle;
                var limit = _cameraPreview?.GetSizeLimit(editingSlot) ?? 480;
                size.Minimum = Math.Min(Math.Min(120, slot.Size), limit);
                size.Maximum = limit;
                size.Value = slot.Size;
                width.Value = slot.BorderWidth;
                color.Color = Color.FromArgb(255, slot.BorderColor.Red, slot.BorderColor.Green, slot.BorderColor.Blue);
                swatch.Background = new SolidColorBrush(color.Color);
                refreshing = false;
            }
        }
    }
}
