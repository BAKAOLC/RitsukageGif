namespace RitsukageGif.Desktop
{
    internal sealed partial class PinnedDocumentWindow
    {
        internal Action<IPinnedWindow, double>? OpacityAdjustmentRequested { get; init; }

        private void AdjustOpacity(double step)
        {
            if (OpacityAdjustmentRequested is not null)
            {
                OpacityAdjustmentRequested(this, step);
            }
            else
            {
                Opacity = PinnedOpacityStep.Apply(Opacity, step);
                Presentation.ShowOpacityMessage();
            }
        }
    }
}
