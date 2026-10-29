using Avalonia;
using Avalonia.Input;
using ImageMagick;

namespace RitsukageGif.Desktop
{
    internal sealed partial class SelectionCoordinator
    {
        private bool _historyPreviewHdr;
        private Task? _historyWork;
        private CaptureImageHistoryEntry[] _imageHistory = [];
        private int _imageHistoryIndex = -1;
        private CaptureRegionHistoryEntry[] _regionHistory = [];
        private int _regionHistoryIndex = -1;

        // ReSharper disable once UnusedMember.Global
        internal Task HistoryWork => _historyWork ?? Task.CompletedTask;

        // ReSharper disable once UnusedAutoPropertyAccessor.Global
        internal Action<Exception>? HistoryError { get; set; }

        // ReSharper disable once MemberCanBePrivate.Global
        // ReSharper disable once AutoPropertyCanBeMadeGetOnly.Global
        internal Func<PixelPoint, bool> MoveCapturePointer { get; set; } = CapturePointerMovement.Move;

        internal void ConfigureHistory(CaptureHistoryStore history)
        {
            if (_started) throw new InvalidOperationException();
            _regionHistory = history.Regions();
            _imageHistory = history.Images();
            _regionHistoryIndex = _imageHistoryIndex = -1;
        }

        private void SelectScreenOrAll()
        {
            var bounds = SelectionBounds();
            if (bounds.IsEmpty) return;
            var monitor = _windows.FirstOrDefault(window => window.Region.Contains(_pointer))?.Region ?? bounds;
            var selected = _state.Selected == monitor ? bounds : monitor;
            if (WindowSource && ScreenshotMode && !UsesLocalSource)
            {
                _historyWork = _screenshotTask = SelectScreenFromWindowAsync(selected);
                return;
            }

            if (WindowSource && !UsesLocalSource)
            {
                WindowSource = false;
                PreferencesChanged?.Invoke();
            }

            _state.Set(selected);
            _selectionIsWholeTarget = false;
            _selectedTarget = null;
            Annotations?.NotifySelection(selected);
        }

        private async Task SelectScreenFromWindowAsync(PixelRect region)
        {
            await SwitchScreenshotSourceAsync(false);
            if (_finished || WindowSource) return;
            _state.Set(region);
            _selectionIsWholeTarget = false;
            _selectedTarget = null;
            Annotations?.NotifySelection(region);
            Refresh();
        }

        private void MovePointer(Key direction)
        {
            var dx = direction switch { Key.Left => -1, Key.Right => 1, _ => 0 };
            var dy = direction switch { Key.Up => -1, Key.Down => 1, _ => 0 };
            var bounds = SelectionBounds();
            if (bounds.IsEmpty) return;
            var point = new PixelPoint((int)Math.Clamp((long)_pointer.X + dx, bounds.X, (long)bounds.Right - 1),
                (int)Math.Clamp((long)_pointer.Y + dy, bounds.Y, (long)bounds.Bottom - 1));
            var window = _windows.FirstOrDefault(item => item.Region.Contains(point));
            if (window is null) return;
            var physical = point;
            if (UsesLocalSource)
            {
                var mapped = window.PointToScreen(new(
                    (point.X - window.Region.X) * window.ClientSize.Width / window.Region.Width,
                    (point.Y - window.Region.Y) * window.ClientSize.Height / window.Region.Height));
                physical = new(mapped.X, mapped.Y);
            }

            if (!MoveCapturePointer(physical)) return;
            PointerInput(point, _left, _right, _shift);
        }

        private void BrowseRegionHistory(bool previous)
        {
            if (_regionHistory.Length == 0 || UsesLocalSource || Annotations is { HasAnnotations: true }) return;
            var index = Math.Clamp(_regionHistoryIndex + (previous ? 1 : -1), 0, _regionHistory.Length - 1);
            var entry = _regionHistory[index].Selection;
            var region = entry.Region;
            WindowTarget? target = null;
            if (entry.WindowSource)
            {
                target = _targets.FirstOrDefault(item =>
                    item.Handle == entry.Target?.Handle && item.Process == entry.Target?.Process);
                if (target is null) return;
                if (entry.Target is { } old)
                    region = new(target.Client.X + entry.Region.X - old.Client.X,
                        target.Client.Y + entry.Region.Y - old.Client.Y, entry.Region.Width, entry.Region.Height);
                region = region.Intersect(target.Client);
            }
            else
            {
                region = region.Intersect(SelectionBounds());
            }

            if (region.IsEmpty) return;
            if (ScreenshotMode && (WindowSource != entry.WindowSource || entry.WindowSource))
            {
                _historyWork = _screenshotTask = RestoreRegionSourceAsync(index, region, target, entry.WindowSource);
                return;
            }

            ApplyHistoryRegion(index, region, target, entry.WindowSource);
        }

        private void ApplyHistoryRegion(int index, PixelRect region, WindowTarget? target, bool windowSource)
        {
            _regionHistoryIndex = index;
            WindowSource = windowSource;
            _selectedTarget = target;
            _selectionIsWholeTarget = false;
            _state.Set(region);
            PreferencesChanged?.Invoke();
            Annotations?.NotifySelection(region);
            Refresh();
        }

        private async Task RestoreRegionSourceAsync(int index, PixelRect region, WindowTarget? target,
            bool windowSource)
        {
            var previousRegion = _state.Selected;
            var previousTarget = _selectedTarget;
            var previousWhole = _selectionIsWholeTarget;
            _selectedTarget = target;
            _selectionIsWholeTarget = false;
            _state.Set(region);
            await SwitchScreenshotSourceAsync(windowSource);
            if (_finished) return;
            if (WindowSource != windowSource || (windowSource && _frozenWindow?.Target.Handle != target?.Handle))
            {
                _selectedTarget = previousTarget;
                _selectionIsWholeTarget = previousWhole;
                _state.Set(previousRegion);
                Refresh();
                return;
            }

            ApplyHistoryRegion(index, region, target, windowSource);
        }

        private void BrowseImageHistory(bool previous)
        {
            if (_imageHistory.Length == 0 || Annotations is not { IsBusy: false, IsDrawing: false } annotations) return;
            if (annotations.HasAnnotations)
            {
                annotations.SetExportState(false, UiText.Get("CaptureHistoryHasAnnotations"));
                return;
            }

            var index = Math.Clamp(_imageHistoryIndex + (previous ? 1 : -1), 0, _imageHistory.Length - 1);
            if (index == _imageHistoryIndex) return;
            _historyWork = _screenshotTask = LoadHistoryImageAsync(index);
        }

        private async Task LoadHistoryImageAsync(int index)
        {
            _screenshotCompleting = true;
            _showing = true;
            try
            {
                var entry = _imageHistory[index];
                var frame = await Task.Run(() =>
                {
                    if (entry.Hdr is { } originalHdr)
                        return new(PackSdrFrame(HdrToneMapping.Map(originalHdr, _screenshotToneMap, _cancel.Token)),
                            originalHdr.Width,
                            originalHdr.Height);
                    using var image = new MagickImage();
                    image.Ping(entry.Png.Span);
                    if (image.Width > 32768 || image.Height > 32768 ||
                        (long)image.Width * image.Height * 4 > 256L * 1024 * 1024)
                        throw new InvalidOperationException(UiText.Get("SelectionTooLarge"));
                    image.Read(entry.Png.Span);
                    using var pixels = image.GetPixels();
                    return new ScrollCaptureImage(pixels.ToByteArray(PixelMapping.BGRA)!, (int)image.Width,
                        (int)image.Height);
                }, _cancel.Token);
                if (_finished) return;
                var outputMode = Annotations!.OutputColorMode;
                ReplaceScreenshotPreview(frame, entry.CaptureScaling, entry.OutputOrigin);
                RestoreFrameDecoration(entry.Decoration);
                _previewOriginalPng = entry.Png.ToArray();
                Annotations!.OutputColorMode = outputMode;
                _hdrScreenshots.Clear();
                _historyPreviewHdr = entry.Hdr is not null;
                if (entry.Hdr is { } hdr)
                {
                    _hdrScreenshots.Add((new(0, 0, frame.Width, frame.Height), hdr));
                    _windows[0].ReplacePixels(frame.Pixels);
                }

                _imageHistoryIndex = index;
                foreach (var window in _windows) window.Show();
                _windows.FirstOrDefault()?.Activate();
            }
            catch (OperationCanceledException) when (_cancel.IsCancellationRequested)
            {
            }
            catch (Exception error)
            {
                Annotations?.SetExportState(false, error.Message);
                HistoryError?.Invoke(error);
            }
            finally
            {
                _screenshotCompleting = false;
                _showing = false;
                if (!_finished) Refresh();
            }
        }
    }
}
