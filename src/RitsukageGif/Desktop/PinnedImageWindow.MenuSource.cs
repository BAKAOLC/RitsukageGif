using Avalonia.Controls;

namespace RitsukageGif.Desktop
{
    // ReSharper disable once ClassCannotBeInstantiated
    internal sealed partial class PinnedImageWindow
    {
        bool IPinnedWindow.IsLocked => IsLocked;

        void IPinnedWindow.SetLocked(bool value)
        {
            SetLocked(value);
        }

        private static void AlignSourceMenu(ContextMenu menu)
        {
            var remaining = menu.Items.ToList();
            menu.Items.Clear();
            AddActions("copy", "copy-original", "save", "save-original", "copy-plain-text", "copy-html");
            menu.Items.Add(new Separator());
            AddActions("annotate", "hide-mark", "through", "window-shade", "topmost", "hide-others");
            menu.Items.Add(new Separator());
            AddActions("move-group", "thumbnail", "set-title", "lock", "close", "destroy");
            if (remaining.Count != 0 && remaining[0] is not Separator) menu.Items.Add(new Separator());
            foreach (var item in remaining)
            {
                if (item is Separator && menu.Items[^1] is Separator) continue;
                menu.Items.Add(item);
            }

            return;

            void AddActions(params string[] actions)
            {
                foreach (var action in actions)
                {
                    var item = remaining.OfType<MenuItem>()
                        .FirstOrDefault(candidate => candidate.Tag as string == action);
                    if (item is null) continue;
                    remaining.Remove(item);
                    menu.Items.Add(item);
                }
            }
        }
    }
}
