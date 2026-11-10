using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace RitsukageGif.Desktop
{
    internal sealed class PinnedFileRowButton : Button
    {
        private static readonly ILogger Logger = LogHub.GetLogger("Desktop.Pins.FileRows");
        private readonly PinnedFileDisplayEntry _entry;
        private readonly string _path;
        private readonly Image _icon;
        private readonly TextBlock _sizeLabel;
        private readonly CancellationToken _lifetime;
        private readonly bool _automatic;
        private Bitmap? _bitmap;
        private Task? _iconTask;
        private int _generation;
        private int _requestedPixelSize;
        private double _imageScale;
        private TopLevel? _topLevel;
        private CancellationTokenSource? _iconRequest;

        internal PinnedFileRowButton(PinnedFileDisplayEntry entry, bool automatic, double imageScale,
            CancellationToken lifetime)
        {
            _entry = entry;
            _path = entry.Path!;
            _lifetime = lifetime;
            _automatic = automatic;
            _imageScale = imageScale;
            Tag = _path;
            Padding = new(5);
            Width = 250;
            Height = 58;
            CornerRadius = new(0);
            Focusable = false;
            Background = Brushes.Transparent;
            BorderThickness = new(0);
            HorizontalAlignment = HorizontalAlignment.Left;
            HorizontalContentAlignment = HorizontalAlignment.Left;
            _icon = new() { Width = 48, Height = 48, Stretch = Stretch.Uniform };
            _sizeLabel = new()
            {
                FontSize = 12, Foreground = new SolidColorBrush(Color.FromRgb(153, 153, 153)),
            };
            var names = new StackPanel
            {
                Margin = new(5, 0, 0, 0), Width = 187,
                Children =
                {
                    new TextBlock
                    {
                        Text = Path.GetFileName(Path.TrimEndingDirectorySeparator(_path)),
                        FontSize = 13, Foreground = Brushes.Black,
                        TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis, MaxLines = 2,
                    },
                    _sizeLabel,
                },
            };
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal, Children = { _icon, names },
            };
            ToolTip.SetTip(this, _path);
            Loaded += (_, _) =>
            {
                if (!_automatic) return;
                _topLevel = TopLevel.GetTopLevel(this);
                if (_topLevel is not null) _topLevel.ScalingChanged += ScalingChanged;
                _ = LoadForDisplayAsync();
            };
            Unloaded += (_, _) =>
            {
                if (_topLevel is not null) _topLevel.ScalingChanged -= ScalingChanged;
                _topLevel = null;
                ReleaseIcon();
            };
        }

        private void ScalingChanged(object? sender, EventArgs args)
        {
            RefreshIconSampling(_imageScale);
        }

        internal void RefreshIconSampling(double imageScale)
        {
            _imageScale = imageScale;
            if (RequestedPixelSize() == _requestedPixelSize) return;
            _generation++;
            _iconRequest?.Cancel();
            _iconTask = null;
            if (_automatic && IsLoaded) _ = LoadForDisplayAsync();
        }

        private int RequestedPixelSize()
        {
            return Math.Max(1, (int)Math.Ceiling(48 * _imageScale * (TopLevel.GetTopLevel(this)?.RenderScaling ?? 1)));
        }

        internal Task EnsureIconAsync(CancellationToken token)
        {
            _iconTask ??= LoadIconAsync(_generation);
            return _iconTask.WaitAsync(token);
        }

        private async Task LoadForDisplayAsync()
        {
            try
            {
                await (_iconTask ??= LoadIconAsync(_generation));
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                Logger.ZLogWarning(
                    $"File row icon loading failed. ErrorType={error.GetType().Name} Code={error.HResult}");
            }
        }

        private async Task LoadIconAsync(int generation)
        {
            using var request = CancellationTokenSource.CreateLinkedTokenSource(_lifetime);
            _iconRequest = request;
            var token = request.Token;
            _requestedPixelSize = RequestedPixelSize();
            try
            {
                var label = await _entry.ReadSizeAsync(_lifetime);
                token.ThrowIfCancellationRequested();
                var bytes = await PinnedFileIconProvider.ReadAsync(_path, _requestedPixelSize, token);
                token.ThrowIfCancellationRequested();
                if (generation != _generation) return;
                _sizeLabel.Text = label;
                if (bytes is null) return;
                using var input = new MemoryStream(bytes, false);
                var bitmap = new Bitmap(input);
                _icon.Source = bitmap;
                _bitmap?.Dispose();
                _bitmap = bitmap;
            }
            finally
            {
                if (ReferenceEquals(_iconRequest, request)) _iconRequest = null;
            }
        }

        private void ReleaseIcon()
        {
            _generation++;
            _iconRequest?.Cancel();
            _iconTask = null;
            _icon.Source = null;
            _bitmap?.Dispose();
            _bitmap = null;
        }
    }
}
