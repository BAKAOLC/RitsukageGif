using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Threading;

namespace RitsukageGif.Desktop
{
    internal sealed class HoverPropertyPanel : IDisposable
    {
        private static HoverPropertyPanel? _activePanel;

        private readonly Func<Control> _create;
        private readonly Action<Control>? _opening;
        private readonly Popup _popup;
        private readonly DispatcherTimer _closeTimer;
        private Border? _panel;
        private bool _targetHovered;
        private bool _panelHovered;
        private bool _disposed;

        public HoverPropertyPanel(Control target, Func<Control> create, Action<Control>? opening = null)
        {
            Target = target;
            _create = create;
            _opening = opening;
            _popup = new()
            {
                Placement = PlacementMode.Bottom, PlacementTarget = target,
                IsLightDismissEnabled = false,
            };
            ((ISetLogicalParent)_popup).SetParent(target);
            _closeTimer = new() { Interval = TimeSpan.FromMilliseconds(200) };
            _closeTimer.Tick += CloseTimerTick;
            target.PointerEntered += TargetPointerEntered;
            target.PointerExited += TargetPointerExited;
            target.DetachedFromVisualTree += TargetDetached;
            target.PropertyChanged += TargetPropertyChanged;
            _popup.Closed += PopupClosed;
        }

        internal Control Target { get; }

        internal bool IsEnabled
        {
            get;
            set
            {
                if (field == value) return;
                field = value;
                if (!value) Hide();
                else if (Target.IsPointerOver)
                    Dispatcher.UIThread.Post(() =>
                    {
                        if (Target.IsPointerOver) ShowPanel();
                    });
            }
        } = true;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Hide();
            Target.PointerEntered -= TargetPointerEntered;
            Target.PointerExited -= TargetPointerExited;
            Target.DetachedFromVisualTree -= TargetDetached;
            Target.PropertyChanged -= TargetPropertyChanged;
            _popup.Closed -= PopupClosed;
            _closeTimer.Tick -= CloseTimerTick;
            if (_panel is not null)
            {
                _panel.PointerEntered -= PanelPointerEntered;
                _panel.PointerExited -= PanelPointerExited;
                _panel.Child = null;
            }

            _popup.Child = null;
            ((ISetLogicalParent)_popup).SetParent(null);
        }

        public void Hide()
        {
            if (ReferenceEquals(_activePanel, this)) _activePanel = null;
            _closeTimer.Stop();
            _targetHovered = false;
            _panelHovered = false;
            _popup.IsOpen = false;
        }

        private void TargetPointerEntered(object? sender, PointerEventArgs args)
        {
            ShowPanel();
        }

        private void ShowPanel()
        {
            if (_disposed || !IsEnabled || !Target.IsEffectivelyEnabled) return;
            _targetHovered = true;
            _closeTimer.Stop();
            if (_panel is null)
            {
                _panel = new()
                {
                    Child = _create(), Padding = new(4), Background = AppTheme.Surface,
                    BorderBrush = AppTheme.Outline, BorderThickness = new(1), CornerRadius = new(5),
                    [TextElement.ForegroundProperty] = AppTheme.Ink,
                };
                _panel.PointerEntered += PanelPointerEntered;
                _panel.PointerExited += PanelPointerExited;
                _popup.Child = _panel;
            }

            ActivatePanel();
            if (!_popup.IsOpen) _opening?.Invoke(_panel.Child!);
            _popup.IsOpen = true;
        }

        private void ActivatePanel()
        {
            if (ReferenceEquals(_activePanel, this)) return;
            var previous = _activePanel;
            _activePanel = this;
            if (previous is null) return;
            previous._closeTimer.Stop();
            previous._closeTimer.Interval = TimeSpan.FromMilliseconds(50);
            previous._closeTimer.Start();
        }

        private void TargetPointerExited(object? sender, PointerEventArgs args)
        {
            _targetHovered = false;
            ScheduleClose();
        }

        private void PanelPointerEntered(object? sender, PointerEventArgs args)
        {
            _panelHovered = true;
            _closeTimer.Stop();
        }

        private void PanelPointerExited(object? sender, PointerEventArgs args)
        {
            _panelHovered = false;
            ScheduleClose();
        }

        private void ScheduleClose()
        {
            _closeTimer.Stop();
            _closeTimer.Interval = TimeSpan.FromMilliseconds(200);
            if (!_disposed && !_targetHovered && !_panelHovered) _closeTimer.Start();
        }

        private void CloseTimerTick(object? sender, EventArgs args)
        {
            _closeTimer.Stop();
            if (!_targetHovered && !_panelHovered) _popup.IsOpen = false;
            else if (!_disposed && IsEnabled && Target.IsEffectivelyEnabled) ActivatePanel();
        }

        private void PopupClosed(object? sender, EventArgs args)
        {
            if (ReferenceEquals(_activePanel, this)) _activePanel = null;
            _closeTimer.Stop();
            _panelHovered = false;
        }

        private void TargetDetached(object? sender, VisualTreeAttachmentEventArgs args)
        {
            Hide();
        }

        private void TargetPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
        {
            if ((args.Property == InputElement.IsEffectivelyEnabledProperty && !Target.IsEffectivelyEnabled) ||
                (args.Property == Visual.IsVisibleProperty && !Target.IsVisible)) Hide();
        }
    }
}
