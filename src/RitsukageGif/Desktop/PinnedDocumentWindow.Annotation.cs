using Avalonia.Controls;
using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    internal sealed partial class PinnedDocumentWindow
    {
        private long _annotationAppliedVersion = -1;
        private bool _annotationExtentDirty;
        private long _annotationFailedVersion = -1;
        private bool _annotationGeometryDirty;
        private int _annotationOutputs;
        private Task _annotationRefresh = Task.CompletedTask;
        private bool _annotationRefreshing;
        private double _annotationScaling = 1;
        private ScreenshotAnnotationSession? _annotationScene;
        private Color _annotationSourceInk = AppTheme.Ink.Color;
        private byte[]? _annotationSourcePixels;
        private Color _annotationSourceSurface = AppTheme.Surface.Color;
        private long _annotationSourceVersion;
        private ScreenshotAnnotationSurface? _annotationSurface;
        private PinnedAnnotationToolbar? _annotationToolbar;
        private PinnedDocumentState? _closedDocumentState;
        private bool _documentSourcesReleased;
        private bool _preparingAnnotationSurface;

        internal Func<AnnotationPreferences> AnnotationPreferencesProvider { get; init; } = () => new();
        internal Action<AnnotationPreferences>? AnnotationPreferencesChanged { get; init; }
        internal Func<TranslationPreferences> TranslationPreferencesProvider { get; init; } = () => new();
        internal Func<RecognitionPreferences> RecognitionPreferencesProvider { get; init; } = () => new();

        internal Func<ITextRecognitionService> TextRecognitionServiceProvider { get; init; } =
            () => new OcrRecognitionService();

        internal Func<Window, ReadOnlyMemory<byte>, CancellationToken, Task>? ImageTranslationRequested { get; init; }
        private bool IsMarkEditing { get; set; }

        private bool IsMarkHidden => _annotationScene is { AnnotationsVisible: false };

        private Task SetMarkEditingAsync(bool enabled)
        {
            if (!enabled)
            {
                EndDocumentMark();
                return Task.CompletedTask;
            }

            if (_closed || IsMarkEditing || _mouseThrough) return Task.CompletedTask;
            var session = EnsureDocumentAnnotations();
            if (!session.AnnotationsVisible) session.ToggleAnnotations();
            IsMarkEditing = true;
            session.CanAnnotate = true;
            session.ApplyPinnedPreferences(AnnotationPreferencesProvider());
            _annotationToolbar = new(this, session, DocumentAnnotationShortcuts,
                () => _ = RunAsync(SaveRenderedImageAsync), EndDocumentMark);
            _annotationToolbar.Show(this);
            _annotationToolbar.Place();
            AnnotationChanged();
            return Task.CompletedTask;
        }

        private void SetMarkHidden(bool hidden)
        {
            if (IsMarkEditing || _closed) return;
            var session = EnsureDocumentAnnotations();
            if (session.AnnotationsVisible == hidden) session.ToggleAnnotations();
        }

        private ScreenshotAnnotationSession EnsureDocumentAnnotations()
        {
            if (_annotationScene is { } existing) return existing;
            _annotationScaling = RenderScaling;
            var session = _annotationScene = new()
            {
                CanScroll = false, CanAnnotate = false, Tool = ScreenshotTool.Brush,
            };
            session.ApplyPinnedPreferences(AnnotationPreferencesProvider());
            session.ToolbarPreferencesProvider = () => AnnotationPreferencesProvider().Toolbar;
            session.ToolbarPreferencesChanged += (preferences, tools) =>
            {
                var current = AnnotationPreferencesProvider();
                AnnotationPreferencesChanged?.Invoke(current with
                {
                    Toolbar = current.Toolbar.Merge(preferences, tools),
                });
            };
            session.CompleteRequested += action => _ = RunAsync(() => CompleteDocumentAnnotationAsync(action));
            session.RecognitionRequested += mode => _ = RunAsync(() => RecognizeDocumentImageAsync(mode));
            session.TranslationRequested += () => _ = RunAsync(TranslateDocumentImageAsync);
            session.PrivacyDetectionRequested += request => _ = DetectDocumentPrivacyAsync(session, request);
            session.Changed += AnnotationChanged;
            DocumentAnnotationLayoutChanged(true);
            PrepareDocumentAnnotationSurface();
            return session;
        }

        private bool PrepareDocumentAnnotationSurface()
        {
            if (!IsLoaded || !DocumentSceneCoordinatesAvailable || _preparingAnnotationSurface ||
                _annotationScene is not { } session) return false;
            if (_annotationSurface is not null) return true;
            _preparingAnnotationSurface = true;
            try
            {
                UpdateDocumentAnnotationExtent();
                _annotationExtentDirty = false;
                _annotationSurface = new(session, session.Selection, _annotationScaling / DocumentSceneViewScale,
                    args =>
                    {
                        var point = DocumentScenePoint(args.GetPosition(_content));
                        return new((int)Math.Floor(point.X * _annotationScaling),
                            (int)Math.Floor(point.Y * _annotationScaling));
                    });
                _content.Children.Insert(_content.Children.Count - 1, _annotationSurface);
                RefreshDocumentAnnotationGeometry();
                return true;
            }
            finally
            {
                _preparingAnnotationSurface = false;
            }
        }

        private void EndDocumentMark()
        {
            if (!IsMarkEditing || _annotationScene is not { } session) return;
            session.CommitTextEdit();
            session.CancelGesture();
            session.ClearSelectedAnnotation();
            var preferences = session.CapturePinnedPreferences(AnnotationPreferencesProvider());
            IsMarkEditing = false;
            session.CanAnnotate = false;
            _annotationToolbar?.Dispose();
            _annotationToolbar = null;
            AnnotationPreferencesChanged?.Invoke(preferences);
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        private static IReadOnlyList<SelectionCommandDefinition> DocumentAnnotationShortcuts()
        {
            return SelectionCommands.SnapshotBindings();
        }

        private void AnnotationChanged()
        {
            if (_closed) return;
            StateChanged?.Invoke(this, EventArgs.Empty);
            RefreshDocumentAnnotationSource();
        }

        private void RefreshDocumentAnnotationAppearance()
        {
            var surface = AppTheme.Surface.Color;
            var ink = AppTheme.Ink.Color;
            if (_annotationSourceSurface == surface && _annotationSourceInk == ink) return;
            _annotationSourceSurface = surface;
            _annotationSourceInk = ink;
            if (_annotationScene is null) return;
            DocumentAnnotationLayoutChanged(true);
            RefreshDocumentAnnotationSource();
        }

        private void RefreshDocumentAnnotationSource()
        {
            if (_closed) return;
            if (!IsLoaded) return;
            if (_annotationScene is not { } session || _annotationRefreshing || session.IsDrawing || session.IsBusy ||
                _annotationAppliedVersion == _annotationSourceVersion ||
                _annotationFailedVersion == _annotationSourceVersion) return;
            if (session.HasMosaicAnnotations || (IsMarkEditing && NeedsDocumentAnnotationPixels(session.Tool)))
            {
                _annotationRefreshing = true;
                _annotationRefresh = RefreshDocumentAnnotationSourceAsync(_lifetime.Token);
            }
        }

        private static bool NeedsDocumentAnnotationPixels(ScreenshotTool tool)
        {
            return tool is ScreenshotTool.Mosaic or ScreenshotTool.Blur or ScreenshotTool.SmartErase or
                ScreenshotTool.Magnifier or ScreenshotTool.Eraser or ScreenshotTool.AutoMosaic;
        }

        private void DocumentAnnotationLayoutChanged(bool sourceChanged)
        {
            if (_annotationScene is null) return;
            _annotationGeometryDirty = true;
            if (sourceChanged)
            {
                _annotationExtentDirty = true;
                _annotationSourceVersion++;
            }
        }

        private void DocumentAnnotationLayoutUpdated(object? sender, EventArgs args)
        {
            if (!_annotationGeometryDirty || _closed || !PrepareDocumentAnnotationSurface()) return;
            _annotationGeometryDirty = false;
            if (_annotationExtentDirty)
            {
                _annotationExtentDirty = false;
                UpdateDocumentAnnotationExtent();
            }

            RefreshDocumentAnnotationGeometry();
            AnnotationChanged();
        }

        private void UpdateDocumentAnnotationExtent()
        {
            if (_annotationScene is not { } session) return;
            var size = MeasureDocumentScene();
            session.NotifySelection(new(0, 0,
                Math.Max(1, (int)Math.Ceiling(size.Width * _annotationScaling)),
                Math.Max(1, (int)Math.Ceiling(size.Height * _annotationScaling))));
        }

        private void RefreshDocumentAnnotationGeometry()
        {
            if (_annotationSurface is not { } surface) return;
            var origin = DocumentSceneViewportOrigin;
            surface.Scale = _annotationScaling / DocumentSceneViewScale;
            surface.SourceOrigin = new((int)Math.Floor(origin.X * _annotationScaling),
                (int)Math.Floor(origin.Y * _annotationScaling));
            _annotationToolbar?.Place();
        }

        private void ReleaseDocumentAnnotationUi()
        {
            IsMarkEditing = false;
            _annotationToolbar?.Dispose();
            _annotationToolbar = null;
            _annotationSurface?.Dispose();
            _annotationSurface = null;
            if (_annotationScene is { } session) session.Changed -= AnnotationChanged;
        }

        private void TryReleaseDocumentSources()
        {
            if (!_closed || _busy || _annotationRefreshing || _annotationOutputs != 0 ||
                _documentSourcesReleased) return;
            _documentSourcesReleased = true;
            _textView?.ReleaseContent();
            _htmlResources?.Dispose();
            _documentBitmap?.Dispose();
            _annotationScene?.Dispose();
            _annotationScene = null;
            _annotationSourcePixels = null;
            _lifetime.Dispose();
        }
    }
}
