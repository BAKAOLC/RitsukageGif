using System.Globalization;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;

namespace RitsukageGif.Desktop
{
    // ReSharper disable once ClassCannotBeInstantiated
    internal sealed partial class PinnedImageWindow
    {
        private static readonly IBrush TextSelectionBrush = AppTheme.Accent;
        private static readonly ITextRecognitionService DefaultTextRecognition = new OcrRecognitionService();
        private string _recognizedCulture = "";
        private string? _recognizedLanguage;
        private OcrRecognitionResult? _recognizedText;
        private (OcrWord Word, int Line)[] _recognizedWords = [];
        private int _selectedTextEnd = -1;
        private int _selectedTextStart = -1;
        private bool _selectingText;
        private bool _textRecognitionAttempted;
        private CancellationTokenSource? _textRecognitionCancellation;
        private PinnedTextSelectionSurface? _textSelectionSurface;
        public Func<ITextRecognitionService>? TextRecognitionServiceProvider { get; set; }
        public Func<RecognitionPreferences> RecognitionPreferencesProvider { get; set; } = () => new();

        private bool CanSelectImageText => CurrentPreferences.SelectableText &&
                                           (!_isThumbnail || CurrentPreferences.ThumbnailSelectableText);

        private void EnsureTextRecognition()
        {
            if (_closed || !CanSelectImageText || _annotation is not null) return;
            var preferences = RecognitionPreferencesProvider();
            var culture = CultureInfo.CurrentUICulture.Name;
            if (_textRecognitionAttempted && string.Equals(_recognizedLanguage, preferences.LanguageTag,
                    StringComparison.OrdinalIgnoreCase) && _recognizedCulture == culture) return;
            ResetTextRecognition();
            _recognizedLanguage = preferences.LanguageTag;
            _recognizedCulture = culture;
            preferences = preferences.Normalize();
            _textRecognitionAttempted = true;
            var cancellation = _textRecognitionCancellation =
                CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            var pixels = _pixels;
            var width = (int)_bitmap.Size.Width;
            var height = (int)_bitmap.Size.Height;
            var service = TextRecognitionServiceProvider?.Invoke() ?? DefaultTextRecognition;
            _ = RecognizeSelectableTextAsync(service, pixels, width, height, preferences, cancellation);
        }

        private async Task RecognizeSelectableTextAsync(ITextRecognitionService service, byte[] pixels,
            int width, int height, RecognitionPreferences preferences, CancellationTokenSource cancellation)
        {
            try
            {
                var capabilities = await service.GetCapabilitiesAsync(cancellation.Token).ConfigureAwait(false);
                var result = await service.RecognizeBgraAsync(pixels, width, height, checked(width * 4),
                        RecognitionOutput.Language(preferences, capabilities.Languages), cancellation.Token)
                    .ConfigureAwait(false);
                cancellation.Token.ThrowIfCancellationRequested();
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (_closed || !ReferenceEquals(_textRecognitionCancellation, cancellation)) return;
                    _recognizedText = result;
                    _recognizedWords =
                    [
                        .. result.Lines
                            .SelectMany((line, index) => line.Words.Select(word => (word, index))),
                    ];
                });
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
            }
            catch (Exception error)
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (!_closed && ReferenceEquals(_textRecognitionCancellation, cancellation))
                        ToolTip.SetTip(_image, error.Message);
                });
            }
            finally
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (ReferenceEquals(_textRecognitionCancellation, cancellation))
                        _textRecognitionCancellation = null;
                });
                cancellation.Dispose();
            }
        }

        private void ResetTextRecognition()
        {
            _textRecognitionCancellation?.Cancel();
            _textRecognitionCancellation = null;
            _textRecognitionAttempted = false;
            _recognizedText = null;
            _recognizedWords = [];
            ClearTextSelection();
            if (ReferenceEquals(_image.Cursor, TextSelectionCursor)) _image.Cursor = null;
            _image.Bind(ToolTip.TipProperty, LiveText.Resource("PinnedImageHelp"));
        }

        private Point TextImagePoint(Point point)
        {
            return new(
                DisplayRegion.X + point.X * RenderScaling / _zoom,
                DisplayRegion.Y + point.Y * RenderScaling / _zoom);
        }

        private int HitRecognizedWord(Point point)
        {
            var pixel = TextImagePoint(point);
            if (_recognizedText is null) return -1;
            var index = 0;
            foreach (var (_, bounds, words) in _recognizedText.Lines)
                if (pixel.X >= bounds.X && pixel.X <= bounds.X + bounds.Width &&
                    pixel.Y >= bounds.Y && pixel.Y <= bounds.Y + bounds.Height)
                    foreach (var word in words)
                    {
                        var wordBounds = word.Bounds;
                        if (pixel.X >= wordBounds.X && pixel.X <= wordBounds.X + wordBounds.Width &&
                            pixel.Y >= wordBounds.Y && pixel.Y <= wordBounds.Y + wordBounds.Height) return index;
                        index++;
                    }
                else index += words.Count;

            return -1;
        }

        private bool TryBeginTextSelection(PointerPressedEventArgs args)
        {
            if (_regionSelectionMode || !CanSelectImageText || _recognizedText is null ||
                args.KeyModifiers != KeyModifiers.None ||
                args.GetCurrentPoint(_image).Properties.PointerUpdateKind != PointerUpdateKind.LeftButtonPressed)
                return false;
            var edges = GetResizeEdges(args.GetPosition(this));
            if (edges.Horizontal != 0 || edges.Vertical != 0) return false;
            var index = HitRecognizedWord(args.GetPosition(_image));
            if (index < 0) return false;
            _selectedTextStart = _selectedTextEnd = index;
            _selectingText = true;
            if (_textSelectionSurface is null)
            {
                _textSelectionSurface = new(DrawTextSelection) { IsHitTestVisible = false, ClipToBounds = true };
                _content.Children.Add(_textSelectionSurface);
            }

            _textSelectionSurface.IsVisible = true;
            _hdrPreviewWork?.Cancel();
            _hdrPreview?.SetFrame(null);
            if (_hdrSource is not null)
                _hdrIndicator.Bind(TextBlock.TextProperty, LiveText.Resource("PinnedImageSdrPreview"));
            _textSelectionSurface.InvalidateVisual();
            args.Pointer.Capture(_image);
            args.Handled = true;
            return true;
        }

        private bool ContinueTextSelection(PointerEventArgs args)
        {
            if (!_selectingText) return false;
            var index = HitRecognizedWord(args.GetPosition(_image));
            if (index >= 0 && index != _selectedTextEnd)
            {
                _selectedTextEnd = index;
                _textSelectionSurface?.InvalidateVisual();
            }

            args.Handled = true;
            return true;
        }

        private bool FinishTextSelection(PointerReleasedEventArgs args)
        {
            if (!_selectingText) return false;
            _selectingText = false;
            args.Pointer.Capture(null);
            args.Handled = true;
            return true;
        }

        private void ClearTextSelection()
        {
            var hadSelection = _selectedTextStart >= 0;
            _selectingText = false;
            _selectedTextStart = _selectedTextEnd = -1;
            _textSelectionSurface?.IsVisible = false;
            if (hadSelection && !_closed && _annotation is null) RefreshHdrPreview();
        }

        private void DrawTextSelection(DrawingContext context)
        {
            if (_selectedTextStart < 0) return;
            var scale = _zoom / RenderScaling;
            var region = DisplayRegion;
            using var clip = context.PushClip(_viewport.Bounds);
            using var opacity = context.PushOpacity(0.4);
            for (var index = Math.Min(_selectedTextStart, _selectedTextEnd);
                 index <= Math.Max(_selectedTextStart, _selectedTextEnd);
                 index++)
            {
                var bounds = _recognizedWords[index].Word.Bounds;
                var rectangle = new Rect((bounds.X - region.X) * scale - _viewport.Offset.X,
                    (bounds.Y - region.Y) * scale - _viewport.Offset.Y, bounds.Width * scale, bounds.Height * scale);
                context.DrawRectangle(TextSelectionBrush, null, rectangle);
            }
        }


        private string SelectedRecognizedText()
        {
            if (_selectedTextStart < 0 || _recognizedText is null) return "";
            var first = Math.Min(_selectedTextStart, _selectedTextEnd);
            var last = Math.Max(_selectedTextStart, _selectedTextEnd);
            var selected = new StringBuilder();
            for (var index = first; index <= last;)
            {
                var lineIndex = _recognizedWords[index].Line;
                var line = _recognizedText.Lines[lineIndex];
                var end = index;
                while (end < last && _recognizedWords[end + 1].Line == lineIndex) end++;
                var cursor = 0;
                var from = -1;
                var to = -1;
                foreach (var word in line.Words)
                {
                    var position = line.Text.IndexOf(word.Text, cursor, StringComparison.Ordinal);
                    if (position < 0) break;
                    if (ReferenceEquals(word, _recognizedWords[index].Word)) from = position;
                    cursor = position + word.Text.Length;
                    if (ReferenceEquals(word, _recognizedWords[end].Word))
                    {
                        to = cursor;
                        break;
                    }
                }

                if (selected.Length != 0) selected.AppendLine();
                if (from >= 0 && to >= from) selected.Append(line.Text.AsSpan(from, to - from));
                else
                    selected.AppendJoin(' ',
                        _recognizedWords.Skip(index).Take(end - index + 1).Select(item => item.Word.Text));
                index = end + 1;
            }

            return selected.ToString();
        }

        private async Task CopySelectedTextAsync()
        {
            if (_selectedTextStart < 0) return;
            var text = RecognitionTextFormatter.Format(SelectedRecognizedText(), RecognitionPreferencesProvider());
            var output = AutomaticSave is not null &&
                         (SavePreferencesProvider().Normalize().AutoSaveTriggers &
                          ScreenshotAutoSaveTriggers.Recognize) != 0
                ? await CaptureOutputAsync(OutputColorMode.Sdr, _lifetime.Token)
                : null;
            var clipboard = Clipboard ?? throw new InvalidOperationException(UiText.Get("ClipboardFailed"));
            await clipboard.SetTextAsync(text);
            await clipboard.FlushAsync();
            if (output is not null) await SaveAutomaticallyAsync(ScreenshotAutoSaveTriggers.Recognize, output);
        }
    }
}
