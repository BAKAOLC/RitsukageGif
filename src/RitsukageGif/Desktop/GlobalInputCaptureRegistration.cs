using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace RitsukageGif.Desktop
{
    internal sealed class GlobalInputCaptureRegistration
    {
        private readonly Control _control;
        private bool _synchronizeQueued;
        private Window? _window;

        internal GlobalInputCaptureRegistration(Control control)
        {
            _control = control;
            control.GotFocus += Focused;
            control.LostFocus += Unfocused;
            control.AttachedToVisualTree += Attached;
            control.DetachedFromVisualTree += Detached;
            AttachWindow();
            Synchronize();
        }

        private void Focused(object? sender, GotFocusEventArgs args)
        {
            Synchronize();
        }

        private void Unfocused(object? sender, RoutedEventArgs args)
        {
            if (_synchronizeQueued) return;
            _synchronizeQueued = true;
            Dispatcher.UIThread.Post(() =>
            {
                _synchronizeQueued = false;
                Synchronize();
            });
        }

        private void Attached(object? sender, VisualTreeAttachmentEventArgs args)
        {
            AttachWindow();
            Synchronize();
        }

        private void Detached(object? sender, VisualTreeAttachmentEventArgs args)
        {
            DetachWindow();
            GlobalInputCapture.SetActive(_control, false);
        }

        private void AttachWindow()
        {
            DetachWindow();
            _window = TopLevel.GetTopLevel(_control) as Window;
            if (_window is null) return;
            _window.PropertyChanged += WindowChanged;
            _window.Closed += Closed;
        }

        private void DetachWindow()
        {
            if (_window is null) return;
            _window.PropertyChanged -= WindowChanged;
            _window.Closed -= Closed;
            _window = null;
        }

        private void WindowChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
        {
            if (args.Property == WindowBase.IsActiveProperty) Synchronize();
        }

        private void Closed(object? sender, EventArgs args)
        {
            DetachWindow();
            GlobalInputCapture.SetActive(_control, false);
        }

        private void Synchronize()
        {
            GlobalInputCapture.SetActive(_control,
                _control.IsKeyboardFocusWithin && _control.GetVisualRoot() is not null &&
                (_window is null || _window.IsActive));
        }
    }
}
