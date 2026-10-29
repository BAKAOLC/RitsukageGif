using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    internal sealed partial class ScreenshotAnnotationSession
    {
        private CancellationTokenSource? _privacyCancel;
        private PrivacyCandidate[] _privacyCandidates = [];
        private long _privacyRequest;
        private bool[] _privacySelected = [];
        private PixelRect _privacySelection;
        private CancellationTokenSource? _privacyWork;

        public bool IsPrivacyDetectionActive { get; private set; }
        public bool PrivacyDetectionPending { get; private set; }
        public IReadOnlyList<PrivacyCandidate> PrivacyCandidates => _privacyCandidates;
        public CancellationToken PrivacyDetectionToken => _privacyCancel?.Token ?? new CancellationToken(true);
        public int SelectedPrivacyCandidateCount => _privacySelected.Count(selected => selected);

        public bool CanApplyPrivacyMosaic => IsPrivacyDetectionActive && !PrivacyDetectionPending && !IsBusy &&
                                             SelectedPrivacyCandidateCount > 0;

        public event Action<long>? PrivacyDetectionRequested;

        public bool RequestPrivacyDetection()
        {
            if (_disposed || !CanAnnotate || IsBusy || IsDrawing || Selection.IsEmpty ||
                PrivacyDetectionRequested is null) return false;
            Tool = ScreenshotTool.Mosaic;
            ClearAnnotationSelection();
            IsPrivacyDetectionActive = true;
            PrivacyDetectionPending = true;
            _privacySelection = Selection;
            _privacyCancel = CancellationTokenSource.CreateLinkedTokenSource(_cancel.Token);
            var request = ++_privacyRequest;
            Error = null;
            Logger.ZLogInformation(
                $"Privacy detection requested. SessionId={_logSessionId} RequestId={request} Width={Selection.Width} Height={Selection.Height}");
            Changed?.Invoke();
            PrivacyDetectionRequested.Invoke(request);
            return true;
        }

        public bool SetPrivacyCandidates(long request, IReadOnlyList<PrivacyCandidate> candidates,
            bool autoSelectSensitive)
        {
            if (!AcceptsPrivacyResult(request) || !PrivacyDetectionPending) return false;
            PrivacyCandidate[] accepted =
            [
                .. candidates.Where(candidate => Enum.IsDefined(candidate.Kind))
                    .Select(candidate => candidate with { Bounds = candidate.Bounds.Intersect(_privacySelection) })
                    .Where(candidate => !candidate.Bounds.IsEmpty).Take(1024),
            ];
            _privacyCandidates = accepted;
            _privacySelected = [.. accepted.Select(candidate => autoSelectSensitive && candidate.IsSensitive)];
            PrivacyDetectionPending = false;
            Logger.ZLogInformation(
                $"Privacy detection candidates accepted. SessionId={_logSessionId} RequestId={request} Candidates={accepted.Length} Selected={SelectedPrivacyCandidateCount}");
            Changed?.Invoke();
            return true;
        }

        public void FailPrivacyDetection(long request, string? error)
        {
            if (!AcceptsPrivacyResult(request) || !PrivacyDetectionPending) return;
            PrivacyDetectionPending = false;
            Error = error;
            Logger.ZLogWarning($"Privacy detection failed. SessionId={_logSessionId} RequestId={request}");
            Changed?.Invoke();
        }

        public void TogglePrivacyCandidate(PixelPoint point)
        {
            if (!IsPrivacyDetectionActive || PrivacyDetectionPending || IsBusy) return;
            var candidateIndex = -1;
            var smallestArea = long.MaxValue;
            for (var index = 0; index < _privacyCandidates.Length; index++)
            {
                var bounds = _privacyCandidates[index].Bounds;
                var area = (long)bounds.Width * bounds.Height;
                if (!bounds.Contains(point) || area > smallestArea) continue;
                smallestArea = area;
                candidateIndex = index;
            }

            if (candidateIndex < 0) return;
            var selectedBounds = _privacyCandidates[candidateIndex].Bounds;
            var selected = true;
            for (var index = 0; index < _privacyCandidates.Length; index++)
                if (_privacyCandidates[index].Bounds == selectedBounds && _privacySelected[index])
                    selected = false;
            for (var index = 0; index < _privacyCandidates.Length; index++)
                if (_privacyCandidates[index].Bounds == selectedBounds)
                    _privacySelected[index] = selected;
            Changed?.Invoke();
        }

        public async Task<bool> ApplyPrivacyMosaicAsync()
        {
            if (!CanApplyPrivacyMosaic) return false;
            var candidates = _privacyCandidates.Where((_, index) => _privacySelected[index])
                .DistinctBy(candidate => candidate.Bounds).ToArray();
            if (_annotations.Count + candidates.Length > 1024 ||
                _annotations.Concat(_retained).Distinct().Sum(annotation => annotation.Points.Count) +
                candidates.Length * 2 > 262144)
            {
                Error = UiText.Get("ScreenshotAnnotationLimit");
                Changed?.Invoke();
                return false;
            }

            var request = _privacyRequest;
            using var work = CancellationTokenSource.CreateLinkedTokenSource(_cancel.Token, PrivacyDetectionToken);
            _privacyWork = work;
            IsBusy = true;
            Changed?.Invoke();
            var pending = new List<ScreenshotAnnotation>();
            try
            {
                var retainedBytes = _retained.SelectMany(annotation => annotation.ImageTiles)
                    .Sum(tile => (long)tile.Image.PixelSize.Width * tile.Image.PixelSize.Height * 4);
                foreach (var candidate in candidates)
                {
                    work.Token.ThrowIfCancellationRequested();
                    var bounds = candidate.Bounds;
                    retainedBytes += (long)bounds.Width * bounds.Height * 4;
                    if (retainedBytes > 128L * 1024 * 1024)
                        throw new InvalidOperationException(UiText.Get("RecordingTooLarge"));
                    var annotation = new ScreenshotAnnotation
                    {
                        Tool = ScreenshotTool.Mosaic, Color = Color, Thickness = Thickness,
                        MosaicSize = MosaicSize, Properties = Properties.Normalize(),
                    };
                    pending.Add(annotation);
                    annotation.Points.Add(new(bounds.X, bounds.Y));
                    annotation.Points.Add(new(bounds.Right, bounds.Bottom));
                    annotation.ImageTiles = await CreateImageEffectAsync(annotation, work.Token);
                }

                work.Token.ThrowIfCancellationRequested();
                if (!AcceptsPrivacyResult(request)) return false;
                SaveHistory();
                foreach (var annotation in pending)
                {
                    _annotations.Add(annotation);
                    _retained.Add(annotation);
                }

                pending.Clear();
                Logger.ZLogInformation(
                    $"Privacy mosaic applied. SessionId={_logSessionId} RequestId={request} Regions={candidates.Length}");
                CancelPrivacyDetection();
                Error = null;
                return true;
            }
            catch (OperationCanceledException) when (work.IsCancellationRequested)
            {
                Logger.ZLogDebug($"Privacy mosaic application canceled. SessionId={_logSessionId} RequestId={request}");
                return false;
            }
            catch (Exception error)
            {
                Logger.ZLogError(error,
                    $"Privacy mosaic application failed. SessionId={_logSessionId} RequestId={request}");
                if (AcceptsPrivacyResult(request)) Error = error.Message;
                return false;
            }
            finally
            {
                foreach (var annotation in pending) annotation.Dispose();
                if (ReferenceEquals(_privacyWork, work))
                {
                    _privacyWork = null;
                    IsBusy = false;
                }

                if (!_disposed) Changed?.Invoke();
            }
        }

        public void CancelPrivacyDetection()
        {
            if (!IsPrivacyDetectionActive) return;
            ++_privacyRequest;
            _privacyCancel?.Cancel();
            _privacyCancel?.Dispose();
            _privacyCancel = null;
            _privacyWork?.Cancel();
            IsPrivacyDetectionActive = false;
            PrivacyDetectionPending = false;
            _privacyCandidates = [];
            _privacySelected = [];
            if (!_disposed) Changed?.Invoke();
        }

        private bool AcceptsPrivacyResult(long request)
        {
            return !_disposed && IsPrivacyDetectionActive && request == _privacyRequest &&
                   Tool == ScreenshotTool.Mosaic && Selection == _privacySelection &&
                   _privacyCancel is { IsCancellationRequested: false };
        }

        private void RenderPrivacyCandidates(DrawingContext context)
        {
            if (!IsPrivacyDetectionActive || PrivacyDetectionPending) return;
            using var clip = context.PushClip(new(Selection.X, Selection.Y, Selection.Width, Selection.Height));
            var fill = new SolidColorBrush(Color.FromArgb(48, 0, 191, 255));
            var selected = new Pen(Brushes.DeepSkyBlue, 2);
            var unselected = new Pen(Brushes.White, 1, DashStyle.Dash);
            for (var index = 0; index < _privacyCandidates.Length; index++)
            {
                var bounds = _privacyCandidates[index].Bounds;
                context.DrawRectangle(_privacySelected[index] ? fill : null,
                    _privacySelected[index] ? selected : unselected,
                    new(bounds.X, bounds.Y, bounds.Width, bounds.Height));
            }
        }
    }
}
