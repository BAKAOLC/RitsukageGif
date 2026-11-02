namespace RitsukageGif.Desktop
{
    internal sealed partial class SelectionCoordinator
    {
        private async Task CompleteAfterAutomaticWatermarkAsync(SelectionResult result,
            ScreenshotAnnotationSession annotations)
        {
            try
            {
                await annotations.AutomaticWatermarkTask.WaitAsync(_cancel.Token);
                if (!_finished && ReferenceEquals(Annotations, annotations) && annotations.Error is null &&
                    Snapshot.Region == result.Region)
                    CompleteSelection(result);
            }
            catch (OperationCanceledException) when (_cancel.IsCancellationRequested)
            {
            }
        }
    }
}
