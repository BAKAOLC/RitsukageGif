using Avalonia.Platform.Storage;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    internal sealed partial class AnimationEditorWindow
    {
        private readonly CheckBox _keepRatio = new() { Content = UiText.Get("AnimationKeepRatio"), IsChecked = true };

        private readonly ComboBox _format = new()
            { ItemsSource = new[] { "GIF", "WebP", "APNG", "PNG" }, SelectedIndex = 0, Width = 110 };

        private readonly CompactNumberInput _exportQuality = Number(90, 1, 100);
        private readonly CompactNumberInput _colors = Number(256, 2, 256);
        private readonly CheckBox _lossless = new() { Content = UiText.Get("AnimationLossless"), IsChecked = true };
        private readonly CheckBox _dither = new() { Content = UiText.Get("AnimationDither"), IsChecked = true };
        private readonly TextBox _fontFamily = new() { Width = 140, Text = "sans-serif" };
        private readonly CheckBox _bold = new() { Content = UiText.Get("AnimationBold") };
        private readonly CheckBox _italic = new() { Content = UiText.Get("AnimationItalic") };

        private readonly TextBox _text = new()
            { Watermark = UiText.Get("AnimationText"), AcceptsReturn = true, Height = 60 };

        private readonly TextBox _color = new() { Text = "#FFFFFFFF", Width = 110 };
        private readonly CompactNumberInput _brush = Number(6, 1, 512);
        private readonly CompactNumberInput _font = Number(28, 1, 512);
        private readonly CompactNumberInput _opacity = Number(100, 0, 100);
        private readonly CompactNumberInput _imageWidth = Number(100, 1, 16384);
        private readonly CompactNumberInput _imageHeight = Number(100, 1, 16384);

        private Control BuildProperties()
        {
            var animation = Page();
            animation.Children.Add(Row("AnimationDelay", _delay));
            animation.Children.Add(ActionButton("AnimationSetDelay", () =>
            {
                var frames = SelectedFrames();
                var delay = Value(_delay);
                return MutateAsync(d => d.SetDelay(frames, delay));
            }));
            animation.Children.Add(Row("AnimationLoops", _loops));
            animation.Children.Add(ActionButton("AnimationSetLoops", () =>
            {
                var loops = (uint)Value(_loops);
                return MutateAsync(d => d.SetIterations(loops));
            }));
            animation.Children.Add(Row("AnimationWidth", _width));
            animation.Children.Add(Row("AnimationHeight", _height));
            animation.Children.Add(_keepRatio);
            _width.LostFocus += (_, _) =>
            {
                if (_keepRatio.IsChecked == true && _document is { } d)
                    _height.Value = Math.Max(1, (int)Math.Round(Value(_width) * (double)d.Height / d.Width));
            };
            _height.LostFocus += (_, _) =>
            {
                if (_keepRatio.IsChecked == true && _document is { } d)
                    _width.Value = Math.Max(1, (int)Math.Round(Value(_height) * (double)d.Width / d.Height));
            };
            animation.Children.Add(ActionButton("AnimationResize", () =>
            {
                var options = new MediaProcessOptions { Width = Value(_width), Height = Value(_height) };
                return EditAsync((d, token) => d.Transform(options, token));
            }));
            animation.Children.Add(ActionButton("AnimationCrop", CropAsync));
            animation.Children.Add(ActionButton("AnimationRotate",
                () => EditAsync((d, token) => d.Transform(new() { Rotation = 90 }, token))));
            animation.Children.Add(ActionButton("AnimationFlipHorizontal",
                () => EditAsync((d, token) => d.Transform(new() { FlipHorizontal = true }, token))));
            animation.Children.Add(ActionButton("AnimationFlipVertical",
                () => EditAsync((d, token) => d.Transform(new() { FlipVertical = true }, token))));
            animation.Children.Add(Row("AnimationFormat", _format));
            animation.Children.Add(Row("AnimationExportQuality", _exportQuality));
            animation.Children.Add(Row("AnimationColors", _colors));
            animation.Children.Add(_lossless);
            animation.Children.Add(_dither);
            animation.Children.Add(new TextBlock
                { Text = UiText.Get("AnimationExportHint"), TextWrapping = TextWrapping.Wrap });
            var drawing = BuildDrawing();
            var effects = Page();
            var amount = Number(8, 1, 256);
            effects.Children.Add(Row("AnimationEffectSize", amount));
            foreach (var (key, kind) in new[]
                     {
                         ("AnimationMosaic", PixelEffectKind.Mosaic), ("AnimationBlur", PixelEffectKind.Blur),
                         ("AnimationGrayscale", PixelEffectKind.Grayscale), ("AnimationInvert", PixelEffectKind.Invert)
                     })
                effects.Children.Add(ActionButton(key,
                    () => ApplyEffectAsync(new(kind, EffectRegion(), BlockSize: Value(amount),
                        Radius: Value(amount)))));
            var brightness = Number(0, -100, 100);
            var contrast = Number(100, 0, 800);
            effects.Children.Add(Row("AnimationBrightness", brightness));
            effects.Children.Add(Row("AnimationContrast", contrast));
            effects.Children.Add(ActionButton("AnimationApplyEffect",
                () => ApplyEffectAsync(new(PixelEffectKind.BrightnessContrast, EffectRegion(),
                    Brightness: Value(brightness) / 100d, Contrast: Value(contrast) / 100d))));
            effects.Children.Add(new TextBlock
            {
                Text = UiText.Get("AnimationEffectHint"), TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 0)
            });
            return new TabControl
            {
                Margin = new(8, 0, 0, 0), ItemsSource = new[]
                {
                    new TabItem { Header = UiText.Get("AnimationFrames"), Content = Scroll(animation) },
                    new TabItem { Header = UiText.Get("AnimationDrawing"), Content = Scroll(drawing) },
                    new TabItem { Header = UiText.Get("AnimationEffects"), Content = Scroll(effects) }
                }
            };
        }

        private static StackPanel Page() => new() { Margin = new(8), Spacing = 4 };

        private static ScrollViewer Scroll(Control content) => new()
        {
            Content = content, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled
        };

        private StackPanel BuildDrawing()
        {
            var panel = Page();
            var tools = new ComboBox
            {
                ItemsSource = new[]
                {
                    UiText.Get("AnimationSelect"), UiText.Get("AnimationPan"), UiText.Get("AnimationBrush"),
                    UiText.Get("AnimationEraser"), UiText.Get("AnimationLine"), UiText.Get("AnimationRectangle"),
                    UiText.Get("AnimationEllipse"), UiText.Get("AnimationSelection")
                },
                SelectedIndex = 0, Width = 140
            };
            tools.SelectionChanged += (_, _) => _canvas.Tool = (AnimationEditorTool)Math.Max(0, tools.SelectedIndex);
            panel.Children.Add(Row("AnimationTool", tools));
            var palette = new WrapPanel();
            foreach (var color in new[]
                     {
                         Colors.Black, Colors.White, Colors.Red, Colors.Orange, Colors.Yellow, Colors.Green,
                         Colors.DeepSkyBlue, Colors.Purple
                     })
            {
                var swatch = new Button
                {
                    Width = 23, Height = 23, MinHeight = 0, Padding = new(0), Margin = new(2),
                    Background = new SolidColorBrush(color)
                };
                ToolTip.SetTip(swatch, color.ToString());
                swatch.Click += (_, _) =>
                {
                    _color.Text = color.ToString();
                    _canvas.BrushColor = color;
                };
                palette.Children.Add(swatch);
            }

            panel.Children.Add(palette);
            panel.Children.Add(Row("AnimationColor", _color));
            panel.Children.Add(Row("AnimationBrushSize", _brush));
            var filled = new CheckBox { Content = UiText.Get("AnimationFilled") };
            filled.IsCheckedChanged += (_, _) => _canvas.Filled = filled.IsChecked == true;
            panel.Children.Add(filled);
            panel.Children.Add(ActionButton("AnimationSetBrush", () => RunAsync(_ =>
            {
                _canvas.BrushColor = Color.Parse(_color.Text ?? "#FFFFFFFF");
                _canvas.BrushSize = Value(_brush);
                return Task.CompletedTask;
            })));
            panel.Children.Add(_text);
            panel.Children.Add(Row("AnimationFontSize", _font));
            panel.Children.Add(Row("AnimationFontFamily", _fontFamily));
            var style = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
            style.Children.Add(_bold);
            style.Children.Add(_italic);
            panel.Children.Add(style);
            panel.Children.Add(ActionButton("AnimationAddText", () => RunAsync(_ =>
            {
                _canvas.AddText(_text.Text ?? "", Value(_font), Color.Parse(_color.Text ?? "#FFFFFFFF"),
                    _fontFamily.Text, _bold.IsChecked == true, _italic.IsChecked == true, Value(_opacity) / 100d);
                return Task.CompletedTask;
            })));
            panel.Children.Add(ActionButton("AnimationUpdateText", () => RunAsync(_ =>
            {
                _canvas.UpdateSelectedText(_text.Text ?? "", Value(_font), Color.Parse(_color.Text ?? "#FFFFFFFF"),
                    _fontFamily.Text, _bold.IsChecked == true, _italic.IsChecked == true, Value(_opacity) / 100d);
                return Task.CompletedTask;
            })));
            panel.Children.Add(ActionButton("AnimationAddImage", AddImageAsync));
            panel.Children.Add(Row("AnimationWidth", _imageWidth));
            panel.Children.Add(Row("AnimationHeight", _imageHeight));
            panel.Children.Add(Row("AnimationOpacity", _opacity));
            panel.Children.Add(ActionButton("AnimationUpdateImage", () => RunAsync(_ =>
            {
                _canvas.UpdateSelectedImage(Value(_imageWidth), Value(_imageHeight), Value(_opacity) / 100d);
                return Task.CompletedTask;
            })));
            panel.Children.Add(ActionButton("AnimationDeleteLayer", () =>
            {
                _canvas.DeleteSelected();
                return Task.CompletedTask;
            }));
            panel.Children.Add(ActionButton("AnimationApplyDrawing", ApplyDrawingAsync));
            panel.Children.Add(ActionButton("AnimationApplyWatermark", ApplyWatermarkAsync));
            panel.Children.Add(ActionButton("AnimationResetDraft",
                () => RunAsync(token => _canvas.ResetDraftAsync(token))));
            panel.Children.Add(ActionButton("AnimationFit", () =>
            {
                _canvas.Fit();
                return Task.CompletedTask;
            }));
            _canvas.SelectionChanged += (_, _) =>
            {
                if (_canvas.SelectedText is { } text)
                {
                    _text.Text = text;
                    _font.Value = (decimal)_canvas.SelectedTextSize;
                    _fontFamily.Text = _canvas.SelectedTextFamily;
                    _color.Text = _canvas.SelectedTextColor.ToString();
                    _bold.IsChecked = _canvas.SelectedTextBold;
                    _italic.IsChecked = _canvas.SelectedTextItalic;
                    _opacity.Value = (decimal)(_canvas.SelectedOpacity * 100);
                }

                if (_canvas.SelectedImageSize is { } size)
                {
                    _imageWidth.Value = (decimal)size.Width;
                    _imageHeight.Value = (decimal)size.Height;
                    _opacity.Value = (decimal)(_canvas.SelectedOpacity * 100);
                }
            };
            panel.Children.Add(new TextBlock
                { Text = UiText.Get("AnimationDrawingHint"), TextWrapping = TextWrapping.Wrap });
            return panel;
        }

        private PixelRect? EffectRegion() => _canvas.Selection is { } rect
            ? new((int)rect.X, (int)rect.Y, Math.Max(1, (int)rect.Width), Math.Max(1, (int)rect.Height))
            : null;

        private Task ApplyEffectAsync(PixelEffect effect)
        {
            var frames = SelectedFrames();
            return EditAsync((d, token) => d.ApplyEffects(frames, [effect], token));
        }

        private Task CropAsync()
        {
            if (EffectRegion() is not { } region)
            {
                _status.Text = UiText.Get("AnimationSelectRegion");
                return Task.CompletedTask;
            }

            return EditAsync((d, token) =>
                d.Transform(
                    new() { CropX = region.X, CropY = region.Y, CropWidth = region.Width, CropHeight = region.Height },
                    token));
        }

        private async Task AddImageAsync()
        {
            var files = await StorageProvider.OpenFilePickerAsync(new()
                { Title = UiText.Get("AnimationAddImage"), FileTypeFilter = [AnimationFiles()] });
            if (files.Count > 0 && files[0].TryGetLocalPath() is { } path)
                await RunAsync(token => _canvas.AddImageAsync(path, token: token));
        }

        private async Task ApplyDrawingAsync()
        {
            if (!_canvas.HasChanges) return;
            var index = CurrentFrame;
            await RunAsync(async token =>
            {
                var bytes = _canvas.ExportPng();
                await Task.Run(() => _document!.ReplaceFrames([index], bytes, token), token);
                _dirty = true;
                await _canvas.LoadFrameAsync(_document!.Frames[CurrentFrame].Path);
                _displayedPath = _document.Frames[CurrentFrame].Path;
                _dirty = true;
                Refresh();
            });
        }

        private async Task ApplyWatermarkAsync()
        {
            if (!_canvas.HasOverlay) return;
            if (_canvas.HasRasterChanges)
            {
                _status.Text = UiText.Get("AnimationApplyFirst");
                return;
            }

            var frames = SelectedFrames();
            await RunAsync(async token =>
            {
                var bytes = _canvas.ExportOverlayPng();
                var path = Path.Combine(Path.GetTempPath(), $"ritsukage-watermark-{Guid.NewGuid():N}.png");
                try
                {
                    await File.WriteAllBytesAsync(path, bytes, token);
                    await Task.Run(() => _document!.Overlay(frames, path, token: token), token);
                    _dirty = true;
                    await _canvas.LoadFrameAsync(_document!.Frames[CurrentFrame].Path);
                    _displayedPath = _document.Frames[CurrentFrame].Path;
                    _dirty = true;
                    Refresh();
                }
                finally
                {
                    File.Delete(path);
                }
            });
        }
    }
}
