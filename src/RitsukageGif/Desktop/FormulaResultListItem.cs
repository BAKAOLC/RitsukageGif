using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace RitsukageGif.Desktop
{
    internal sealed class FormulaResultListItem : ListBoxItem, IDisposable
    {
        private static readonly SemaphoreSlim ThumbnailSlot = new(1, 1);
        private static readonly ILogger Logger = LogHub.GetLogger("Desktop.FormulaResults");
        private readonly int _index;
        private readonly CancellationToken _lifetime;
        private readonly TextBlock _number = new() { VerticalAlignment = VerticalAlignment.Center };

        private readonly TextBlock _source = new()
        {
            TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center,
        };

        private readonly Image _thumbnail = new() { Stretch = Stretch.Uniform, MaxHeight = 38 };
        private bool _attached;
        private Bitmap? _bitmap;
        private uint _cachedColor;
        private byte[]? _cachedPng;
        private double _cachedScale;
        private bool _disposed;
        private string _latex;
        private CancellationTokenSource? _renderWork;
        private uint _requestedColor;
        private string? _requestedLatex;
        private double _requestedScale;
        private byte[]? _shownPng;

        public FormulaResultListItem(string latex, int index, Action remove, CancellationToken lifetime)
        {
            _latex = latex;
            _index = index;
            _lifetime = lifetime;
            MinHeight = 44;
            Padding = new(4, 2);
            HorizontalContentAlignment = HorizontalAlignment.Stretch;
            _number.Text = index.ToString(CultureInfo.CurrentCulture);
            AutomationProperties.SetName(this, UiText.Format("FormulaEntry", index));
            var delete = new Button { Width = 24, Height = 24, Padding = new(2), Focusable = false };
            delete.Content = ToolbarIcon.Create(delete, ToolbarIcon.GetDefinition("AnimationDelete"));
            delete.Bind(ToolTip.TipProperty, LiveText.Resource("FormulaRemove"));
            delete.Bind(AutomationProperties.NameProperty, LiveText.Resource("FormulaRemove"));
            delete.Click += (_, args) =>
            {
                args.Handled = true;
                if (!_disposed) remove();
            };
            var content = new Grid { ColumnDefinitions = new("Auto,*,Auto"), ColumnSpacing = 4 };
            content.Children.Add(_number);
            var preview = new Grid { Children = { _thumbnail, _source } };
            Grid.SetColumn(preview, 1);
            content.Children.Add(preview);
            Grid.SetColumn(delete, 2);
            content.Children.Add(delete);
            Content = content;
            UpdateSourceText();
            AttachedToVisualTree += (_, _) =>
            {
                _attached = true;
                RequestThumbnail();
            };
            LayoutUpdated += (_, _) =>
            {
                if (_attached && !(TopLevel.GetTopLevel(this)?.RenderScaling ?? 1).Equals(_requestedScale))
                    RequestThumbnail();
            };
            DetachedFromVisualTree += (_, _) =>
            {
                _attached = false;
                _renderWork?.Cancel();
                _thumbnail.Source = null;
                _shownPng = null;
                _bitmap?.Dispose();
                _bitmap = null;
            };
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _renderWork?.Cancel();
            _cachedPng = null;
            _shownPng = null;
            _thumbnail.Source = null;
            _bitmap?.Dispose();
            _bitmap = null;
        }

        public void SetLatex(string latex)
        {
            if (_latex == latex || _disposed) return;
            _latex = latex;
            _cachedPng = null;
            UpdateSourceText();
            RequestThumbnail();
        }

        public void RefreshLanguage()
        {
            AutomationProperties.SetName(this, UiText.Format("FormulaEntry", _index));
        }

        public void RefreshTheme()
        {
            RequestThumbnail();
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);
            if (change.Property == ForegroundProperty && _attached) RequestThumbnail();
        }

        private void UpdateSourceText()
        {
            _source.Text = _latex.ReplaceLineEndings(" ");
            ToolTip.SetTip(this, _latex);
        }

        private void RequestThumbnail()
        {
            if (_disposed || !_attached || !IsEffectivelyVisible || _lifetime.IsCancellationRequested) return;
            var color = (Foreground as ISolidColorBrush)?.Color ?? AppTheme.Ink.Color;
            var argb = (uint)((color.A << 24) | (color.R << 16) | (color.G << 8) | color.B);
            var scale = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
            if (_cachedPng is { } png && _cachedColor == argb && _cachedScale.Equals(scale))
            {
                ShowThumbnail(png, scale);
                return;
            }

            if (_renderWork is { IsCancellationRequested: false } && _requestedColor == argb &&
                _requestedScale.Equals(scale) && _requestedLatex == _latex) return;
            _renderWork?.Cancel();
            _requestedColor = argb;
            _requestedLatex = _latex;
            _requestedScale = scale;
            var work = CancellationTokenSource.CreateLinkedTokenSource(_lifetime);
            _renderWork = work;
            _ = RenderThumbnailAsync(_latex, argb, scale, work);
        }

        private async Task RenderThumbnailAsync(string latex, uint color, double scale,
            CancellationTokenSource work)
        {
            var entered = false;
            try
            {
                await Task.Delay(150, work.Token);
                await ThumbnailSlot.WaitAsync(work.Token);
                entered = true;
                var rendered =
                    await StructuredRecognitionProcess.Shared.RenderPngAsync(latex, (float)(14 * scale), color,
                        work.Token);
                work.Token.ThrowIfCancellationRequested();
                if (_disposed || !_attached || !IsEffectivelyVisible || !ReferenceEquals(_renderWork, work)) return;
                if (rendered.Png is not { } png || rendered.ValidationError is not null)
                {
                    ShowSourceFallback();
                    return;
                }

                _cachedColor = color;
                _cachedScale = scale;
                _cachedPng = png.Length <= 256 * 1024 ? png : null;
                ShowThumbnail(png, scale);
            }
            catch (OperationCanceledException) when (work.IsCancellationRequested)
            {
            }
            catch (Exception error)
            {
                Logger.ZLogDebug(error, $"Formula thumbnail rendering failed.");
                if (!_disposed && _attached && ReferenceEquals(_renderWork, work)) ShowSourceFallback();
            }
            finally
            {
                if (entered) ThumbnailSlot.Release();
                if (ReferenceEquals(_renderWork, work)) _renderWork = null;
                work.Dispose();
            }
        }

        private void ShowThumbnail(byte[] png, double scale)
        {
            if (_bitmap is not null && ReferenceEquals(_shownPng, png)) return;
            using var input = new MemoryStream(png, false);
            var bitmap = new Bitmap(input);
            _thumbnail.Source = null;
            _bitmap?.Dispose();
            _bitmap = bitmap;
            _shownPng = png;
            _thumbnail.Source = _bitmap;
            _thumbnail.Height = Math.Min(38, bitmap.PixelSize.Height / scale);
            _thumbnail.IsVisible = true;
            _source.IsVisible = false;
        }

        private void ShowSourceFallback()
        {
            _thumbnail.Source = null;
            _bitmap?.Dispose();
            _bitmap = null;
            _shownPng = null;
            _thumbnail.IsVisible = false;
            _source.IsVisible = true;
        }
    }
}
