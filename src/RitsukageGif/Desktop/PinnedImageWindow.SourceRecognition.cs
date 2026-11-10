using Avalonia.Controls;
using Avalonia.Input.Platform;

namespace RitsukageGif.Desktop
{
    // ReSharper disable once ClassCannotBeInstantiated
    internal sealed partial class PinnedImageWindow
    {
        private bool? _ocrTextSelectable;
        private bool OcrTextSelectable => _ocrTextSelectable ?? CurrentPreferences.SelectableText;

        private PinnedImageTranslationStatus SourceRecognitionStatus => _textRecognitionCancellation is not null
            ? PinnedImageTranslationStatus.Pending
            : _recognizedText is { Lines.Count: > 0 }
                ? PinnedImageTranslationStatus.Ready
                : _recognizedText is { LanguageTag.Length: > 0 }
                    ? PinnedImageTranslationStatus.Empty
                    : PinnedImageTranslationStatus.None;

        internal void InitializeTextRecognition()
        {
            if (_closed || _textRecognitionInitialized || !CanUseInlineTranslation) return;
            _textRecognitionInitialized = true;
            if (OcrTextSelectable) EnsureTextRecognition(true);
        }

        private Task ToggleOcrTextSelectableAsync()
        {
            if (SourceRecognitionStatus == PinnedImageTranslationStatus.Pending) return Task.CompletedTask;
            var next = !OcrTextSelectable;
            if (next && SourceRecognitionStatus == PinnedImageTranslationStatus.Empty)
            {
                Presentation.ShowMessage(UiText.Get("PinnedImageNoTextDetected"));
                return Task.CompletedTask;
            }

            _ocrTextSelectable = next;
            switch (next)
            {
                case true when SourceRecognitionStatus == PinnedImageTranslationStatus.None:
                    Presentation.ShowMessage(UiText.Get("PinnedImageRecognizingText"));
                    EnsureTextRecognition(true);
                    break;
                case false:
                    ClearTextSelection();
                    break;
            }

            RefreshOcrLayerVisibility();
            RefreshHdrPreview();
            StateChanged?.Invoke(this, EventArgs.Empty);
            return Task.CompletedTask;
        }

        private void RefreshOcrLayerVisibility()
        {
            var underlay = HasTextMarkup ? _textMarkupSession : null;
            if (_annotationScene is { } scene && !ReferenceEquals(scene.Underlay, underlay))
                scene.Underlay = underlay;
            if (_textSelectionSurface is not { } surface) return;
            surface.IsVisible = HasTextSelection || HasTextMarkup;
            surface.InvalidateVisual();
            InvalidateInputUi();
        }

        private Task DetectSourceTextAsync()
        {
            if (_closed || SourceRecognitionStatus == PinnedImageTranslationStatus.Pending)
                return Task.CompletedTask;
            EnsureTextRecognition(true);
            return Task.CompletedTask;
        }

        private void SetPinRecognitionLanguage(string language)
        {
            EnsureTextRecognition(true, language);
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        private async Task CopyCurrentRecognizedTextAsync()
        {
            if (_activeTranslatedRaster is null && _recognizedText is null)
            {
                await CopyRecognizedTextAsync();
                return;
            }

            var preferences = RecognitionPreferencesProvider().Normalize();
            var output = AutomaticSave is not null &&
                         (SavePreferencesProvider().Normalize().AutoSaveTriggers &
                          ScreenshotAutoSaveTriggers.Recognize) != 0
                ? await CaptureOutputAsync(OutputColorMode.Sdr, _lifetime.Token)
                : null;
            var text = _activeTranslatedRaster is { } translated
                ? translated.Text.Text
                : RecognitionTextFormatter.Format(_recognizedText!, preferences);
            if (_activeTranslatedRaster is null && preferences.ShowDialogOnCopyAll)
            {
                await RecognitionWindow.ShowAsync(this, _png, RecognitionMode.Text,
                    preferences: preferences, initialResult: _recognizedText, token: _lifetime.Token,
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

        private async Task PopulatePinRecognitionLanguagesAsync(MenuItem parent)
        {
            try
            {
                var service = TextRecognitionServiceProvider?.Invoke() ?? DefaultTextRecognition;
                var capabilities = await service.GetCapabilitiesAsync(_lifetime.Token);
                if (_closed) return;
                parent.Items.Clear();
                foreach (var language in capabilities.Languages)
                {
                    var selected =
                        language.Tag.Equals(_recognizedText?.LanguageTag, StringComparison.OrdinalIgnoreCase);
                    var item = new MenuItem
                    {
                        Header = language.DisplayName, Tag = language.Tag,
                        ToggleType = selected
                            ? MenuItemToggleType.CheckBox
                            : MenuItemToggleType.None,
                        IsChecked = selected, IsEnabled = !selected,
                    };
                    item.Click += (_, _) => SetPinRecognitionLanguage(language.Tag);
                    parent.Items.Add(item);
                }
            }
            catch (OperationCanceledException) when (_closed || _lifetime.IsCancellationRequested)
            {
            }
            catch (Exception error)
            {
                ReportInlineTranslationError(error);
            }
        }
    }
}
