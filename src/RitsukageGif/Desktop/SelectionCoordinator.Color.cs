using Avalonia.Input.Platform;

namespace RitsukageGif.Desktop
{
    internal sealed partial class SelectionCoordinator
    {
        private ColorFormatDefinition _sampleColorDefinition = SampleColorFormats.CreateDefaults()[0];
        internal SampleColorTemplate SampleColorTemplate { get; private set; } = SampleColorFormats.DefaultTemplate;

        private void InitializeSampleColorFormat()
        {
            _sampleColorDefinition = SampleColorFormats.First(CapturePreferences.ColorFormats,
                CapturePreferences.MagnifierColorFormatId);
            SampleColorTemplate = SampleColorFormats.Compile(_sampleColorDefinition);
            CapturePreferences.MagnifierColorFormatId = _sampleColorDefinition.Id;
        }

        private void CycleSampleColorFormat()
        {
            _sampleColorDefinition =
                SampleColorFormats.Next(CapturePreferences.ColorFormats, _sampleColorDefinition.Id);
            SampleColorTemplate = SampleColorFormats.Compile(_sampleColorDefinition);
            CapturePreferences = CapturePreferences with { MagnifierColorFormatId = _sampleColorDefinition.Id };
            PreferencesChanged?.Invoke();
        }

        private async Task CopySampleColorAsync()
        {
            var window = _windows.FirstOrDefault(item => item.Region.Contains(_mouse));
            if (window is null || !window.TryGetSampleColor(_mouse, out var color)) return;
            try
            {
                var clipboard = window.Clipboard ?? throw new InvalidOperationException(UiText.Get("ClipboardFailed"));
                var text = SampleColorTemplate.Format(new(color.R, color.G, color.B, color.A, _mouse, Snapshot.Region));
                await clipboard.SetTextAsync(text);
                await clipboard.FlushAsync();
            }
            catch (Exception error)
            {
                if (!_finished) window.ShowOperationError(error.Message);
            }
        }
    }
}
