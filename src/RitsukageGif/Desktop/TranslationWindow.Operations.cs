using Avalonia.Input.Platform;

namespace RitsukageGif.Desktop
{
    // ReSharper disable once ClassCannotBeInstantiated
    internal sealed partial class TranslationWindow
    {
        private static readonly ILogger Logger = LogHub.GetLogger("Desktop.TranslationWindow");

        private void StartTranslation()
        {
            if (_work is not null || _lifetime.IsCancellationRequested) return;
            var source = _sourceLanguage.LanguageTag.Trim();
            var target = _targetLanguage.LanguageTag.Trim();
            var fallback = _fallbackLanguage.LanguageTag.Trim();
            if (!source.Equals("auto", StringComparison.OrdinalIgnoreCase) &&
                source.Equals(target, StringComparison.OrdinalIgnoreCase) && fallback.Length > 0)
                _targetLanguage.LanguageTag = fallback;
            _operation = TranslateAsync();
        }

        private async Task TranslateAsync()
        {
            using var work = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            _work = work;
            var revision = ++_revision;
            SetBusy(true);
            _fallback.IsVisible = false;
            SetStatus("TranslationWorking");
            var source = _sourceLanguage.LanguageTag.Trim();
            var target = _targetLanguage.LanguageTag.Trim();
            var fallback = _fallbackLanguage.LanguageTag.Trim();
            var request = new TranslationRequest(_sourceText.Text ?? "", target, source);
            var readingCredential = false;
            try
            {
                if (string.IsNullOrWhiteSpace(_preferences.Endpoint))
                    throw new ArgumentException("Translation service endpoint is not configured.");
                var apiKey = "";
                if (_preferences.UseApiKey)
                {
                    readingCredential = true;
                    var stored = await CredentialVault.ReadAsync(_preferences.CredentialKey, work.Token);
                    readingCredential = false;
                    if (stored is null)
                    {
                        SetStatus("TranslationCredentialMissing");
                        return;
                    }

                    apiKey = stored;
                }

                var result = await new TranslationService().TranslateAsync(_preferences.CreateOptions(apiKey), request,
                    work.Token);
                work.Token.ThrowIfCancellationRequested();
                if (revision != _revision || _lifetime.IsCancellationRequested) return;
                _detectedSpeechLanguage = result.DetectedSourceLanguage;
                _translatedText.Text = result.Text;
                SetStatus("TranslationCompleted");
                _fallback.IsVisible = fallback.Length > 0 &&
                                      !fallback.Equals(target, StringComparison.OrdinalIgnoreCase) &&
                                      result.DetectedSourceLanguage?.Equals(target,
                                          StringComparison.OrdinalIgnoreCase) == true;
            }
            catch (OperationCanceledException) when (work.IsCancellationRequested)
            {
                if (!_lifetime.IsCancellationRequested) SetStatus("TranslationCanceled");
            }
            catch (Exception error)
            {
                if (_lifetime.IsCancellationRequested) return;
                Logger.ZLogWarning(error, $"Translation window operation failed: exceptionType={error.GetType().Name}");
                var key = readingCredential
                    ? "TranslationCredentialUnavailable"
                    : error switch
                    {
                        TranslationException { Kind: TranslationFailureKind.Http } => "TranslationHttpError",
                        TranslationException { Kind: TranslationFailureKind.ResponseTooLarge } =>
                            "TranslationResponseTooLarge",
                        TranslationException => "TranslationInvalidResponse",
                        TimeoutException => "TranslationTimedOut",
                        ArgumentException => "TranslationConfigureService",
                        _ => "TranslationFailed",
                    };
                if (!readingCredential && error is TranslationException { Kind: TranslationFailureKind.Http } failure)
                    SetStatus(key, (int?)failure.StatusCode ?? 0);
                else SetStatus(key);
                _run.Bind(ContentProperty, LiveText.Resource("TranslationRetry"));
            }
            finally
            {
                if (ReferenceEquals(_work, work)) _work = null;
                if (!_lifetime.IsCancellationRequested) SetBusy(false);
            }
        }

        private async Task CopyAsync()
        {
            if (_work is not null || string.IsNullOrEmpty(_translatedText.Text)) return;
            try
            {
                var clipboard = Clipboard ?? throw new InvalidOperationException(UiText.Get("ClipboardFailed"));
                await clipboard.SetTextAsync(_translatedText.Text);
                await clipboard.FlushAsync();
            }
            catch (Exception error)
            {
                Logger.ZLogWarning(error, $"Translation clipboard copy failed: exceptionType={error.GetType().Name}");
                if (!_lifetime.IsCancellationRequested) SetStatus("ClipboardFailed");
            }
        }
    }
}
