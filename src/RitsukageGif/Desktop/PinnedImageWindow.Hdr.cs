using System.Buffers;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    // ReSharper disable once ClassCannotBeInstantiated
    internal sealed partial class PinnedImageWindow
    {
        private readonly TextBlock _hdrIndicator = new()
            { IsVisible = false, FontSize = 11, Margin = new(4, 2), TextTrimming = TextTrimming.CharacterEllipsis };

        private readonly SemaphoreSlim _hdrPreviewGate = new(1);

        private Grid? _hdrLayer;
        private HdrPreviewHost? _hdrPreview;
        private CancellationTokenSource? _hdrPreviewWork;
        private CapturedFrame? _hdrSource;
        private byte[]? _sdrPng;

        private void ConfigureHdr(CapturedFrame? frame)
        {
            _hdrSource = frame;
            if (frame is null)
            {
                _hdrPreview?.SetFrame(null);
                _hdrIndicator.IsVisible = false;
                return;
            }

            if (_hdrPreview is null)
            {
                _hdrPreview = new();
                _hdrPreview.PresentationChanged += visible =>
                    _hdrIndicator.Bind(TextBlock.TextProperty,
                        LiveText.Resource(visible ? "PinnedImageHdrPreview" : "PinnedImageSdrPreview"));
                _hdrLayer = new();
                _viewport.Content = null;
                _hdrLayer.Children.Add(_image);
                _hdrLayer.Children.Add(_hdrPreview);
                _viewport.Content = _hdrLayer;
                _content.RowDefinitions = new("*,Auto");
                Grid.SetRow(_hdrIndicator, 1);
                _hdrIndicator[!ToolTip.TipProperty] = _hdrIndicator[!TextBlock.TextProperty];
                _content.Children.Add(_hdrIndicator);
            }

            _hdrIndicator.IsVisible = true;
            _hdrIndicator.Bind(TextBlock.TextProperty, LiveText.Resource("PinnedImageSdrPreview"));
            _hdrLayer!.Width = _image.Width;
            _hdrLayer.Height = _image.Height;
            RefreshHdrPreview();
            _hdrIndicator.Bind(TextBlock.TextProperty,
                LiveText.Resource(_hdrPreview.IsVisible ? "PinnedImageHdrPreview" : "PinnedImageSdrPreview"));
            UpdateSize();
        }

        private void RefreshHdrPreview()
        {
            _hdrPreviewWork?.Cancel();
            if (_hdrPreview is null || _hdrSource is not { } source || _annotation is not null ||
                _selectedTextStart >= 0) return;
            var work = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            _hdrPreviewWork = work;
            _hdrPreview.SetFrame(null);
            _ = RefreshHdrPreviewAsync(source, _pixels, DisplayRegion, work);
        }

        private async Task RefreshHdrPreviewAsync(CapturedFrame source, byte[] pixels, PixelRect region,
            CancellationTokenSource work)
        {
            var entered = false;
            try
            {
                await _hdrPreviewGate.WaitAsync(work.Token);
                entered = true;
                using var preview =
                    await Task.Run(() => BuildHdrPreview(source, pixels, region, work.Token), work.Token);
                if (!_closed && _annotation is null && _selectedTextStart < 0 && ReferenceEquals(_hdrPreviewWork, work))
                    _hdrPreview?.SetFrame(preview);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception error)
            {
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

        private static PreviewFrame BuildHdrPreview(CapturedFrame source, byte[] pixels, PixelRect region,
            CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var size = checked(region.Width * region.Height * 8);
            var hdr = ArrayPool<byte>.Shared.Rent(size);
            var sdr = ArrayPool<byte>.Shared.Rent(checked(region.Width * region.Height * 4));
            try
            {
                for (var y = 0; y < region.Height; y++)
                {
                    token.ThrowIfCancellationRequested();
                    source.Pixels.Span.Slice((region.Y + y) * source.Stride + region.X * 8, region.Width * 8)
                        .CopyTo(hdr.AsSpan(y * region.Width * 8));
                    pixels.AsSpan(((region.Y + y) * source.Width + region.X) * 4, region.Width * 4)
                        .CopyTo(sdr.AsSpan(y * region.Width * 4));
                    if (source.AlphaMode != CaptureAlphaMode.Straight) continue;
                    var channels = MemoryMarshal.Cast<byte, Half>(hdr.AsSpan(y * region.Width * 8, region.Width * 8));
                    for (var index = 0; index < channels.Length; index += 4)
                    {
                        var alpha = Math.Clamp((float)channels[index + 3], 0, 1);
                        for (var channel = 0; channel < 3; channel++)
                            channels[index + channel] = (Half)((float)channels[index + channel] * alpha);
                        channels[index + 3] = (Half)1;
                    }
                }

                return new(sdr, region.Width, region.Height, 0, hdr);
            }
            catch
            {
                ArrayPool<byte>.Shared.Return(hdr);
                ArrayPool<byte>.Shared.Return(sdr);
                throw;
            }
        }
    }
}
