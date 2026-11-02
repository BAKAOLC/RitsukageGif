using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;

namespace RitsukageGif.Desktop
{
    // ReSharper disable once ClassCannotBeInstantiated
    internal sealed partial class PinnedImageWindow
    {
        private static readonly ILogger PinLogger = LogHub.GetLogger("Desktop.Pins.Window");

        private async Task CopyRecognizedTextAsync()
        {
            var output = await CaptureOutputAsync(OutputColorMode.Sdr, _lifetime.Token);
            var png = output.Png;
            var preferences = RecognitionPreferencesProvider().Normalize();
            var text = await RecognitionOutput.RecognizeAsync(png, preferences,
                TextRecognitionServiceProvider?.Invoke() ?? DefaultTextRecognition, _lifetime.Token);
            if (string.IsNullOrEmpty(text)) throw new InvalidOperationException(UiText.Get("RecognitionEmpty"));
            var clipboard = Clipboard ?? throw new InvalidOperationException(UiText.Get("ClipboardFailed"));
            await clipboard.SetTextAsync(text);
            await clipboard.FlushAsync();
            await SaveAutomaticallyAsync(ScreenshotAutoSaveTriggers.Recognize, output);
            if (preferences.ShowDialogOnCopyAll)
                await RecognitionWindow.ShowAsync(this, png, RecognitionMode.Text,
                    preferences: preferences, initialText: text, token: _lifetime.Token);
        }

        private Task ToggleAnnotationToolbarAsync()
        {
            if (_annotation is null) return BeginAnnotationAsync();
            _annotationToolbar?.IsVisible = !_annotationToolbar.IsVisible;
            return Task.CompletedTask;
        }

        private async Task DragContentAsync(PointerPressedEventArgs args)
        {
            var png = await CurrentImageAsync();
            var sdr = _hdrSource is null
                ? png
                : (await Task.Run(() => PinnedImageContent.Decode(png, _lifetime.Token), _lifetime.Token)).SdrPng!;
            var files = new ScreenshotClipboardFileStore(Path.Combine(Path.GetTempPath(), "RitsukageGif", "Drag"));
            await files.CleanupAsync(new HashSet<string>(StringComparer.OrdinalIgnoreCase));
            var path = await files.CreateAsync(png, ScreenshotClipboardFileFormat.Png,
                SavePreferencesProvider().Normalize().Quality, _lifetime.Token);
            var published = false;
            try
            {
                var file = await StorageProvider.TryGetFileFromPathAsync(path) ?? throw new IOException(path);
                var transfer = new DataTransfer();
                var item = new DataTransferItem();
                ClipboardPlatformFormats.SetImage(item, png, "image/png", sdr);
                item.Set(DataFormat.File, file);
                transfer.Add(item);
                _mouseGesture.Reset();
                _pressedButtons.Clear();
                args.Pointer.Capture(null);
                using var registration = PinnedDragRegistration.Register(transfer, this, true);
                published = await DragDrop.DoDragDropAsync(args, transfer, DragDropEffects.Copy) !=
                            DragDropEffects.None;
            }
            finally
            {
                if (!published) files.Delete(path);
            }
        }
    }
}
