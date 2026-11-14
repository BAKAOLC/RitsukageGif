using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace RitsukageGif.Desktop
{
    internal sealed class SimulatedWheelInput
    {
        private readonly Control _root;
        private IPointer? _pointer;
        private Point _position;
        private InputElement? _source;
        private ulong _timestamp;

        internal SimulatedWheelInput(Control root)
        {
            _root = root;
            root.AddHandler(InputElement.PointerMovedEvent, RememberPointer, RoutingStrategies.Tunnel, true);
            root.AddHandler(InputElement.PointerEnteredEvent, RememberPointer, RoutingStrategies.Tunnel, true);
        }

        internal Point? PointerPosition =>
            _source is { } source && IsPointerCurrent(source) ? _position : null;

        internal bool Raise(int direction)
        {
            if (_source is not { } source || _pointer is not { } pointer || !IsPointerCurrent(source)) return false;
#pragma warning disable CS0618
            var wheel = new PointerWheelEventArgs(source, pointer, _root, _position, _timestamp,
                new(), KeyModifiers.None, new(0, direction));
#pragma warning restore CS0618
            source.RaiseEvent(wheel);
            return wheel.Handled;
        }

        private bool IsPointerCurrent(InputElement source)
        {
            return _root.IsPointerOver && source.GetPresentationSource() == _root.GetPresentationSource();
        }

        private void RememberPointer(object? sender, PointerEventArgs args)
        {
            if (args.Source is not InputElement source) return;
            _source = source;
            _pointer = args.Pointer;
            _position = args.GetPosition(_root);
            _timestamp = args.Timestamp;
        }
    }
}
