using System.Collections.Immutable;
using Avalonia;
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
        private Guid _privacyAnnotationId;
        private Rect _privacyObjectBounds;
        private string? _privacyNotice;
        private string[] _privacyGroupKeys = [];
        private long _privacyCandidatesVersion;
        private ScreenshotAnnotation? _privacyRestoredAnnotation;
        private Rect _privacyRestoredBounds;
        private long _privacyRestoredVersion = -1;
        private AnnotationRegionSelectionIndex? _privacyRegionIndex;

        public bool IsPrivacyDetectionActive { get; private set; }
        public bool PrivacyDetectionPending { get; private set; }
        public CancellationToken PrivacyDetectionToken => _privacyCancel?.Token ?? new CancellationToken(true);
        private int SelectedPrivacyCandidateCount => _privacySelected.Count(selected => selected);

        public bool CanDetectPrivacy =>
            PrivacyDetectionRequested is not null && CanUseDrawingTool(ScreenshotTool.AutoMosaic);

        public event Action<long>? PrivacyDetectionRequested;

        public void SetPrivacyNotice(string message)
        {
            if (_disposed) return;
            _privacyNotice = message;
            Error = message;
            Changed?.Invoke();
        }

        private void RequestPrivacyDetection()
        {
            if (_disposed || !CanAnnotate || IsBusy || IsDrawing || !CanUseDrawingTool(ScreenshotTool.AutoMosaic) ||
                Selection.IsEmpty ||
                PrivacyDetectionRequested is null) return;
            var current = _selected is { AutoMosaicId: var id } && id != Guid.Empty ? _selected : null;
            ClearAnnotationSelection();
            Tool = ScreenshotTool.AutoMosaic;
            if (current is not null) LoadObjectStyle(current);
            _privacyAnnotationId = current?.AutoMosaicId ?? Guid.NewGuid();
            IsPrivacyDetectionActive = true;
            PrivacyDetectionPending = true;
            _privacySelection = Selection;
            _privacyObjectBounds =
                current?.Bounds ?? new Rect(Selection.X, Selection.Y, Selection.Width, Selection.Height);
            _privacyCancel = CancellationTokenSource.CreateLinkedTokenSource(_cancel.Token);
            var request = ++_privacyRequest;
            Error = null;
            _privacyNotice = null;
            if (current is { AutoMosaicCandidates.IsDefault: false })
            {
                _privacyCandidates =
                [
                    .. current.AutoMosaicCandidates.Select(candidate =>
                        ReprojectPrivacyCandidate(candidate, current.AutoMosaicCandidateBounds, current.Bounds)),
                ];
                _privacyGroupKeys = [.. _privacyCandidates.Select(candidate => NormalizePrivacyText(candidate.Text))];
                _privacySelected = new bool[_privacyCandidates.Length];
                _privacyCandidatesVersion++;
                PrivacyDetectionPending = false;
                RestorePrivacySelection(current);
                _selected = current;
                Changed?.Invoke();
                return;
            }

            Logger.ZLogInformation(
                $"Privacy detection requested. SessionId={_logSessionId} RequestId={request} Width={Selection.Width} Height={Selection.Height}");
            Changed?.Invoke();
            PrivacyDetectionRequested.Invoke(request);
        }

        public bool SetPrivacyCandidates(long request, IReadOnlyList<PrivacyCandidate> candidates,
            bool autoSelectSensitive)
        {
            if (!AcceptsPrivacyResult(request) || !PrivacyDetectionPending) return false;
            _privacyCandidates =
            [
                .. candidates.Where(candidate => Enum.IsDefined(candidate.Kind))
                    .Select(candidate => candidate with { Bounds = candidate.Bounds.Intersect(_privacySelection) })
                    .Where(candidate => !candidate.Bounds.IsEmpty),
            ];
            _privacyGroupKeys = [.. _privacyCandidates.Select(candidate => NormalizePrivacyText(candidate.Text))];
            _privacySelected =
                [.. _privacyCandidates.Select(candidate => autoSelectSensitive && candidate.IsSensitive)];
            _privacyCandidatesVersion++;
            PrivacyDetectionPending = false;
            var existing = _annotations.FirstOrDefault(annotation => annotation.AutoMosaicId == _privacyAnnotationId);
            if (existing is not null) RestorePrivacySelection(existing);
            Logger.ZLogInformation(
                $"Privacy detection candidates accepted. SessionId={_logSessionId} RequestId={request} Candidates={_privacyCandidates.Length} Selected={SelectedPrivacyCandidateCount}");
            _ = SynchronizePrivacyMosaicAsync(request);
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
            var candidateIndex = FindPrivacyCandidate(point);
            if (candidateIndex < 0) return;
            var targets = PrivacyHoverTargets(candidateIndex);
            var selected = !targets.Any(index => _privacySelected[index]);
            foreach (var index in targets) _privacySelected[index] = selected;
            _ = SynchronizePrivacyMosaicAsync(_privacyRequest);
        }

        private async Task SynchronizePrivacyMosaicAsync(long request)
        {
            if (!AcceptsPrivacyResult(request) || IsBusy) return;
            using var work = CancellationTokenSource.CreateLinkedTokenSource(_cancel.Token, PrivacyDetectionToken);
            _privacyWork = work;
            IsBusy = true;
            Changed?.Invoke();
            ScreenshotAnnotation? pending = null;
            try
            {
                var existing =
                    _annotations.FirstOrDefault(annotation => annotation.AutoMosaicId == _privacyAnnotationId);
                if (existing is null && _annotations.Count >= 1024)
                    throw new InvalidOperationException(UiText.Get("ScreenshotAnnotationLimit"));
                var regions = _privacyCandidates.Where((_, index) => _privacySelected[index])
                    .Select(candidate => candidate.Bounds).Distinct().Select(bounds => new Rect(
                        (bounds.X - _privacyObjectBounds.X) / _privacyObjectBounds.Width,
                        (bounds.Y - _privacyObjectBounds.Y) / _privacyObjectBounds.Height,
                        bounds.Width / _privacyObjectBounds.Width,
                        bounds.Height / _privacyObjectBounds.Height)).ToImmutableArray();
                var tool = Properties.MosaicMode == AnnotationMosaicMode.Blur
                    ? ScreenshotTool.Blur
                    : ScreenshotTool.Mosaic;
                pending = new()
                {
                    Tool = tool, Color = Color, Thickness = Thickness, MosaicSize = MosaicSize,
                    Properties = Properties.Normalize(), AutoMosaicId = _privacyAnnotationId,
                    AutoMosaicRegions = regions,
                    AutoMosaicCandidates = [.. _privacyCandidates], AutoMosaicCandidateBounds = _privacyObjectBounds,
                };
                pending.Points.Add(_privacyObjectBounds.TopLeft);
                pending.Points.Add(_privacyObjectBounds.BottomRight);
                var index = existing is null ? _annotations.Count : _annotations.IndexOf(existing);
                pending.ImageTiles = await CreateImageEffectAsync(pending, work.Token, index);
                work.Token.ThrowIfCancellationRequested();
                if (!AcceptsPrivacyResult(request)) return;
                SaveHistory();
                if (existing is null) _annotations.Add(pending);
                else _annotations[index] = pending;
                _retained.Add(pending);
                _selected = pending;
                _styleTool = ScreenshotTool.AutoMosaic;
                StoreToolStyle(ScreenshotTool.AutoMosaic);
                pending = null;
                Error = _privacyNotice;
                Logger.ZLogInformation(
                    $"Privacy mosaic regions updated. SessionId={_logSessionId} RequestId={request} Regions={regions.Length}");
            }
            catch (OperationCanceledException) when (work.IsCancellationRequested)
            {
                Logger.ZLogDebug($"Privacy mosaic update canceled. SessionId={_logSessionId} RequestId={request}");
            }
            catch (Exception error)
            {
                Logger.ZLogError(error, $"Privacy mosaic update failed. SessionId={_logSessionId} RequestId={request}");
                if (AcceptsPrivacyResult(request))
                {
                    Error = error.Message;
                    var existing =
                        _annotations.FirstOrDefault(annotation => annotation.AutoMosaicId == _privacyAnnotationId);
                    if (existing is not null) RestorePrivacySelection(existing, true);
                }
            }
            finally
            {
                pending?.Dispose();
                if (ReferenceEquals(_privacyWork, work))
                {
                    _privacyWork = null;
                    IsBusy = false;
                }

                if (!_disposed) Changed?.Invoke();
            }
        }

        private void RestorePrivacySelection(ScreenshotAnnotation annotation, bool force = false)
        {
            var bounds = annotation.Bounds;
            if (!force && ReferenceEquals(annotation, _privacyRestoredAnnotation) &&
                bounds == _privacyRestoredBounds && _privacyRestoredVersion == _privacyCandidatesVersion) return;
            if (_privacyRestoredAnnotation is not { } previous ||
                !previous.AutoMosaicRegions.Equals(annotation.AutoMosaicRegions))
                _privacyRegionIndex = new(annotation.AutoMosaicRegions);
            _privacyRestoredAnnotation = annotation;
            _privacyRestoredBounds = bounds;
            _privacyRestoredVersion = _privacyCandidatesVersion;
            var originalBounds = annotation.AutoMosaicCandidateBounds;
            for (var index = 0; index < _privacyCandidates.Length; index++)
            {
                var candidate = annotation.AutoMosaicCandidates[index].Bounds;
                var normalized = new Rect((candidate.X - originalBounds.X) / originalBounds.Width,
                    (candidate.Y - originalBounds.Y) / originalBounds.Height,
                    candidate.Width / originalBounds.Width, candidate.Height / originalBounds.Height);
                _privacySelected[index] = _privacyRegionIndex!.ContainsEquivalent(normalized);
            }
        }

        private static PrivacyCandidate ReprojectPrivacyCandidate(PrivacyCandidate candidate, Rect original,
            Rect target)
        {
            var x = target.X + (candidate.Bounds.X - original.X) / original.Width * target.Width;
            var y = target.Y + (candidate.Bounds.Y - original.Y) / original.Height * target.Height;
            var right = x + candidate.Bounds.Width / original.Width * target.Width;
            var bottom = y + candidate.Bounds.Height / original.Height * target.Height;
            var left = (int)Math.Floor(x);
            var top = (int)Math.Floor(y);
            return candidate with
            {
                Bounds = new(left, top, (int)Math.Ceiling(right) - left, (int)Math.Ceiling(bottom) - top),
            };
        }

        private bool PrivacyCandidatesMatch(int index, int otherIndex)
        {
            var candidate = _privacyCandidates[index];
            var other = _privacyCandidates[otherIndex];
            if (candidate.IsImageStructure)
                return PrivacyCandidateGrouping.HasSimilarAppearance(candidate, other);
            if (string.IsNullOrEmpty(_privacyGroupKeys[otherIndex])) return false;
            return (candidate.Kind == other.Kind || (candidate.IsSensitive && other.IsSensitive)) &&
                   _privacyGroupKeys[index] == _privacyGroupKeys[otherIndex];
        }

        private static string NormalizePrivacyText(string text)
        {
            return text.Replace('（', '(').Replace('）', ')').Replace('【', '[').Replace('】', ']')
                .Replace('｛', '{').Replace('｝', '}').Replace('〔', '[').Replace('〕', ']')
                .Replace('《', '<').Replace('》', '>');
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
            _privacyGroupKeys = [];
            _privacyCandidatesVersion++;
            _privacyRestoredAnnotation = null;
            _privacyRegionIndex = null;
            _privacyDragStart = null;
            _privacyDragTargets = [];
            if (_tool == ScreenshotTool.AutoMosaic) _tool = ScreenshotTool.Select;
            if (!_disposed) Changed?.Invoke();
        }

        private bool AcceptsPrivacyResult(long request)
        {
            return !_disposed && IsPrivacyDetectionActive && request == _privacyRequest &&
                   Tool == ScreenshotTool.AutoMosaic && Selection == _privacySelection &&
                   _privacyCancel is { IsCancellationRequested: false };
        }

        private void RenderPrivacyCandidates(DrawingContext context, double scale)
        {
            if (!IsPrivacyDetectionActive || PrivacyDetectionPending) return;
            if (!IsBusy)
            {
                var current =
                    _annotations.FirstOrDefault(annotation => annotation.AutoMosaicId == _privacyAnnotationId);
                if (current is not null) RestorePrivacySelection(current);
                else Array.Clear(_privacySelected);
            }

            using var clip = context.PushClip(new(Selection.X, Selection.Y, Selection.Width, Selection.Height));
            if (_privacyDragStart is { } start)
            {
                DrawPrivacyTargets(context, _privacyDragTargets,
                    _privacyDragTargets.Any(index => !_privacySelected[index]), scale);
                var rectangle = new Rect(new(start.X, start.Y),
                    new Point(_privacyDragEnd.X, _privacyDragEnd.Y)).Normalize().Intersect(_privacyObjectBounds);
                context.DrawRectangle(new SolidColorBrush(Color.FromArgb(60, 0x33, 0x88, 0xff)),
                    new Pen(new SolidColorBrush(Color.FromRgb(0x33, 0x88, 0xff)), scale), rectangle);
            }
            else if (_hoverPoint is { } point && FindPrivacyCandidate(point) is >= 0 and var index)
            {
                var targets = PrivacyHoverTargets(index);
                DrawPrivacyTargets(context, targets, !targets.Any(target => _privacySelected[target]), scale);
            }
        }
    }
}
