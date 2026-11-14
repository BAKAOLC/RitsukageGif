using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace RitsukageGif.Desktop
{
    internal sealed partial class AnnotationEditableCombo
    {
        private Popup? _wheelPopup;
        private Control? _wheelPopupChild;

        protected override void OnApplyTemplate(TemplateAppliedEventArgs args)
        {
            if (_wheelPopup is not null)
            {
                _wheelPopup.Opened -= WheelPopupOpened;
                _wheelPopup.Closed -= WheelPopupClosed;
            }

            DetachWheelPopup();
            base.OnApplyTemplate(args);
            _wheelPopup = args.NameScope.Get<Popup>("PART_Popup");
            _wheelPopup.Opened += WheelPopupOpened;
            _wheelPopup.Closed += WheelPopupClosed;
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs args)
        {
            DetachWheelPopup();
            base.OnDetachedFromVisualTree(args);
        }

        private void WheelPopupOpened(object? sender, EventArgs args)
        {
            DetachWheelPopup();
            _wheelPopupChild = _wheelPopup!.Child;
            _wheelPopupChild?.AddHandler(PointerWheelChangedEvent, ChoiceWheel, RoutingStrategies.Tunnel);
        }

        private void WheelPopupClosed(object? sender, EventArgs args)
        {
            DetachWheelPopup();
        }

        private void DetachWheelPopup()
        {
            _wheelPopupChild?.RemoveHandler(PointerWheelChangedEvent, ChoiceWheel);
            _wheelPopupChild = null;
        }

        private void ChoiceWheel(object? sender, PointerWheelEventArgs args)
        {
            if (args.Handled) return;
            args.Handled = true;
            if (!IsEffectivelyEnabled) return;
            var index = Array.IndexOf(_items, Text);
            if (index >= 0 && AnnotationChoiceWheel.TryStep(args.Delta, index, _items.Length, out var next))
                SetCurrentValue(SelectedItemProperty, _items[next]);
        }
    }
}
