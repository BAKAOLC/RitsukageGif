using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;

namespace RitsukageGif.Desktop
{
    // ReSharper disable once ClassCannotBeInstantiated
    internal sealed partial class PinnedImageWindow
    {
        private bool CanUseInlineTranslation => !LiveCaptureActive && !AnimationActive;

        private bool HasInlineTranslationBackend
        {
            get
            {
                var preferences = TranslationPreferencesProvider().Normalize();
                return Uri.TryCreate(preferences.Endpoint, UriKind.Absolute, out var uri) &&
                       uri.Scheme is "http" or "https";
            }
        }

        private void AddImageSourcePrelude(ContextMenu menu)
        {
            _viewport.LayoutUpdated += (_, _) => RefreshTransparentBackground();
            ActualThemeVariantChanged += (_, _) =>
            {
                RefreshTransparentBackground();
                RefreshHdrPreview();
            };
            Opened += (_, _) => Dispatcher.UIThread.Post(InitializeTextRecognition, DispatcherPriority.Background);
            var cutOut = Command("PinnedImageCutOut", CutOutThumbnailAsync);
            cutOut.Tag = "cut-out";
            var copySelected = Command("PinnedImageCopySelectedText", CopySelectedTextAsync);
            copySelected.Tag = "copy-selected-ocr";
            var selectable = Command("PinnedImageOcrTextSelectable", ToggleOcrTextSelectableAsync);
            selectable.Tag = "ocr-text-selectable";
            selectable.ToggleType = MenuItemToggleType.CheckBox;
            var recognition = new MenuItem
            {
                Tag = "source-recognition",
                [!HeaderedSelectingItemsControl.HeaderProperty] = LiveText.Resource("PinnedImageRecognition"),
            };
            var detect = Command("PinnedImageDetectText", () =>
                SourceRecognitionStatus == PinnedImageTranslationStatus.Ready
                    ? CopyCurrentRecognizedTextAsync()
                    : DetectSourceTextAsync());
            recognition.Items.Add(detect);
            var language = new MenuItem
            {
                [!HeaderedSelectingItemsControl.HeaderProperty] = LiveText.Resource("PinnedImageRecognitionLanguage"),
            };
            recognition.Items.Add(language);
            var table = Command("PinnedMenuFormRecognition", () => RecognizeAsync(RecognitionMode.Table));
            table.Tag = "recognize-table";
            recognition.Items.Add(table);
            var formula = Command("FormulaRecognition", () => RecognizeAsync(RecognitionMode.Formula));
            formula.Tag = "recognize-formula";
            recognition.Items.Add(formula);
            var barcode = Command("RecognizeBarcode", () => RecognizeAsync(RecognitionMode.Barcode));
            barcode.Tag = "recognize-barcode";
            recognition.Items.Add(barcode);

            var translate = Command("PinnedInlineTranslate", ToggleInlineTranslationAsync);
            translate.Tag = "translate-inline";
            translate.ToggleType = MenuItemToggleType.CheckBox;
            var targetPicker = new TranslationLanguagePicker();
            var targetUpdating = false;
            targetPicker.LanguageTagChanged += (_, _) =>
            {
                if (!targetUpdating && CanUseInlineTranslation)
                    StartInlineTranslation(targetPicker.LanguageTag);
            };
            var target = new MenuItem
            {
                [!HeaderedSelectingItemsControl.HeaderProperty] = LiveText.Resource("PinnedImageTranslateTo"),
            };
            target.Items.Add(new MenuItem { Header = targetPicker });
            var separator = new Separator();
            foreach (var item in new Control[]
                         { separator, target, translate, recognition, selectable, copySelected, cutOut })
                menu.Items.Insert(0, item);

            menu.Opened += (_, _) =>
            {
                var staticImage = CanUseInlineTranslation;
                var source = _inlineTranslation is not { Enabled: true };
                var status = SourceRecognitionStatus;
                cutOut.IsVisible = staticImage && _isThumbnail;
                cutOut.IsEnabled = CutOutRequested is not null && !_busy;
                copySelected.IsVisible = staticImage && HasTextSelection && CanSelectImageText &&
                                         status == PinnedImageTranslationStatus.Ready;
                selectable.IsVisible = staticImage && status == PinnedImageTranslationStatus.Ready;
                selectable.IsChecked = _ocrTextSelectable ?? true;
                recognition.IsVisible = staticImage && source;
                detect.Header = UiText.Get(status switch
                {
                    PinnedImageTranslationStatus.Pending => "PinnedImageDetectingText",
                    PinnedImageTranslationStatus.Ready => "PinnedImageCopyAllText",
                    PinnedImageTranslationStatus.Empty => "PinnedImageNoTextDetected",
                    _ => "PinnedImageDetectText",
                });
                detect.IsEnabled =
                    status is not (PinnedImageTranslationStatus.Pending or PinnedImageTranslationStatus.Empty);
                detect.InputGesture = CurrentPreferences.ShowMenuShortcuts &&
                                      status == PinnedImageTranslationStatus.Ready
                    ? BuiltinShortcuts.Snapshot().FirstOrDefault(binding =>
                            binding is { ContextKey: BuiltinShortcutContexts.Pinned, Action: "copy-all-text" })
                        ?.Gesture
                    : null;
                translate.IsVisible = target.IsVisible = staticImage &&
                                                         (HasInlineTranslationBackend ||
                                                          _inlineTranslation?.Cached is not null);
                translate.IsChecked = _inlineTranslation is { Enabled: true };
                targetUpdating = true;
                targetPicker.LanguageTag = _inlineTranslation is { TargetLanguage.Length: > 0 } session
                    ? session.TargetLanguage
                    : TranslationPreferencesProvider().Normalize().TargetLanguage;
                targetUpdating = false;
                separator.IsVisible = cutOut.IsVisible || copySelected.IsVisible || selectable.IsVisible ||
                                      recognition.IsVisible || translate.IsVisible;
                if (recognition.IsVisible) _ = PopulatePinRecognitionLanguagesAsync(language);
                var bindings = BuiltinShortcuts.Snapshot();
                foreach (var item in new[] { selectable, translate, table, formula, barcode })
                    item.InputGesture = CurrentPreferences.ShowMenuShortcuts
                        ? bindings.FirstOrDefault(binding =>
                            binding.ContextKey == BuiltinShortcutContexts.PinnedImage &&
                            binding.Action == item.Tag as string)?.Gesture
                        : null;
            };
        }
    }
}
