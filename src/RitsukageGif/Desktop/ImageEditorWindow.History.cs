namespace RitsukageGif.Desktop
{
    internal sealed partial class ImageEditorWindow
    {
        private const int DocumentHistoryStates = 32;
        private const long DocumentHistoryBytes = 512L * 1024 * 1024;
        private readonly List<ImageEditorDocumentState> _documentRedo = [];
        private readonly List<ImageEditorDocumentState> _documentUndo = [];
        private readonly HashSet<ScreenshotAnnotationSession> _outputSessions = [];
        private readonly HashSet<ScreenshotAnnotationSession> _serviceSessions = [];
        private bool _documentChanging;
        private ImageEditorHistoryStore? _historyStore;
        private bool _retainDocumentPreview;
        private bool _servicesConnected;
        private ImageEditorHistoryStore HistoryStore => _historyStore ??= new();

        private void AttachDocumentSession()
        {
            Session.Changed += SessionChanged;
            Session.TextEditStateChanged += QueuePreview;
            if (_outputSessions.Add(Session)) Session.CompleteRequested += action => _ = CompleteAsync(action);
            Session.DocumentCanUndo = () => _documentUndo.Count > 0;
            Session.DocumentCanRedo = () => _documentRedo.Count > 0;
            Session.DocumentUndo = () => _ = RunAsync(() => SwitchDocumentHistoryAsync(_documentUndo, _documentRedo));
            Session.DocumentRedo = () => _ = RunAsync(() => SwitchDocumentHistoryAsync(_documentRedo, _documentUndo));
            Session.DocumentBranchChanged = ClearDocumentRedo;
        }

        private void ClearDocumentRedo()
        {
            foreach (var state in _documentRedo) ReleaseHistoryState(state);
            _documentRedo.Clear();
        }

        private void ReleaseHistoryState(ImageEditorDocumentState state)
        {
            _serviceSessions.Remove(state.Session);
            _outputSessions.Remove(state.Session);
            state.Session.Dispose();
            _historyStore?.Delete(state.Stored);
        }

        private void TrimDocumentHistory()
        {
            while (_documentUndo.Count + _documentRedo.Count > 1 &&
                   (_documentUndo.Count + _documentRedo.Count > DocumentHistoryStates ||
                    _documentUndo.Concat(_documentRedo).Sum(state => state.Stored.ByteLength) > DocumentHistoryBytes ||
                    RetainedHistoryAssetBytes() > ScreenshotAnnotationSession.ImageHistoryAssetBudget))
            {
                var list = _documentUndo.Count > 0 ? _documentUndo : _documentRedo;
                var oldest = list[0];
                list.RemoveAt(0);
                ReleaseHistoryState(oldest);
            }
        }

        private long RetainedHistoryAssetBytes()
        {
            var active = Session.ImageHistoryAssets.ToHashSet();
            return _documentUndo.Concat(_documentRedo).SelectMany(state => state.Session.ImageHistoryAssets)
                .Where(image => !active.Contains(image)).Distinct().Sum(image =>
                    (long)image.Bitmap.PixelSize.Width * image.Bitmap.PixelSize.Height * 4 +
                    (image.Encoded?.LongLength ?? 0));
        }

        private async Task ApplyStripAsync()
        {
            if (_closed || _outputBusy || _documentChanging || _strip.Axis is not { } axis ||
                !_strip.HasSelection) return;
            var start = _strip.FirstPixel;
            var count = _strip.PixelCount;
            var extent = axis == ImageStripAxis.Rows ? _sourceBounds.Height : _sourceBounds.Width;
            if (count <= 0 || count >= extent) return;
            BeginDocumentTransition();
            ImageEditorStoredDocument? backup = null;
            ScreenshotAnnotationSession? nextSession = null;
            var entered = false;
            try
            {
                var token = _lifetime.Token;
                await _previewGate.WaitAsync(token);
                entered = true;
                backup = await HistoryStore.WriteAsync(_sourcePng, _hdrSource, token);
                var png = await Session.ComposeAsync(_sourceBounds, _sourcePng, token);
                var hdr = _hdrSource;
                if (hdr is not null && Session.HasAnnotations)
                    hdr = await Session.ComposeHdrAsync(_sourceBounds, hdr, token: token);
                var result = await Task.Run(() => ImageStripService.Delete(png, hdr, axis, start, count, token), token);
                var image = await Task.Run(() => PinnedImageContent.Decode(result.Png, token), token);
                token.ThrowIfCancellationRequested();
                nextSession = Session.CreateImageDocumentSession();
                var view = await PrepareDocumentViewAsync(image, result.Hdr, nextSession, token);
                var previous = Session;
                Session.SetExportState(false);
                DetachDocumentView();
                previous.SuspendImageSourceForHistory();
                ClearDocumentRedo();
                _documentUndo.Add(new(previous, backup));
                backup = null;
                InstallDocumentView(view);
                nextSession = null;
                TrimDocumentHistory();
                Logger.ZLogInformation(
                    $"Image editor strip deleted. Axis={axis.ToString()} First={start} Count={count} Width={image.Width} Height={image.Height}");
            }
            finally
            {
                if (backup is not null) _historyStore?.Delete(backup);
                nextSession?.Dispose();
                if (entered) _previewGate.Release();
                EndDocumentTransition();
            }
        }

        private async Task SwitchDocumentHistoryAsync(List<ImageEditorDocumentState> from,
            List<ImageEditorDocumentState> to)
        {
            if (_closed || _outputBusy || _documentChanging || from.Count == 0) return;
            BeginDocumentTransition();
            ImageEditorStoredDocument? backup = null;
            var target = from[^1];
            var entered = false;
            var restored = false;
            try
            {
                var token = _lifetime.Token;
                await _previewGate.WaitAsync(token);
                entered = true;
                var result = await HistoryStore.ReadAsync(target.Stored, token);
                var image = await Task.Run(() => PinnedImageContent.Decode(result.Png, token), token);
                backup = await HistoryStore.WriteAsync(_sourcePng, _hdrSource, token);
                token.ThrowIfCancellationRequested();
                target.Session.RestoreImageDocumentControls(Session);
                var view = await PrepareDocumentViewAsync(image, result.Hdr, target.Session, token);
                var previous = Session;
                Session.SetExportState(false);
                DetachDocumentView();
                previous.SuspendImageSourceForHistory();
                from.RemoveAt(from.Count - 1);
                to.Add(new(previous, backup));
                backup = null;
                InstallDocumentView(view);
                restored = true;
                HistoryStore.Delete(target.Stored);
                TrimDocumentHistory();
                Logger.ZLogInformation(
                    $"Image editor document history restored. Width={image.Width} Height={image.Height}");
            }
            finally
            {
                if (backup is not null) _historyStore?.Delete(backup);
                if (!restored && !ReferenceEquals(target.Session, Session))
                    target.Session.SuspendImageSourceForHistory();
                if (entered) _previewGate.Release();
                EndDocumentTransition();
            }
        }

        private void BeginDocumentTransition()
        {
            _outputBusy = true;
            _documentChanging = true;
            Session.SetExportState(true);
            _previewWork?.Cancel();
            _hdrPreviewWork?.Cancel();
            RefreshDocumentButtons();
        }

        private void EndDocumentTransition()
        {
            _outputBusy = false;
            _documentChanging = false;
            if (_closed) return;
            Session.SetExportState(false);
            SessionChanged();
            PositionStripButton();
        }

        private void DisposeDocumentHistory()
        {
            foreach (var state in _documentUndo.Concat(_documentRedo)) state.Session.Dispose();
            _documentUndo.Clear();
            _documentRedo.Clear();
            _serviceSessions.Clear();
            _outputSessions.Clear();
            _historyStore?.Dispose();
            _historyStore = null;
        }
    }
}
