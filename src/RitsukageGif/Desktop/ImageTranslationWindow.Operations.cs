using Avalonia.Input.Platform;

namespace RitsukageGif.Desktop
{
    // ReSharper disable once ClassCannotBeInstantiated
    internal sealed partial class ImageTranslationWindow
    {
        private static readonly ILogger Logger = LogHub.GetLogger("Desktop.ImageTranslation");

        private void StartTranslation()
        {
            if (_lifetime.IsCancellationRequested) return;
            _pending = true;
            _work?.Cancel();
            if (!_loopRunning) _operation = RunLoopAsync();
        }

        private async Task RunLoopAsync()
        {
            _loopRunning = true;
            try
            {
                while (_pending && !_lifetime.IsCancellationRequested)
                {
                    _pending = false;
                    using var work = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
                    _work = work;
                    var token = work.Token;
                    SetBusy(true);
                    SetStatus("TranslationWorking");
                    _run.Bind(ContentProperty, LiveText.Resource("TranslationRun"));
                    var readingCredential = false;
                    var preferences = _preferences with
                    {
                        SourceLanguage = _sourceLanguage.LanguageTag, TargetLanguage = _targetLanguage.LanguageTag,
                    };
                    try
                    {
                        var apiKey = "";
                        if (preferences.UseApiKey)
                        {
                            readingCredential = true;
                            apiKey = await CredentialVault.ReadAsync(preferences.CredentialKey, token) ?? "";
                            readingCredential = false;
                            if (apiKey.Length == 0)
                            {
                                SetStatus("TranslationCredentialMissing");
                                continue;
                            }
                        }

                        if (_recognized is null || _recognizedLanguage != preferences.SourceLanguage)
                        {
                            _recognized =
                                await ImageTranslationService.RecognizeAsync(_png, preferences, _recognition, _ocr,
                                    token);
                            _recognizedLanguage = preferences.SourceLanguage;
                            _sourceImageText = preferences.Mode == TranslationMode.ImageToImage
                                ? ImageTextSelectionBuilder.FromOcr(_recognized)
                                : ImageTextSelectionDocument.Empty;
                            _updating = true;
                            _sourceText.Text = _recognized.Text;
                            _updating = false;
                        }

                        var recognized = _recognized;
                        var sourceText = _sourceText.Text ?? "";
                        if (string.IsNullOrWhiteSpace(sourceText))
                        {
                            SetStatus("RecognitionEmpty");
                            continue;
                        }

                        if (preferences.Mode == TranslationMode.ImageToImage &&
                            recognized.Lines.All(line => string.IsNullOrWhiteSpace(line.Text)))
                        {
                            SetStatus("TranslationNoTextRegions");
                            continue;
                        }

                        if (sourceText != recognized.Text)
                        {
                            var lines = sourceText.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
                            var regions = recognized.Lines.Where(line => !string.IsNullOrWhiteSpace(line.Text))
                                .ToArray();
                            if (preferences.Mode == TranslationMode.ImageToImage && lines.Length != regions.Length)
                                throw new InvalidDataException(
                                    "Edited image text must retain its text region boundaries.");
                            recognized = recognized with
                            {
                                Text = sourceText, Lines = preferences.Mode == TranslationMode.ImageToImage
                                    ? [.. regions.Select((line, index) => line with { Text = lines[index] })]
                                    : recognized.Lines,
                            };
                        }

                        var document =
                            await new ImageTranslationService().TranslateRecognizedAsync(_png, recognized, preferences,
                                apiKey, token);
                        byte[]? translatedPng = null;
                        var translatedText = ImageTextSelectionDocument.Empty;
                        if (preferences.Mode == TranslationMode.ImageToImage)
                        {
                            SetStatus("TranslationPreparingImage");
                            var background = await Task.Run(() => ImageTranslationBackground.Create(document, token),
                                token);
                            var rendered = await ImageTranslationRenderer.RenderAsync(document, background, token);
                            translatedPng = rendered.Png;
                            translatedText = rendered.Text;
                        }

                        token.ThrowIfCancellationRequested();
                        _document = document;
                        _updating = true;
                        _targetLanguage.LanguageTag = document.TargetLanguage;
                        _updating = false;
                        _swap.IsEnabled = CanSwapLanguages;
                        _translatedText.Text = document.TranslatedText;
                        _translatedPng = translatedPng;
                        _translatedImageText = translatedText;
                        var old = _translatedBitmap;
                        _translatedBitmap = translatedPng is null ? null : new(new MemoryStream(translatedPng, false));
                        RefreshImage();
                        old?.Dispose();
                        _speech.Refresh();
                        SetStatus("TranslationCompleted");
                        Logger.ZLogInformation(
                            $"Image translation completed: mode={preferences.Mode}, blocks={document.Blocks.Count}");
                    }
                    catch (OperationCanceledException) when (work.IsCancellationRequested)
                    {
                        if (!_lifetime.IsCancellationRequested && !_pending) SetStatus("TranslationCanceled");
                    }
                    catch (Exception error)
                    {
                        Logger.ZLogWarning(
                            $"Image translation failed: exceptionType={error.GetType().Name}, hresult={error.HResult}");
                        if (!_lifetime.IsCancellationRequested)
                        {
                            var key = readingCredential
                                ? "TranslationCredentialUnavailable"
                                : error switch
                                {
                                    TranslationException
                                    {
                                        Kind: TranslationFailureKind.Http,
                                    } => "TranslationHttpError",
                                    TranslationException
                                    {
                                        Kind: TranslationFailureKind.ResponseTooLarge,
                                    } => "TranslationResponseTooLarge",
                                    TranslationException => "TranslationInvalidResponse",
                                    TimeoutException => "TranslationTimedOut",
                                    ArgumentException => "TranslationConfigureService",
                                    InvalidDataException when preferences.Mode == TranslationMode.ImageToImage =>
                                        "TranslationImageUnavailable",
                                    _ => "TranslationFailed",
                                };
                            if (!readingCredential && error is TranslationException
                                {
                                    Kind: TranslationFailureKind.Http,
                                } failure)
                                SetStatus(key, (int?)failure.StatusCode ?? 0);
                            else SetStatus(key);
                            _run.Bind(ContentProperty, LiveText.Resource("TranslationRetry"));
                        }
                    }
                    finally
                    {
                        _updating = false;
                        _work = null;
                    }
                }
            }
            finally
            {
                _loopRunning = false;
                if (!_lifetime.IsCancellationRequested) SetBusy(false);
            }
        }

        private async Task CopyTextAsync(string? text)
        {
            if (string.IsNullOrEmpty(text) || _lifetime.IsCancellationRequested) return;
            try
            {
                var clipboard = Clipboard ?? throw new InvalidOperationException("Clipboard is unavailable.");
                await clipboard.SetTextAsync(text);
                await clipboard.FlushAsync();
            }
            catch (Exception error)
            {
                ReportClipboardError(error);
            }
        }

        private async Task CopyImageAsync()
        {
            if (_lifetime.IsCancellationRequested) return;
            var png = _originalImage ? _png.ToArray() : _translatedPng;
            if (png is null) return;
            try
            {
                var clipboard = Clipboard ?? throw new InvalidOperationException("Clipboard is unavailable.");
                await ClipboardImage.CopyAsync(clipboard, png, _lifetime.Token);
            }
            catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
            {
            }
            catch (Exception error)
            {
                ReportClipboardError(error);
            }
        }

        private void ReportClipboardError(Exception error)
        {
            Logger.ZLogWarning(
                $"Image translation clipboard failed: exceptionType={error.GetType().Name}, hresult={error.HResult}");
            if (!_lifetime.IsCancellationRequested) SetStatus("ClipboardFailed");
        }
    }
}
