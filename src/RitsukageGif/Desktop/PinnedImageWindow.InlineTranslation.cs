using System.Runtime.ExceptionServices;
using Avalonia.Threading;

namespace RitsukageGif.Desktop
{
    // ReSharper disable once ClassCannotBeInstantiated
    internal sealed partial class PinnedImageWindow
    {
        private PinnedImageTranslationSession<PinnedImageTranslationCache>? _inlineTranslation;
        private Task<Exception?> _translationPresentation = Task.FromResult<Exception?>(null);
        private long _translationRasterRevision;

        private PinnedImageTranslationSession<PinnedImageTranslationCache> InlineTranslation
        {
            get
            {
                if (_inlineTranslation is { } current) return current;
                var session = new PinnedImageTranslationSession<PinnedImageTranslationCache>(
                    cache => cache.TargetLanguage, _lifetime.Token);
                session.Changed += InlineTranslationChanged;
                session.Failed += ReportInlineTranslationError;
                _inlineTranslation = session;
                return session;
            }
        }

        private Task ToggleInlineTranslationAsync()
        {
            if (_closed) return Task.CompletedTask;
            var session = InlineTranslation;
            if (session.Enabled) session.Disable();
            else
                _ = StartInlineTranslation(DefaultInlineTranslationTarget(session));
            return Task.CompletedTask;
        }

        internal void StartScreenshotTranslation()
        {
            if (_closed || !CanUseInlineTranslation) return;
            if (!HasInlineTranslationBackend)
            {
                Presentation.ShowMessage(UiText.Get("TranslationConfigureService"));
                return;
            }

            var session = InlineTranslation;
            _ = StartInlineTranslation(DefaultInlineTranslationTarget(session));
        }

        internal async Task<bool> TranslationAutomationAsync(bool? enabled, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            ObjectDisposedException.ThrowIf(_closed, this);
            if (!CanUseInlineTranslation)
                throw new NotSupportedException("This pin does not support inline translation.");
            if (enabled is null) return _activeTranslatedRaster is not null;

            var session = InlineTranslation;
            if (enabled.Value)
            {
                if (!session.Enabled && !HasInlineTranslationBackend && session.Cached is null)
                    throw new InvalidOperationException(UiText.Get("TranslationConfigureService"));
                var pending = session.Enabled
                    ? session.Completion
                    : StartInlineTranslation(DefaultInlineTranslationTarget(session));
                if (await pending.WaitAsync(token) is { } error)
                    ExceptionDispatchInfo.Throw(error);
                ObjectDisposedException.ThrowIf(_closed, this);
            }
            else if (session.Enabled)
            {
                session.Disable();
            }

            while (true)
            {
                var presentation = _translationPresentation;
                var failure = await presentation.WaitAsync(token);
                ObjectDisposedException.ThrowIf(_closed, this);
                if (!ReferenceEquals(presentation, _translationPresentation)) continue;
                if (failure is not null) ExceptionDispatchInfo.Throw(failure);
                break;
            }

            if (!CanUseInlineTranslation) throw new OperationCanceledException(token);
            return _activeTranslatedRaster is not null;
        }

        private string DefaultInlineTranslationTarget(
            PinnedImageTranslationSession<PinnedImageTranslationCache> session)
        {
            return session.TargetLanguage.Length == 0
                ? TranslationPreferencesProvider().Normalize().TargetLanguage
                : session.TargetLanguage;
        }

        private Task<Exception?> StartInlineTranslation(string target)
        {
            var sourcePng = _png;
            var sourceHdr = _hdrSource;
            var preferences = TranslationPreferencesProvider().Normalize() with { TargetLanguage = target };
            var recognition = RecognitionPreferencesProvider();
            var ocr = TextRecognitionServiceProvider?.Invoke() ?? DefaultTextRecognition;
            return InlineTranslation.Enable(target, async (_, token) =>
            {
                var key = preferences.UseApiKey
                    ? await CredentialVault.ReadAsync(preferences.CredentialKey, token) ?? ""
                    : "";
                if (preferences.UseApiKey && key.Length == 0)
                    throw new InvalidOperationException(UiText.Get("TranslationCredentialMissing"));
                return await PinnedImageInlineTranslationService.TranslateAsync(sourcePng, sourceHdr,
                    preferences, recognition, ocr, key, token);
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
            var revision = ++_translationRasterRevision;
            var raster = session.Displayed?.Raster;
            _status.IsVisible = session is { Enabled: true, Status: PinnedImageTranslationStatus.Pending };
            if (_status.IsVisible) _status.Text = UiText.Get("TranslationWorking");
            StateChanged?.Invoke(this, EventArgs.Empty);
            _translationPresentation = ApplyInlineTranslationRasterAsync(revision, raster);
        }

        private async Task<Exception?> ApplyInlineTranslationRasterAsync(long revision,
            PinnedImageTranslationRaster? raster)
        {
            try
            {
                await SerializeRasterUpdateAsync(async () =>
                {
                    if (_closed || revision != _translationRasterRevision ||
                        ReferenceEquals(raster, _activeTranslatedRaster)) return;
                    await RebuildRasterCoreAsync(_colorMatrixState, raster, _lifetime.Token);
                });
            }
            catch (OperationCanceledException error) when (_closed || _lifetime.IsCancellationRequested)
            {
                return error;
            }
            catch (Exception error)
            {
                ReportInlineTranslationError(error);
                return error;
            }

            return null;
        }

        private void ReportInlineTranslationError(Exception error)
        {
            if (!Dispatcher.UIThread.CheckAccess())
            {
                Dispatcher.UIThread.Post(() => ReportInlineTranslationError(error));
                return;
            }

            PinLogger.ZLogError(error, $"Pinned image inline translation failed.");
            if (_closed) return;
            _status.Text = error.Message;
            _status.IsVisible = true;
        }

        private PinnedImageTranslationState? CaptureInlineTranslationState()
        {
            return _inlineTranslation is { } session
                ? new(session.TargetLanguage, session.Enabled,
                    session.Status == PinnedImageTranslationStatus.Empty, session.Cached)
                : null;
        }

        private void RestoreInlineTranslationState(PinnedImageTranslationState? state)
        {
            if (state is null) return;
            var session = InlineTranslation;
            session.Changed -= InlineTranslationChanged;
            try
            {
                session.Restore(state.TargetLanguage, state.Enabled, state.Cache,
                    state.Empty);
                _activeTranslatedRaster = session.Displayed?.Raster;
                _translatedTextSelection = new(_activeTranslatedRaster?.Text ?? ImageTextSelectionDocument.Empty);
            }
            finally
            {
                session.Changed += InlineTranslationChanged;
            }
        }
    }
}
