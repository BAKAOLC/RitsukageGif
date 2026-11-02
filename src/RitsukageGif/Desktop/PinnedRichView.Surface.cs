using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    internal sealed partial class PinnedRichView
    {
        private sealed class PinnedRichSurface(PinnedRichView owner) : Control
        {
            protected override Size MeasureOverride(Size availableSize)
            {
                owner.EnsureLayout(owner._formatWidth);
                return owner._layout!.Size;
            }

            public override void Render(DrawingContext context)
            {
                base.Render(context);
                owner.UpdateVisibleResources();
                owner._layout?.Draw(context, owner.VisibleBounds, owner.SelectionStart,
                    owner.SelectionEnd - owner.SelectionStart);
            }
        }
    }
}
