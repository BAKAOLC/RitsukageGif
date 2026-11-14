using System.Runtime.ExceptionServices;
using Avalonia.Threading;

namespace RitsukageGif.Desktop
{
    internal sealed partial class PinnedDocumentWindow
    {
        private PinnedImageTranslationSession<PinnedDocumentTranslationState>? _inlineTranslation;
        internal ITextTranslationService? TextTranslationService { get; init; }

        private PinnedDocumentTranslationState? DisplayedTranslation { get; set; }

        private PinnedImageTranslationSession<PinnedDocumentTranslationState> InlineTranslation
        {
            get
            {
                if (_inlineTranslation is { } existing) return existing;
                var session = new PinnedImageTranslationSession<PinnedDocumentTranslationState>(
                    cache => cache.TargetLanguage, _lifetime.Token);
                session.Changed += InlineTranslationChanged;
                session.Failed += ReportInlineTranslationError;
                _inlineTranslation = session;
                return session;
            }
        }

        private Task ToggleInlineTranslationAsync()
        {
            var session = InlineTranslation;
            if (session.Enabled)
            {
                session.Disable();
                return Task.CompletedTask;
            }

            _ = StartInlineTranslation(session);
            return Task.CompletedTask;
        }

        internal async Task<bool> TranslationAutomationAsync(bool? enabled, CancellationToken token)
        {
            if (Document is not (PinnedTextDocument or PinnedHtmlDocument))
                throw new NotSupportedException("This pin does not support inline translation.");
            token.ThrowIfCancellationRequested();
            ObjectDisposedException.ThrowIf(_closed, this);
            if (enabled is null) return DisplayedTranslation is not null;

            var session = InlineTranslation;
            if (enabled.Value)
            {
                var pending = session.Enabled ? session.Completion : StartInlineTranslation(session);
                if (await pending.WaitAsync(token) is { } error)
                    ExceptionDispatchInfo.Throw(error);
                ObjectDisposedException.ThrowIf(_closed, this);
            }
            else if (session.Enabled)
            {
                session.Disable();
            }

            return DisplayedTranslation is not null;
        }

        private Task<Exception?> StartInlineTranslation(
            PinnedImageTranslationSession<PinnedDocumentTranslationState> session)
        {
            var preferences = TranslationPreferencesProvider() with { };
            if (ScriptTextTranslationService.ConfigurationFailure(preferences) is not null)
                throw new InvalidOperationException(UiText.Get("TranslationConfigureService"));

            var target = session.Cached?.TargetLanguage ?? preferences.TargetLanguage;
            var service = TextTranslationService ?? new ScriptTextTranslationService(() => preferences);
            return session.Enable(target, async (language, token) =>
            {
                var source = Document.PlainText;
                if (string.IsNullOrWhiteSpace(source) && Document is PinnedHtmlDocument html)
                    source = PinnedHtmlView.Parse(html).PlainText;
                if (string.IsNullOrWhiteSpace(source))
                    throw new InvalidOperationException(UiText.Get("PinnedImageNoTextDetected"));
                if (source.Length > TranslationService.MaximumInputLength)
                    throw new InvalidOperationException(UiText.Get("PinnedTextTooLarge"));
                var result = await service.TranslateAsync(source, new() { TargetLanguage = language }, token);
                var cache = new PinnedDocumentTranslationState(language, result.Text, true);
                if (!cache.IsValid)
                    throw new InvalidOperationException(UiText.Get("TranslationInvalidResponse"));
                return cache;
            });
        }

        private void InlineTranslationChanged()
        {
            if (!Dispatcher.UIThread.CheckAccess())
            {
                Dispatcher.UIThread.Post(InlineTranslationChanged);
                return;
            }

            if (_closed) return;
            var session = _inlineTranslation!;
            if (!ReferenceEquals(DisplayedTranslation, session.Displayed))
            {
                _annotationScene?.CommitTextEdit();
                _annotationScene?.CancelGesture();
                DisplayedTranslation = session.Displayed;
                RebuildContent();
            }

            _status.IsVisible = session is { Enabled: true, Status: PinnedImageTranslationStatus.Pending };
            if (_status.IsVisible) _status.Text = UiText.Get("TranslationWorking");
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        private void ReportInlineTranslationError(Exception error)
        {
            if (!Dispatcher.UIThread.CheckAccess())
            {
                Dispatcher.UIThread.Post(() => ReportInlineTranslationError(error));
                return;
            }

            if (_closed) return;
            Logger.ZLogWarning(
                $"Pinned document translation failed. ErrorType={error.GetType().Name} Code={error.HResult}");
            _status.Text = error switch
            {
                ArgumentException => UiText.Get("TranslationConfigureService"),
                TranslationException { Kind: TranslationFailureKind.Http } failure =>
                    UiText.Format("TranslationHttpError", (int?)failure.StatusCode ?? 0),
                TranslationException { Kind: TranslationFailureKind.ResponseTooLarge } =>
                    UiText.Get("TranslationResponseTooLarge"),
                TranslationException => UiText.Get("TranslationInvalidResponse"),
                TimeoutException => UiText.Get("TranslationTimedOut"),
                _ => error.Message,
            };
            _status.IsVisible = true;
        }

        private PinnedDocumentTranslationState? CaptureInlineTranslationState()
        {
            return _inlineTranslation is { Cached: { } cache } session
                ? cache with { Enabled = session.Enabled }
                : null;
        }

        private bool RestoreInlineTranslationState(PinnedDocumentTranslationState? state)
        {
            if (state is null && _inlineTranslation is null) return false;
            var session = InlineTranslation;
            session.Changed -= InlineTranslationChanged;
            try
            {
                session.Restore(state?.TargetLanguage ?? "", state?.Enabled == true, state, false);
                if (ReferenceEquals(DisplayedTranslation, session.Displayed)) return false;
                DisplayedTranslation = session.Displayed;
                return true;
            }
            finally
            {
                session.Changed += InlineTranslationChanged;
            }
        }
    }
}
