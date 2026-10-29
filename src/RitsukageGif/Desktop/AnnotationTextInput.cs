using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace RitsukageGif.Desktop
{
    internal static class AnnotationTextInput
    {
        public static void ProtectComposition(TextBox editor)
        {
            var heldEnter = false;
            var heldEscape = false;
            editor.AddHandler(InputElement.KeyDownEvent, (_, args) =>
            {
                if (args.Key is not (Key.Enter or Key.Escape)) return;
                var held = args.Key == Key.Enter ? heldEnter : heldEscape;
                if (!held && !IsComposing(editor)) return;
                if (args.Key == Key.Enter) heldEnter = true;
                else heldEscape = true;
                args.Handled = true;
            }, RoutingStrategies.Tunnel);
            editor.AddHandler(InputElement.KeyUpEvent, (_, args) =>
            {
                // ReSharper disable once SwitchStatementMissingSomeEnumCasesNoDefault
                switch (args.Key)
                {
                    case Key.Enter:
                        heldEnter = false;
                        break;
                    case Key.Escape:
                        heldEscape = false;
                        break;
                }
            }, RoutingStrategies.Tunnel, true);
            editor.LostFocus += (_, _) =>
            {
                heldEnter = false;
                heldEscape = false;
            };
        }

        private static bool IsComposing(TextBox editor)
        {
            return editor.GetVisualDescendants().OfType<TextPresenter>()
                .Any(presenter => !string.IsNullOrEmpty(presenter.PreeditText));
        }
    }
}
