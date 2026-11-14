using Avalonia.Controls;
using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    internal sealed partial class PinnedDocumentWindow
    {
        private void AddColorCopyCommands(ContextMenu menu)
        {
            if (Document is not PinnedColorDocument color) return;

            menu.Items.Add(new Separator());
            var formats = new PinnedColorFormats(Color.Parse(color.Value));
            foreach (var format in Enum.GetValues<PinnedColorFormat>())
            {
                var description = formats.Format(format, true);
                var value = formats.Format(format);
                var item = new MenuItem { Tag = "copy-color-" + format };
                item.Click += (_, _) => _ = Presentation.RunMenuAsync(() => RunAsync(() => CopyTextAsync(value)));
                menu.Opened += (_, _) => item.Header = UiText.Format("PinnedCopyColorFormat", description);
                menu.Items.Add(item);
            }

            menu.Items.Add(new Separator());
        }
    }
}
