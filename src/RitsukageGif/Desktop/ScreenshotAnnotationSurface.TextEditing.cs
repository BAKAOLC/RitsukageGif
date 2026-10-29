using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace RitsukageGif.Desktop
{
    internal sealed partial class ScreenshotAnnotationSurface
    {
        private TextBox? _inlineTextEditor;

        private bool IsTextEditorSource(object? source)
        {
            return source is Control control && _inlineTextEditor is { } editor &&
                   (ReferenceEquals(editor, control) || editor.IsVisualAncestorOf(control));
        }

        private void RefreshTextEditor()
        {
            if (!_session.IsCanvasTextEditing || !new Rect(_monitor.X, _monitor.Y, _monitor.Width, _monitor.Height)
                    .Contains(_session.TextEditPosition))
            {
                if (_inlineTextEditor is { } previous) Children.Remove(previous);
                _inlineTextEditor = null;
                return;
            }

            if (_inlineTextEditor is null)
            {
                var created = new TextBox
                {
                    AcceptsReturn = true, BorderThickness = new(1),
                    BorderBrush = Brushes.DeepSkyBlue, Background = Brushes.Transparent,
                    MinWidth = 32, MinHeight = 24, HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Top,
                    HorizontalContentAlignment = HorizontalAlignment.Left,
                    VerticalContentAlignment = VerticalAlignment.Top,
                };
                created.TextChanged += (_, _) => _session.UpdateTextEditDraft(created.Text ?? "");
                created.AddHandler(KeyDownEvent, (_, args) =>
                {
                    if (args.Handled) return;
                    // ReSharper disable once SwitchStatementMissingSomeEnumCasesNoDefault
                    switch (args.Key)
                    {
                        case Key.Escape:
                            args.Handled = true;
                            _session.CancelTextEdit();
                            break;
                        case Key.Enter when !args.KeyModifiers.HasFlag(KeyModifiers.Shift):
                            args.Handled = true;
                            _session.CommitTextEdit();
                            break;
                    }
                }, RoutingStrategies.Tunnel);
                AnnotationTextInput.ProtectComposition(created);
                _inlineTextEditor = created;
                Children.Add(created);
                Dispatcher.UIThread.Post(() =>
                {
                    if (!ReferenceEquals(created, _inlineTextEditor) || !_session.IsEditingText) return;
                    created.Focus();
                    created.CaretIndex = created.Text?.Length ?? 0;
                });
            }

            var editor = _inlineTextEditor;
            var properties = _session.Properties;
            editor.FontFamily = string.IsNullOrEmpty(properties.FontFamily)
                ? FontFamily.Default
                : new(properties.FontFamily);
            editor.FontSize = Math.Max(1, _session.FontSize / Scale);
            editor.FontWeight = properties.Bold ? FontWeight.Bold : FontWeight.Normal;
            editor.FontStyle = properties.Italic ? FontStyle.Italic : FontStyle.Normal;
            editor.Foreground = new SolidColorBrush(_session.Color);
            editor.Padding = new(properties.TextPadding / Scale);
            editor.TextWrapping = _session.TextEditWraps ? TextWrapping.Wrap : TextWrapping.NoWrap;
            editor.MaxWidth = double.PositiveInfinity;
            editor.Width = _session.TextEditWraps
                ? Math.Max(1, (_session.TextEditWrapWidth + properties.TextPadding * 2) / Scale + 2)
                : double.NaN;
            if (editor.Text != _session.TextEditValue) editor.Text = _session.TextEditValue;
            SetLeft(editor, (_session.TextEditPosition.X - _monitor.X) / Scale - 1);
            SetTop(editor, (_session.TextEditPosition.Y - _monitor.Y) / Scale - 1);
        }
    }
}
