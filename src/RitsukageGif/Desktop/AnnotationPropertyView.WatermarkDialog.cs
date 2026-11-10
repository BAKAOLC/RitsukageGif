using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    internal sealed partial class AnnotationPropertyView
    {
        private Window? _watermarkDialog;

        private async Task ShowWatermarkEditorAsync(Control anchor)
        {
            if (_disposed || _watermarkDialog is not null || TopLevel.GetTopLevel(anchor) is not Window owner ||
                !_session.BeginTextPropertyEdit()) return;
            var generation = _session.TextEditGeneration;
            var text = new TextBox
            {
                Text = _session.TextEditValue, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
                MinHeight = 72,
            };
            AnnotationTextInput.ProtectComposition(text);
            var apply = new Button { Content = UiText.Get("GlobalActionApply"), IsDefault = true, MinWidth = 64 };
            var cancel = new Button { Content = UiText.Get("Cancel"), IsCancel = true, MinWidth = 64 };
            var footer = new StackPanel
            {
                Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
                Spacing = 6, Children = { apply, cancel },
            };
            var layout = new Border { Padding = new(8), Child = WatermarkTextEditorContent(text, footer) };
            var dialog = new Window
            {
                Title = UiText.Get("AnnotationWatermarkInput"), Width = 350, Height = 150,
                MinWidth = 350, MinHeight = 150, Content = layout, ShowInTaskbar = false,
                WindowStartupLocation = WindowStartupLocation.CenterOwner, Topmost = owner.Topmost,
                Background = AppTheme.Surface, Foreground = AppTheme.Ink,
            };
            _watermarkDialog = dialog;
            apply.Click += (_, _) => dialog.Close(true);
            cancel.Click += (_, _) => dialog.Close(false);
            dialog.Opened += (_, _) =>
            {
                text.CaretIndex = text.Text?.Length ?? 0;
                text.Focus();
            };
            using var scope = _dialogScopeProvider?.Invoke();
            try
            {
                var accepted = await dialog.ShowDialog<bool>(owner);
                if (!_session.IsEditingText || _session.TextEditGeneration != generation) return;
                if (_disposed)
                {
                    _session.CancelTextEdit();
                    return;
                }

                if (accepted)
                {
                    _session.UpdateTextEditDraft(text.Text ?? "");
                    _session.CommitTextEdit();
                }
                else
                {
                    _session.CancelTextEdit();
                }
            }
            finally
            {
                _watermarkDialog = null;
            }
        }
    }
}
