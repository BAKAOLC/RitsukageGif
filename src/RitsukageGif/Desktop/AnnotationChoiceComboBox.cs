using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace RitsukageGif.Desktop
{
    internal sealed class AnnotationChoiceComboBox : ComboBox
    {
        private Popup? _choicePopup;
        private Control? _choicePopupChild;

        public AnnotationChoiceComboBox()
        {
            Focusable = false;
            IsTabStop = false;
            AddHandler(PointerWheelChangedEvent, ChoiceWheel, RoutingStrategies.Tunnel);
        }

        protected override Type StyleKeyOverride => typeof(ComboBox);

        protected override void OnApplyTemplate(TemplateAppliedEventArgs args)
        {
            if (_choicePopup is not null)
            {
                _choicePopup.Opened -= ChoicePopupOpened;
                _choicePopup.Closed -= ChoicePopupClosed;
            }

            DetachChoicePopup();
            base.OnApplyTemplate(args);
            _choicePopup = args.NameScope.Get<Popup>("PART_Popup");
            _choicePopup.Opened += ChoicePopupOpened;
            _choicePopup.Closed += ChoicePopupClosed;
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs args)
        {
            DetachChoicePopup();
            base.OnDetachedFromVisualTree(args);
        }

        private void ChoicePopupOpened(object? sender, EventArgs args)
        {
            DetachChoicePopup();
            _choicePopupChild = _choicePopup!.Child;
            _choicePopupChild?.AddHandler(PointerWheelChangedEvent, ChoiceWheel, RoutingStrategies.Tunnel);
        }

        private void ChoicePopupClosed(object? sender, EventArgs args)
        {
            DetachChoicePopup();
        }

        private void DetachChoicePopup()
        {
            _choicePopupChild?.RemoveHandler(PointerWheelChangedEvent, ChoiceWheel);
            _choicePopupChild = null;
        }

        private void ChoiceWheel(object? sender, PointerWheelEventArgs args)
        {
            if (args.Handled) return;
            args.Handled = true;
            if (!IsEffectivelyEnabled) return;
            if (AnnotationChoiceWheel.TryStep(args.Delta, SelectedIndex, ItemCount, out var next))
                SetCurrentValue(SelectedIndexProperty, next);
        }
    }
}
