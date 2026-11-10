using Avalonia.Threading;

namespace RitsukageGif.Desktop
{
    // ReSharper disable once ClassCannotBeInstantiated
    internal sealed partial class PinnedImageWindow
    {
        private PinnedImageTranslationSession<PinnedImageTranslationCache>? _inlineTranslation;
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
                StartInlineTranslation(session.TargetLanguage.Length == 0
                    ? TranslationPreferencesProvider().Normalize().TargetLanguage
                    : session.TargetLanguage);
            return Task.CompletedTask;
        }

        private void StartInlineTranslation(string target)
        {
            var sourcePng = _png;
            var sourceHdr = _hdrSource;
            var preferences = TranslationPreferencesProvider().Normalize() with { TargetLanguage = target };
            var recognition = RecognitionPreferencesProvider();
            var ocr = TextRecognitionServiceProvider?.Invoke() ?? DefaultTextRecognition;
            InlineTranslation.Enable(target, async (_, token) =>
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
            _ = ApplyInlineTranslationRasterAsync(revision, raster);
        }

        private async Task ApplyInlineTranslationRasterAsync(long revision, PinnedImageTranslationRaster? raster)
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
            catch (OperationCanceledException) when (_closed || _lifetime.IsCancellationRequested)
            {
            }
            catch (Exception error)
            {
                ReportInlineTranslationError(error);
            }
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
