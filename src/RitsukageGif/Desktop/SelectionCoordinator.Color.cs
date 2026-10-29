namespace RitsukageGif.Desktop
{
    internal sealed partial class SelectionCoordinator
    {
        internal SampleColorFormat SampleColorFormat { get; private set; }

        private async Task CopySampleColorAsync()
        {
            var window = _windows.FirstOrDefault(item => item.Region.Contains(_mouse));
            if (window is null || !window.TryGetSampleColor(_mouse, out var color)) return;
            try
            {
                var clipboard = window.Clipboard ?? throw new InvalidOperationException(UiText.Get("ClipboardFailed"));
                await clipboard.SetTextAsync(SampleColorText.Format(color, SampleColorFormat));
                await clipboard.FlushAsync();
            }
            catch (Exception error)
            {
                if (!_finished) window.ShowOperationError(error.Message);
            }
        }
    }
}
