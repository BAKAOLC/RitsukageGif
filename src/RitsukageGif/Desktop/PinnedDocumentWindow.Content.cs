using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace RitsukageGif.Desktop
{
    internal sealed partial class PinnedDocumentWindow
    {
        private Bitmap? _documentBitmap;
        private ItemsControl? _fileList;
        private Task<byte[]?>? _formulaPngTask;
        private bool _restoredLayout;

        private void RebuildContent()
        {
            _textView?.ReleaseContent();
            _textView = null;
            _fileList = null;
            _surface.Child = null;
            _documentBitmap?.Dispose();
            _documentBitmap = null;
            switch (Document)
            {
                case PinnedHtmlDocument html when !IgnoreFormat:
                    var rich = PinnedHtmlView.Create(html, _htmlResources ??= PinnedHtmlView.CreateResources());
                    rich.LinkRequested += OpenHtmlLink;
                    rich.SceneContentChanged += (_, _) => DocumentAnnotationLayoutChanged(true);
                    _textView = rich;
                    _surface.Child = rich;
                    break;
                case PinnedFormulaDocument formula:
                    var formulaImage = new Image
                    {
                        Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly,
                    };
                    _surface.Child = new Border
                    {
                        Background = Brushes.White,
                        Child = formulaImage,
                    };
                    _formulaPngTask = RenderFormulaAsync(formula, formulaImage);
                    break;
                case PinnedFilesDocument:
                    _surface.Padding = new(0);
                    _surface.Background = Brushes.White;
                    _fileList = CreateFileGrid(true);
                    _surface.Child = _fileList;
                    break;
                case PinnedColorDocument color:
                    _textView = new PinnedTextView(color.Value) { FontSize = 16 };
                    _surface.Child = new Grid
                    {
                        RowDefinitions = new("*,Auto"), MinHeight = 140,
                        Children =
                        {
                            new Border { Background = new SolidColorBrush(Color.Parse(color.Value)), MinHeight = 100 },
                            new Border { Padding = new(0, 8, 0, 0), Child = _textView.Control, [Grid.RowProperty] = 1 },
                        },
                    };
                    break;
                default:
                    _textView = new PinnedTextView(Document.PlainText);
                    _surface.Child = _textView.Control;
                    break;
            }

            DocumentAnnotationLayoutChanged(true);
            if (_isThumbnail) ApplyDocumentThumbnailGeometry();
            if (_textView is not null)
            {
                _textView.SetSelectable(Selectable && (!_isThumbnail || PreferencesProvider().ThumbnailSelectableText));
                _textView.ScrollOffsetChanged += (_, _) =>
                {
                    DocumentAnnotationLayoutChanged(false);
                    StateChanged?.Invoke(this, EventArgs.Empty);
                };
            }
        }

        private void SetSelectable(bool value)
        {
            Selectable = value;
            _textView?.SetSelectable(value && (!_isThumbnail || PreferencesProvider().ThumbnailSelectableText));

            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        private async Task<byte[]?> RenderFormulaAsync(PinnedFormulaDocument formula, Image image)
        {
            var token = _lifetime.Token;
            try
            {
                var result = await StructuredRecognitionProcess.Shared.RenderManyPngAsync(
                    formula.Sources, formula.FontSize, formula.TextColor, token);
                token.ThrowIfCancellationRequested();
                if (_closed) return null;
                if (result.Png is not { } encoded)
                {
                    _status.Text = result.ValidationError ?? UiText.Get("FileOperationFailed");
                    _status.IsVisible = true;
                    return null;
                }

                using (var stream = new MemoryStream(encoded)) _documentBitmap = new(stream);
                image.Source = _documentBitmap;
                if (!_restoredLayout && !_isThumbnail)
                {
                    FitInitialContent();
                    RestoreToScreen();
                }

                ApplyDocumentThumbnailRequest();
                if (_isThumbnail) ApplyDocumentThumbnailGeometry();

                return encoded;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                return null;
            }
            catch (Exception error)
            {
                Logger.ZLogError(
                    $"Pinned formula rendering failed. ErrorType={error.GetType().Name} Code={error.HResult}");
                if (!_closed)
                {
                    _status.Text = UiText.Get("FileOperationFailed");
                    _status.IsVisible = true;
                }

                return null;
            }
        }

        private void SetIgnoreFormat(bool value)
        {
            IgnoreFormat = value;
            RebuildContent();
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        private async Task RunAsync(Func<Task> operation)
        {
            if (_closed || _busy) return;
            _busy = true;
            _status.IsVisible = false;
            try
            {
                await operation();
            }
            catch (OperationCanceledException) when (_closed || _lifetime.IsCancellationRequested)
            {
            }
            catch (Exception error)
            {
                Logger.ZLogError(
                    $"Pinned document operation failed. ErrorType={error.GetType().Name} Code={error.HResult}");
                if (!_closed)
                {
                    _status.Text = error.Message;
                    _status.IsVisible = true;
                }
            }
            finally
            {
                _busy = false;
                TryReleaseDocumentSources();
            }
        }
    }
}
