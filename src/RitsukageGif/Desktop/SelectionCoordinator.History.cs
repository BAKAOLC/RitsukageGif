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
            _selectionShape = null;
            _shapePoints.Clear();
            _regionEditBase = null;
            _selectionWindows.Clear();
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
            if (!MoveCapturePointer(CapturePointerScreenPoint(window, point))) return;
            PointerInput(point, _left, _right, _shift);
        }

        private PixelPoint CapturePointerScreenPoint(OverlayWindow window, PixelPoint point)
        {
            if (!UsesLocalSource) return point;
            var mapped = window.PointToScreen(new(
                (point.X - window.Region.X) * window.ClientSize.Width / window.Region.Width,
                (point.Y - window.Region.Y) * window.ClientSize.Height / window.Region.Height));
            return new(mapped.X, mapped.Y);
        }

        private void BrowseRegionHistory(bool previous)
        {
            if (_regionHistory.Length == 0 || UsesLocalSource || _screenshotCompleting ||
                Annotations is { HasAnnotations: true } or { IsBusy: true } or { IsDrawing: true }) return;
            var step = _regionHistoryIndex < 0 || previous ? 1 : -1;
            var index = _regionHistoryIndex < 0 ? 0 : _regionHistoryIndex + step;
            for (var attempt = 0; attempt < _regionHistory.Length; attempt++, index += step)
            {
                if (index < 0 || index >= _regionHistory.Length)
                {
                    if (!CapturePreferences.CycleRegionHistory) break;
                    index = (index + _regionHistory.Length) % _regionHistory.Length;
                }

                if (index == _regionHistoryIndex) break;
                if (!CaptureHistoryRegionResolver.TryResolve(_regionHistory[index].Selection, _targets,
                        SelectionBounds(), out var selection) || selection is null) continue;
                if (ScreenshotMode && (WindowSource != selection.WindowSource || selection.WindowSource))
                {
                    _historyWork = _screenshotTask = RestoreRegionSourceAsync(index, selection);
                }
                else
                {
                    SetHistorySelection(selection);
                    CommitHistorySelection(index);
                }

                return;
            }
        }

        private void SetHistorySelection(SelectionResult selection)
        {
            _selectedTarget = selection.Target;
            _selectionIsWholeTarget = false;
            _state.Set(selection.Region);
            _selectionShape = selection.Shape;
            _shapePoints.Clear();
            _regionEditBase = null;
            _regionEditTarget = null;
            _shapePreviewDirty = false;
            _selectionWindows.Clear();
            if (selection.Windows is { } windows) _selectionWindows.AddRange(windows);
            ShapeMode = selection.Windows is { Count: > 1 } ? SelectionShapeMode.MultiWindow :
                selection.Shape is not null ? SelectionShapeMode.Free : SelectionShapeMode.Rectangle;
            _windowShapeRevision++;
        }

        private void CommitHistorySelection(int index)
        {
            _shapeModeTimer?.Dispose();
            _shapeModeTimer = null;
            _previewShapeMode = null;
            _shapeHeldModifiers = KeyModifiers.None;
            _regionHistoryIndex = index;
            WindowSource = _regionHistory[index].Selection.WindowSource;
            PreferencesChanged?.Invoke();
            Annotations?.NotifySelection(_state.Selected);
            Refresh();
        }

        private async Task RestoreRegionSourceAsync(int index, SelectionResult selection)
        {
            var previousRegion = _state.Selected;
            var previousTarget = _selectedTarget;
            var previousWhole = _selectionIsWholeTarget;
            var previousSource = WindowSource;
            var previousShape = _selectionShape;
            var previousMode = ShapeMode;
            var previousWindows = _selectionWindows.ToArray();
            var previousPoints = _shapePoints.ToArray();
            var previousEdit = _regionEditBase;
            var previousEditTarget = _regionEditTarget;
            var previousDirty = _shapePreviewDirty;
            var previousRevision = _windowShapeRevision;
            var previousFrozenRevision = _frozenShapeRevision;
            SetHistorySelection(selection);
            var succeeded = await SwitchScreenshotSourceCoreAsync(selection.WindowSource, true);
            if (_finished) return;
            if (!succeeded)
            {
                _selectedTarget = previousTarget;
                _selectionIsWholeTarget = previousWhole;
                WindowSource = previousSource;
                _state.Set(previousRegion);
                _selectionShape = previousShape;
                ShapeMode = previousMode;
                _selectionWindows.Clear();
                _selectionWindows.AddRange(previousWindows);
                _shapePoints.Clear();
                _shapePoints.AddRange(previousPoints);
                _regionEditBase = previousEdit;
                _regionEditTarget = previousEditTarget;
                _shapePreviewDirty = previousDirty;
                _windowShapeRevision = previousRevision;
                _frozenShapeRevision = previousFrozenRevision;
                Annotations?.NotifySelection(previousRegion);
                Refresh();
                return;
            }

            CommitHistorySelection(index);
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
                _selectionShape = null;
                _shapePoints.Clear();
                _regionEditBase = null;
                _selectionWindows.Clear();
                ReplaceScreenshotPreview(frame, entry.CaptureScaling, entry.OutputOrigin);
                RestoreFrameDecoration(entry.Decoration);
                _previewOriginalPng = entry.Png.ToArray();
                Annotations!.OutputColorMode = outputMode;
                _hdrScreenshots.Clear();
                _nativeScreenshots.Clear();
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
