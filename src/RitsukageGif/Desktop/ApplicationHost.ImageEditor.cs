using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;

namespace RitsukageGif.Desktop
{
    public sealed partial class ApplicationHost
    {
        private readonly HashSet<ImageEditorWindow> _imageEditors = [];
        private (PinnedImageContent Image, ScreenshotResult Source)? _pendingImageEdit;

        private async Task ShowImageEditorAsync(string? path = null)
        {
            if (IsClosing) return;
            var token = _cleanupCancellation.Token;
            IReadOnlyList<IStorageFile> files = [];
            try
            {
                if (string.IsNullOrWhiteSpace(path))
                {
                    files = await StoragePickerAwaiter.WaitAsync(_services.Context.StorageProvider.OpenFilePickerAsync(
                        new()
                        {
                            Title = UiText.Get("ImageEditorTitle"), AllowMultiple = false,
                            FileTypeFilter = [FilePickerFileTypes.ImageAll],
                        }), token);
                    path = files.Count == 0 ? null : files[0].TryGetLocalPath();
                    if (path is null) return;
                }

                using var resource = await MediaResource.OpenAsync(path, token);
                if (new FileInfo(resource.Path).Length > PinnedImageContent.MaximumEncodedBytes)
                    throw new InvalidDataException(UiText.Get("RecordingTooLarge"));
                var bytes = await File.ReadAllBytesAsync(resource.Path, token);
                var image = await Task.Run(() => PinnedImageContent.Decode(bytes, token), token);
                token.ThrowIfCancellationRequested();
                ShowImageEditor(image, null);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
            }
            finally
            {
                StoragePickerAwaiter.DisposeFiles(files);
            }
        }

        private async Task<bool> EditScreenshotAsync(ScreenshotResult result, CancellationToken token)
        {
            var image = await Task.Run(() => PinnedImageContent.Decode(result.Png, token), token);
            token.ThrowIfCancellationRequested();
            _pendingImageEdit = (image, result);
            return true;
        }

        private void ShowPendingImageEditor()
        {
            var pending = _pendingImageEdit;
            _pendingImageEdit = null;
            if (!IsClosing && pending is { } document) ShowImageEditor(document.Image, document.Source);
        }

        private void ShowImageEditor(PinnedImageContent image, ScreenshotResult? source)
        {
            if (IsClosing) return;
            var window = new ImageEditorWindow(image, source, Settings.Annotation,
                SelectionCommands.SnapshotBindings(), OutputScreenshotAsync)
            {
                PreferencesProvider = () => Settings.Annotation,
                PreferencesChanged = PersistAnnotationPreferences,
                TopmostChanged = PersistImageEditorTopmost,
                RecognitionRequested = RecognizeEditorImageAsync,
                TranslationRequested = (owner, png, token) => ShowImageTranslationAsync(owner, png, token),
            };
            window.PrivacyRequested = (request, token) =>
                DetectEditorPrivacyAsync(window, window.SourceFrame, request, token);
            window.ConnectServices();
            window.ApplyTopmostPreference(Settings.ImageEditorTopmost);
            if (View is { } main) window.Icon = main.Icon;
            _imageEditors.Add(window);
            window.Closed += (_, _) => _imageEditors.Remove(window);
            WindowActivation.Show(window);
        }

        private void PersistImageEditorTopmost(bool topmost)
        {
            var updated = Settings with { ImageEditorTopmost = topmost };
            QueueSettingsSave(updated);
            Settings = updated;
            Notify();
        }

        private async Task RecognizeEditorImageAsync(Window owner, byte[] png, RecognitionMode mode,
            CancellationToken token)
        {
            var preferences = Settings.Recognition.Normalize();
            if (mode == RecognitionMode.Text && !preferences.ShowDialogOnRecognition)
            {
                var text = await RecognitionOutput.RecognizeAsync(png, preferences,
                    _pinnedImages.TextRecognitionService, token);
                if (string.IsNullOrWhiteSpace(text))
                    throw new InvalidOperationException(UiText.Get("RecognitionEmpty"));
                var clipboard = owner.Clipboard ?? throw new InvalidOperationException(UiText.Get("ClipboardFailed"));
                await clipboard.SetTextAsync(text);
                await clipboard.FlushAsync();
                return;
            }

            await RecognitionWindow.ShowAsync(owner, png, mode, preferences: preferences,
                translationPreferences: Settings.Translation, token: token,
                pinFormula: (_, formula) => _pinnedImages.PinFormulas(formula),
                copyFormatChanged: PersistFormulaCopyFormat,
                originalImageVisibilityChanged: PersistRecognitionOriginalImage,
                latexFormatChanged: PersistFormulaLatexFormat);
        }

        private async Task DetectEditorPrivacyAsync(ImageEditorWindow window, CapturedFrame frame,
            long request, CancellationToken token)
        {
            try
            {
                var service = new PrivacyDetectionService([
                    new OcrPrivacyCandidateProvider(_pinnedImages.TextRecognitionService,
                        Settings.Recognition.LanguageTag),
                    new BarcodePrivacyCandidateProvider(new BarcodeRecognitionService()),
                ]);
                var detected = await service.DetectAsync(frame, token);
                token.ThrowIfCancellationRequested();
                if (window.Session.SetPrivacyCandidates(request, detected.Candidates,
                        Settings.Annotation.AutoSelectPrivacyMosaic) && detected.Failures.Count > 0)
                    window.Session.SetExportState(false, string.Join(Environment.NewLine,
                        detected.Failures.Select(failure => failure.Message)));
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
            }
            catch (Exception error)
            {
                HostLogger.ZLogWarning(error, $"Static image editor privacy detection failed.");
                window.Session.FailPrivacyDetection(request, error.Message);
            }
        }
    }
}
