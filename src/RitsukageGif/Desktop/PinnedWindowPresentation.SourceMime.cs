using Avalonia.Controls;
using Avalonia.Controls.Primitives;

namespace RitsukageGif.Desktop
{
    internal sealed partial class PinnedWindowPresentation
    {
        internal PinnedSourceMime? SourceMime { get; set; }

        internal void AddSourceMimeCommands(ContextMenu menu, Func<PinnedImagePreferences> preferences)
        {
            var anchor = menu.Items.OfType<MenuItem>().LastOrDefault(item =>
                item.Tag as string is "save-original" or "save-image" or "save");
            var index = anchor is null ? menu.Items.Count : menu.Items.IndexOf(anchor) + 1;
            Add("PinnedCopyPlainText", "copy-plain-text", PinnedSourceMimeFormat.PlainText);
            Add("PinnedCopyWithHtml", "copy-html", PinnedSourceMimeFormat.Html);
            return;

            void Add(string key, string action, PinnedSourceMimeFormat format)
            {
                var item = new MenuItem { Tag = action, IsVisible = false };
                item.Bind(HeaderedSelectingItemsControl.HeaderProperty, LiveText.Resource(key));
                item.Click += async (_, _) => await RunMenuAsync(() =>
                    RunOperationAsync(() => CopySourceMimeAsync(format)));
                menu.Items.Insert(index++, item);
                menu.Opened += (_, _) =>
                {
                    item.IsVisible = format == PinnedSourceMimeFormat.PlainText
                        ? SourceMime?.HasText == true
                        : SourceMime?.HasHtml == true;
                    item.InputGesture = preferences().ShowMenuShortcuts
                        ? BuiltinShortcuts.Snapshot().FirstOrDefault(binding =>
                            binding.ContextKey == "BuiltinShortcutPinnedContext" && binding.Action == action)?.Gesture
                        : null;
                };
            }
        }

        internal async Task CopySourceMimeAsync(PinnedSourceMimeFormat format)
        {
            switch (format)
            {
                case PinnedSourceMimeFormat.PlainText when SourceMime?.HasText != true:
                    ShowMessage(UiText.Get("PinnedNoPlainTextToCopy"));
                    return;
                case PinnedSourceMimeFormat.Html when SourceMime?.HasHtml != true:
                    ShowMessage(UiText.Get("PinnedNoHtmlTextToCopy"));
                    return;
                case PinnedSourceMimeFormat.PlainText:
                case PinnedSourceMimeFormat.Html:
                case PinnedSourceMimeFormat.Typed:
                default:
                    break;
            }

            if (SourceMime is not { } source) return;
            var clipboard = _owner.Clipboard ?? throw new InvalidOperationException(UiText.Get("ClipboardFailed"));
            await clipboard.SetDataAsync(source.CreateTransfer(format));
            await clipboard.FlushAsync();
            if (format == PinnedSourceMimeFormat.PlainText) ShowMessage(UiText.Get("PinnedPlainTextCopied"));
        }
    }
}
