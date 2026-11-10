using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace RitsukageGif.Desktop
{
    // ReSharper disable once ClassCannotBeInstantiated
    internal sealed partial class PinnedImageWindow
    {
        private readonly TextBlock _hdrIndicator = new()
            { IsVisible = false, FontSize = 11, Margin = new(4, 2), TextTrimming = TextTrimming.CharacterEllipsis };

        private readonly SemaphoreSlim _hdrPreviewGate = new(1);


        private HdrPreviewHost? _hdrPreview;
        private CancellationTokenSource? _hdrPreviewWork;
        private CapturedFrame? _hdrSource;
        private PinnedViewportSnapshot? _hdrViewport;
        private PinnedHdrPreviewBackground? _hdrBackground;
        private bool _hdrTextMarkupVisible;
        private bool _hdrViewportRefreshQueued;
        private byte[]? _sdrPng;

        private void ConfigureHdr(CapturedFrame? frame)
        {
            _hdrSource = frame;
            if (frame is null)
            {
                _hdrPreviewWork?.Cancel();
                _hdrPreview?.SetFrame(null);
                _hdrIndicator.IsVisible = false;
                return;
            }

            if (_hdrPreview is null)
            {
                _hdrPreview = new()
                {
                    TransparentComposition = true,
                    SmoothScaling = CurrentPreferences.SmoothScaling, IsHitTestVisible = false,
                    HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch,
                };
                _hdrPreview.PresentationChanged += visible =>
                {
                    _image.Opacity = visible ? 0 : 1;
                    _transparentBackground.Opacity = visible ? 0 : 1;
                    _inputRegions?.SetHdrFrame(_hdrPreview?.PresentedFrame);
                    InvalidateInputUi();
                    _hdrIndicator.Bind(TextBlock.TextProperty,
                        LiveText.Resource(visible ? "PinnedImageHdrPreview" : "PinnedImageSdrPreview"));
                    _textSelectionSurface?.InvalidateVisual();
                    if (_annotationSurface is { } surface && _annotationScene is { } scene)
                        surface.RenderContent = !visible && !NeedsAnnotationPreview(scene);
                };
                _content.Children.Insert(1, _hdrPreview);
                _hdrPreview.DisplayColorChanged += RefreshHdrPreview;
                _viewport.LayoutUpdated += (_, _) => QueueHdrViewportRefresh();
                _content.RowDefinitions = new("*,Auto");
                Grid.SetRow(_hdrIndicator, 1);
                _hdrIndicator[!ToolTip.TipProperty] = _hdrIndicator[!TextBlock.TextProperty];
                _content.Children.Add(_hdrIndicator);
            }

            _hdrIndicator.IsVisible = true;
            _hdrIndicator.Bind(TextBlock.TextProperty, LiveText.Resource("PinnedImageSdrPreview"));

            RefreshHdrPreview();
            _hdrIndicator.Bind(TextBlock.TextProperty,
                LiveText.Resource(_hdrPreview.IsVisible ? "PinnedImageHdrPreview" : "PinnedImageSdrPreview"));
            UpdateSize();
        }

        private void RefreshHdrPreview()
        {
            _hdrPreviewWork?.Cancel();
            if (TryRefreshAnimationHdr()) return;
            if (TryRefreshLiveHdr()) return;
            if (_hdrPreview is null) return;
            if (ProcessingSourceHdr is not { } source || _annotation is not null || HasTextSelection)
            {
                _hdrPreview.SetFrame(null);
                return;
            }

            var work = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            _hdrPreviewWork = work;
            _hdrPreview.SetFrame(null);
            _ = RefreshHdrPreviewAsync(source, work);
        }

        private async Task RefreshHdrPreviewAsync(CapturedFrame source, CancellationTokenSource work)
        {
            var entered = false;
            var viewport = SnapshotInputHdrViewport();
            var background = SnapshotHdrPreviewBackground(source);
            _hdrViewport = viewport;
            _hdrBackground = background;
            _hdrTextMarkupVisible = HasTextMarkup;
            var colorState = _colorMatrixState;
            var colorContent = ReferenceEquals(source, ProcessingSourceHdr) ? ColorContent : null;
            try
            {
                await _hdrPreviewGate.WaitAsync(work.Token);
                entered = true;
                if (colorContent?.Hdr is { } prepared)
                    source = prepared;
                else if (colorState.Matrix is not null)
                    source = await Task.Run(() => PinnedColorMatrixProcessor.Apply(source, colorState, work.Token),
                        work.Token);

                if (HasTextMarkup && _textMarkupSession is { HasAnnotations: true } marks)
                    source = await marks.ComposeHdrAsync(marks.Selection, source, token: work.Token);

                if (_annotationScene is { HasAnnotations: true } scene)
                    source = await scene.ComposeHdrAsync(scene.Selection, source, token: work.Token);

                using var preview =
                    await Task.Run(() => PinnedViewportRasterizer.PreviewTransparent(source, viewport, background,
                        work.Token), work.Token);
                if (!_closed && _annotation is null && !HasTextSelection && ReferenceEquals(_hdrPreviewWork, work))
                {
                    _hdrPreview?.SetFrame(preview);
                    _inputRegions?.SetHdrFrame(_hdrPreview?.PresentedFrame);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception error)
            {
                PinLogger.ZLogWarning(error, $"Pinned HDR preview rendering failed.");
                if (!_closed && ReferenceEquals(_hdrPreviewWork, work))
                {
                    _status.Text = error.Message;
                    _status.IsVisible = true;
                }
            }
            finally
            {
                if (entered) _hdrPreviewGate.Release();
                if (ReferenceEquals(_hdrPreviewWork, work)) _hdrPreviewWork = null;
                work.Dispose();
            }
        }

        private void QueueHdrViewportRefresh()
        {
            if (_closed || _hdrPreview is null || _hdrViewportRefreshQueued) return;
            _hdrViewportRefreshQueued = true;
            Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    if (_closed || _hdrPreview is null) return;
                    var viewport = SnapshotInputHdrViewport();
                    var background = ProcessingSourceHdr is { } source
                        ? SnapshotHdrPreviewBackground(source)
                        : (PinnedHdrPreviewBackground?)null;
                    if (_hdrViewport == viewport && _hdrBackground == background &&
                        _hdrTextMarkupVisible == HasTextMarkup) return;
                    _hdrViewport = viewport;
                    RefreshHdrPreview();
                }
                finally
                {
                    _hdrViewportRefreshQueued = false;
                }
            }, DispatcherPriority.Render);
        }
    }
}
