using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace RitsukageGif.Desktop
{
    // ReSharper disable once ClassCannotBeInstantiated
    internal sealed partial class ScreenshotScrollingWindow
    {
        private readonly Button _cropLeading = new();
        private readonly Popup _cropPopup = new() { IsLightDismissEnabled = false };
        private readonly Button _cropTrailing = new();
        private readonly Button _move = new();
        private readonly List<Button> _outputButtons = [];
        private bool _cropButtonHovered;
        private IDisposable? _cropCloseTimer;
        private bool _cropPanelHovered;
        private bool _previewExcluded;

        private Border BuildControls(WindowTarget? target, bool respondShortcuts)
        {
            var controls = new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 2, Margin = new(4),
            };
            var grip = new ToolbarDragHandle(this, () => _manualToolbarPosition = true, Reposition)
            {
                [!IsEnabledProperty] = _move[!IsEnabledProperty],
            };
            grip.Bind(AutomationProperties.NameProperty, LiveText.Resource("ToolbarMove"));
            grip.Bind(ToolTip.TipProperty, LiveText.Resource("ToolbarMove"));
            controls.Children.Add(grip);
            PositionChanged += (_, _) =>
            {
                if (_manualToolbarPosition && IsVisible) RefreshLivePreview();
            };
            ScalingChanged += (_, _) =>
            {
                if (IsVisible) Reposition();
            };
            _screens.Changed += ToolbarScreensChanged;
            Closed += (_, _) => _screens.Changed -= ToolbarScreensChanged;
            _size.FontSize = 12;
            _size.VerticalAlignment = VerticalAlignment.Center;
            _size.Margin = new(4, 0);
            _size.Text = $"{_region.Width} × {_region.Height}";
            ToolTip.SetTip(_size, _status);
            controls.Children.Add(_size);
            SetCompactContent(_move, "ScrollCaptureMove", "ScreenshotScrollVertical", true);
            _move.AddHandler(PointerPressedEvent, (_, args) =>
            {
                if (_outputting || !args.GetCurrentPoint(_move).Properties.IsLeftButtonPressed) return;
                _session.AutomaticEnabled = false;
                _dragStart = _move.PointToScreen(args.GetPosition(_move));
                _dragPosition = _axis == ScrollCaptureAxis.Vertical ? _region.Y : _region.X;
                args.Pointer.Capture(_move);
                args.Handled = true;
            }, RoutingStrategies.Tunnel);
            _move.AddHandler(PointerMovedEvent, (_, args) =>
            {
                if (_dragStart is not { } start) return;
                var point = _move.PointToScreen(args.GetPosition(_move));
                MoveRegion(
                    _dragPosition + (_axis == ScrollCaptureAxis.Vertical ? point.Y - start.Y : point.X - start.X));
                args.Handled = true;
            }, RoutingStrategies.Tunnel);
            _move.AddHandler(PointerReleasedEvent, (_, args) =>
            {
                if (_dragStart is null) return;
                _dragStart = null;
                args.Handled = true;
                args.Pointer.Capture(null);
                Reposition();
            }, RoutingStrategies.Tunnel);
            _move.PointerCaptureLost += (_, _) => _dragStart = null;
            controls.Children.Add(_move);
            controls.Children.Add(Separator());
            _direction.Width = 112;
            _direction.MinHeight = _direction.Height = 32;
            _direction.Padding = new(8, 0);
            _direction.Foreground = AppTheme.Ink;
            _direction.Focusable = false;
            RefreshDirection();
            UiText.LanguageChanged += RefreshDirection;
            controls.Children.Add(_direction);
            InitializeAutomatic(controls, target, respondShortcuts);
            var crop = new Button();
            SetCompactContent(crop, "ScrollCaptureCrop", "ScrollCaptureCrop", true);
            _cropPopup.PlacementTarget = crop;
            _cropPopup.Placement = PlacementMode.Bottom;
            _cropPopup.Child = BuildCropControls(respondShortcuts);
            crop.PointerEntered += (_, _) =>
            {
                _cropButtonHovered = true;
                RefreshCropVisibility();
            };
            crop.PointerExited += (_, _) =>
            {
                _cropButtonHovered = false;
                RefreshCropVisibility();
            };
            _cropPopup.Child.PointerEntered += (_, _) =>
            {
                _cropPanelHovered = true;
                RefreshCropVisibility();
            };
            _cropPopup.Child.PointerExited += (_, _) =>
            {
                _cropPanelHovered = false;
                RefreshCropVisibility();
            };
            crop.Click += (_, _) => _cropPopup.IsOpen = !_cropPopup.IsOpen;
            _cropPopup.Opened += (_, _) =>
            {
                _previewExcluded = _cropPopup.Child is { } child && GetTopLevel(child) is { } root && Exclude(root);
                UpdateObstruction();
            };
            _cropPopup.Closed += (_, _) =>
            {
                _previewExcluded = false;
                _cropPanelHovered = false;
                UpdateObstruction();
            };
            controls.Children.Add(crop);
            SetPauseContent("ScrollCapturePause", true);
            _pause.Click += (_, _) =>
            {
                if (_outputting) return;
                _session.Paused = !_session.Paused;
                if (!_session.Paused) _statusNoticeKey = null;
                SetPauseContent(_session.Paused ? "ScrollCaptureResume" : "ScrollCapturePause", !_session.Paused);
                RefreshStatus();
                RefreshLivePreview();
            };
            controls.Children.Add(_pause);
            controls.Children.Add(Separator());
            var edit = OutputButton(new(), "ScrollCaptureEdit", "ScrollCaptureEdit", ScrollingCaptureAction.Edit);
            if (respondShortcuts) AddShortcutHint(edit, "edit");
            controls.Children.Add(edit);
            controls.Children.Add(OutputButton(new(), "ScrollCaptureOcrCopy", "RecognizeText",
                ScrollingCaptureAction.OcrCopy));
            controls.Children.Add(OutputButton(new(), "ScreenshotPin", "ScreenshotPin", ScrollingCaptureAction.Pin));
            controls.Children.Add(OutputButton(new(), "ScreenshotSave", "ScreenshotSave", ScrollingCaptureAction.Save));
            controls.Children.Add(OutputButton(new(), "ScrollCaptureQuickSave", "ScrollCaptureQuickSave",
                ScrollingCaptureAction.QuickSave));
            var close = new Button();
            SetCompactContent(close, "ScrollCaptureClose", "Cancel", true);
            close.Click += (_, _) => Close();
            controls.Children.Add(close);
            controls.Children.Add(
                OutputButton(_finish, "ScreenshotCopy", "ScreenshotCopy", ScrollingCaptureAction.Copy));
            if (respondShortcuts)
            {
                AddShortcutHint(_pause, "pause");
                AddShortcutHint(_finish, "finish");
            }

            AddShortcutHint(close, "cancel");
            _status.FontSize = 12;
            _status.MaxWidth = 320;
            var scroller = new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                MaxWidth = MaxWidth - 2, Content = controls,
            };
            var host = new Grid();
            host.Children.Add(scroller);
            host.Children.Add(_cropPopup);
            UpdateDirectionControls();
            SetOutputActionsEnabled(false);
            return new()
                { BorderBrush = AppTheme.Outline, BorderThickness = new(1), CornerRadius = new(6), Child = host };
        }

        private Button OutputButton(Button button, string key, string icon, ScrollingCaptureAction action)
        {
            SetCompactContent(button, key, icon, true);
            button.Click += async (_, _) => await CompleteCapture(action);
            _outputButtons.Add(button);
            return button;
        }

        private static Border Separator()
        {
            return new() { Width = 1, Height = 20, Margin = new(4, 0), Background = AppTheme.Outline };
        }

        private void SetOutputActionsEnabled(bool enabled)
        {
            enabled &= !_outputting;
            foreach (var button in _outputButtons) button.IsEnabled = enabled;
            _move.IsEnabled = _direction.IsEnabled = _pause.IsEnabled = !_outputting;
            _automatic.IsEnabled = !_outputting && _session.AutomaticAvailable;
            _cropLeading.IsEnabled = enabled && _session.CanCropLeading;
            _cropTrailing.IsEnabled = enabled && _session.CanCropTrailing;
        }

        private Border BuildCropControls(bool respondShortcuts)
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
            SetCompactContent(_cropLeading, "ScrollCaptureCropTop", "ScrollCaptureCropEdge", true);
            SetCompactContent(_cropTrailing, "ScrollCaptureCropBottom", "ScrollCaptureCropEdge", true);
            _cropLeading.Click += (_, _) => Crop(true);
            _cropTrailing.Click += (_, _) => Crop(false);
            if (respondShortcuts)
            {
                AddShortcutHint(_cropLeading, "crop-leading");
                AddShortcutHint(_cropTrailing, "crop-trailing");
            }

            panel.Children.Add(_cropLeading);
            panel.Children.Add(_cropTrailing);
            return new()
            {
                Padding = new(4), CornerRadius = new(5), Background = AppTheme.Surface,
                BorderBrush = AppTheme.Outline, BorderThickness = new(1), Child = panel,
            };
        }

        private void Crop(bool leading)
        {
            if (_outputting) return;
            if (!(leading ? _session.CropLeading() : _session.CropTrailing())) return;
            SetPauseContent("ScrollCaptureResume", false);
            RefreshStatus();
            RefreshLivePreview();
        }

        private void RefreshCropVisibility()
        {
            _cropCloseTimer?.Dispose();
            _cropCloseTimer = null;
            if (_cropButtonHovered || _cropPanelHovered)
            {
                if (!_closed && !_outputting) _cropPopup.IsOpen = true;
            }
            else
            {
                _cropCloseTimer = DispatcherTimer.RunOnce(() =>
                {
                    _cropCloseTimer = null;
                    if (!_cropButtonHovered && !_cropPanelHovered) _cropPopup.IsOpen = false;
                }, TimeSpan.FromMilliseconds(200));
            }
        }

        private void ResetCapture(ScrollCaptureAxis axis, int leading, int trailing)
        {
            _axis = axis;
            _session.ConfigureAxis(axis, _region);
            _session.ConfigureInsets(leading, trailing);
            _leadingInset = leading;
            _trailingInset = trailing;
            SetOutputActionsEnabled(false);
            _preview.Image = null;
            _bitmap?.Dispose();
            _bitmap = null;
            _size.Text = $"{_region.Width} × {_region.Height}";
            UpdateDirectionControls();
            _session.Paused = false;
            SetPauseContent("ScrollCapturePause", true);
            _lastCaptureStatus = null;
            _captureFailure = null;
            _statusNoticeKey = null;
            SetOutputActionsEnabled(false);
            RefreshStatus();
            RefreshLivePreview();
        }

        private void UpdateDirectionControls()
        {
            var vertical = _axis == ScrollCaptureAxis.Vertical;
            _move.Cursor = new(vertical ? StandardCursorType.SizeNorthSouth : StandardCursorType.SizeWestEast);
            SetCompactContent(_move, "ScrollCaptureMove",
                vertical ? "ScreenshotScrollVertical" : "ScreenshotScrollHorizontal", true);
            UpdateCrop(_cropLeading, vertical ? "ScrollCaptureCropTop" : "ScrollCaptureCropLeft", vertical ? 180 : 90);
            UpdateCrop(_cropTrailing, vertical ? "ScrollCaptureCropBottom" : "ScrollCaptureCropRight",
                vertical ? 0 : 270);
        }

        private static void UpdateCrop(Button button, string key, double rotation)
        {
            button.Bind(AutomationProperties.NameProperty, LiveText.Resource(key));
            if (button.Content is Control icon)
            {
                icon.RenderTransformOrigin = RelativePoint.Center;
                icon.RenderTransform = new RotateTransform(rotation);
            }
        }

        private static void SetCompactContent(Button button, string key, string icon, bool iconOnly)
        {
            var geometry = ToolbarIcon.TryGeometry(icon) ?? ToolbarIcon.Geometry(icon);
            SetActionContent(button, key, geometry);
            button.MinHeight = button.Height = 32;
            button.Focusable = false;
            button.Padding = new(4);
            if (iconOnly)
            {
                button.Width = 32;
                var image = icon == "ScrollCaptureCropEdge"
                    ? ToolbarIcon.Create(button, geometry)
                    : ToolbarIcon.CreateInViewport(button, geometry);
                image.Width = image.Height = 20;
                button.Content = image;
            }
        }
    }
}
