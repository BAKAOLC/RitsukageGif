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
            var (png, output) = await PrepareRecognitionInputAsync();
            var preferences = RecognitionPreferencesProvider().Normalize();
            var recognition = await RecognitionOutput.RecognizeAsync(png, preferences,
                TextRecognitionServiceProvider?.Invoke() ?? DefaultTextRecognition, _lifetime.Token);
            var text = RecognitionTextFormatter.Format(recognition, preferences);
            if (string.IsNullOrEmpty(text)) throw new InvalidOperationException(UiText.Get("RecognitionEmpty"));
            if (preferences.ShowDialogOnCopyAll)
            {
                await RecognitionWindow.ShowAsync(this, png, RecognitionMode.Text,
                    preferences: preferences, initialResult: recognition, token: _lifetime.Token,
                    copyDirectlyNextTime: () =>
                        DirectRecognitionCopyRequested?.Invoke(RecognitionConfirmationKind.PinAllText),
                    dialogPreferencesChanged: RecognitionDialogPreferencesChanged,
                    originalImageVisibilityChanged: RecognitionOriginalImageVisibilityChanged);
            }
            else
            {
                var clipboard = Clipboard ?? throw new InvalidOperationException(UiText.Get("ClipboardFailed"));
                await clipboard.SetTextAsync(text);
                await clipboard.FlushAsync();
            }

            if (output is not null) await SaveAutomaticallyAsync(ScreenshotAutoSaveTriggers.Recognize, output);
        }

        private Task ToggleAnnotationToolbarAsync()
        {
            if (_annotation is null) return BeginAnnotationAsync();
            EndAnnotation();
            return Task.CompletedTask;
        }

        private async Task DragContentAsync(PointerPressedEventArgs args)
        {
            var output = await CaptureOutputAsync(OutputColorMode.Both, _lifetime.Token);
            var png = output.Hdr is { } hdr
                ? await HdrImageService.EncodePngAsync(hdr, _lifetime.Token)
                : output.Png;
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
                ClipboardPlatformFormats.SetImage(item, png, "image/png", output.Png);
                ClipboardWindowShape.Write(item, output.WindowShape, output.CaptureScaling);
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
