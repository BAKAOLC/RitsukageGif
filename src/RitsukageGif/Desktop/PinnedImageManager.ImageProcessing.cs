namespace RitsukageGif.Desktop
{
    internal sealed partial class PinnedImageManager
    {
        private void AdjustSelectionOpacity(IPinnedWindow source, double step)
        {
            var selection = SelectionSnapshot(source);
            foreach (var window in selection.Length == 0 ? [source] : selection)
            {
                window.Window.Opacity = PinnedOpacityStep.Apply(window.Window.Opacity, step);
                window.Presentation.ShowOpacityMessage();
            }
        }
    }
}
