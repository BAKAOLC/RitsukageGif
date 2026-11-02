namespace RitsukageGif.Desktop
{
    public sealed partial class ApplicationHost
    {
        private ClearRecordDialog? _clearRecordsWindow;
        private Task _recordClearingTask = Task.CompletedTask;

        private async Task OpenClearRecordsAsync()
        {
            if (IsClosing || IsSelecting) return;
            if (_clearRecordsWindow is { IsVisible: true } existing)
            {
                WindowActivation.Show(existing);
                return;
            }

            if (_clearRecordsWindow is not null) return;
            var dialog = new ClearRecordDialog();
            _clearRecordsWindow = dialog;
            Changed += CloseDuringCaptureOrShutdown;
            Notify();
            try
            {
                var selection = await dialog.ShowAsync(View is { IsVisible: true } view ? view : null);
                if (selection is null || IsClosing || IsSelecting) return;
                _recordClearingTask = ClearRecordsAsync(selection);
                await _recordClearingTask;
            }
            catch (Exception error)
            {
                HostLogger.ZLogError(error, $"Record clearing failed.");
                throw;
            }
            finally
            {
                Changed -= CloseDuringCaptureOrShutdown;
                _clearRecordsWindow = null;
                _recordClearingTask = Task.CompletedTask;
                Notify();
            }

            return;

            void CloseDuringCaptureOrShutdown()
            {
                if ((IsClosing || IsSelecting) && dialog.IsVisible) dialog.Close();
            }
        }

        private async Task ClearRecordsAsync(ClearRecordSelection selection)
        {
            await _captureHistoryConfiguration;
            HostLogger.ZLogInformation(
                $"Record clearing started. Screenshots={selection.ScreenshotHistory} Regions={selection.RegionHistory} PinHistory={selection.PinHistory} PinsOnScreen={selection.PinsOnScreen}");
            await _captureHistory.ClearAsync(selection.ScreenshotHistory, selection.RegionHistory);
            await _pinnedImages.ClearRecordDataAsync(selection.PinHistory, selection.PinsOnScreen);
            HostLogger.ZLogInformation($"Record clearing completed.");
            SetStatusKey("ClearRecordsCompleted");
        }
    }
}
