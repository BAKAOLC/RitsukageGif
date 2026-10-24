using Avalonia.Platform.Storage;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    internal sealed partial class AnimationEditorWindow
    {
        private readonly CheckBox _keepRatio = new()
            { [!ContentControl.ContentProperty] = LiveText.Resource("AnimationKeepRatio"), IsChecked = true };

        private readonly ComboBox _format = new()
            { ItemsSource = new[] { "GIF", "WebP", "APNG", "PNG" }, SelectedIndex = 0, Width = 110 };

        private readonly CompactNumberInput _exportQuality = Number(90, 1, 100);
        private readonly CompactNumberInput _colors = Number(256, 2, 256);

        private readonly CheckBox _lossless = new()
            { [!ContentControl.ContentProperty] = LiveText.Resource("AnimationLossless"), IsChecked = true };

        private readonly CheckBox _dither = new()
            { [!ContentControl.ContentProperty] = LiveText.Resource("AnimationDither"), IsChecked = true };

        private readonly TextBox _fontFamily = new() { Width = 140, Text = "sans-serif" };

        private readonly CheckBox _bold = new()
            { [!ContentControl.ContentProperty] = LiveText.Resource("AnimationBold") };

        private readonly CheckBox _italic = new()
            { [!ContentControl.ContentProperty] = LiveText.Resource("AnimationItalic") };

        private readonly TextBox _text = new()
            { [!TextBox.WatermarkProperty] = LiveText.Resource("AnimationText"), AcceptsReturn = true, Height = 60 };

        private readonly TextBox _color = new() { Text = "#FFFFFFFF", Width = 110 };
        private readonly CompactNumberInput _brush = Number(6, 1, 512);
        private readonly CompactNumberInput _font = Number(28, 1, 512);
        private readonly CompactNumberInput _opacity = Number(100, 0, 100);
        private readonly CompactNumberInput _imageWidth = Number(100, 1, 16384);
        private readonly CompactNumberInput _imageHeight = Number(100, 1, 16384);

        private readonly ContentControl _propertyContent = new();
        private readonly TextBlock _propertyTitle = new() { FontWeight = FontWeight.SemiBold };
        private readonly Button _applyDraft = new();
        private readonly Border _draftFooter = new();

        private readonly ComboBox _draftScope = new()
        {
            ItemsSource = DraftScopes(),
            SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch, Padding = new(6, 2)
        };

        private readonly Button _discardDraft = new();
        private readonly CompactNumberInput _layerX = Number(0, -16384, 16384);
        private readonly CompactNumberInput _layerY = Number(0, -16384, 16384);
        private readonly CompactNumberInput _effectAmount = Number(8, 1, 256);
        private readonly CompactNumberInput _brightness = Number(0, -100, 100);
        private readonly CompactNumberInput _contrast = Number(100, 0, 800);

        private readonly CheckBox _filled = new()
            { [!ContentControl.ContentProperty] = LiveText.Resource("AnimationFilled") };

        private bool _syncingProperties;

        private Control BuildProperties()
        {
            var root = new Grid
                { Name = "AnimationProperties", RowDefinitions = new("Auto,*,Auto") };
            _propertyTitle.Margin = new(10, 10, 10, 8);
            var heading = new DockPanel { Background = AppTheme.SurfaceTint };
            var close = ActionButton("AnimationCloseProperties", () =>
            {
                SetPropertiesVisible(false);
                return Task.CompletedTask;
            });
            close.Classes.Add("icon-button");
            close.Classes.Add("Secondary");
            close.Content = new Avalonia.Controls.Shapes.Path
            {
                Data = Geometry.Parse("M2,2 L12,12 M12,2 L2,12"), Stroke = AppTheme.Muted,
                StrokeThickness = 1.5,
                Width = 14, Height = 14
            };
            close.Bind(ToolTip.TipProperty, LiveText.Resource("AnimationCloseProperties"));
            DockPanel.SetDock(close, Dock.Right);
            heading.Children.Add(close);
            heading.Children.Add(_propertyTitle);
            root.Children.Add(heading);
            var scroll = Scroll(_propertyContent);
            Grid.SetRow(scroll, 1);
            root.Children.Add(scroll);
            var actions = new StackPanel { Margin = new(8), Spacing = 4 };
            actions.Children.Add(_draftScope);
            _draftScope.Name = "DraftApplyScope";
            _draftScope.Bind(ToolTip.TipProperty, LiveText.Resource("AnimationDraftScopeHint"));
            var buttons = new Grid { ColumnDefinitions = new("*,*") };
            foreach (var button in new[] { _applyDraft, _discardDraft })
            {
                button.HorizontalAlignment = HorizontalAlignment.Stretch;
                button.HorizontalContentAlignment = HorizontalAlignment.Center;
                button.Padding = new(6, 3);
                buttons.Children.Add(button);
            }

            WithActionIcon(_applyDraft, "AnimationApplyDraft", "M3,12 L9,18 21,5", "AnimationApplyDraft");
            WithActionIcon(_discardDraft, "AnimationDiscardDraft", "M5,5 L19,19 M19,5 L5,19", "AnimationDiscardDraft");
            _applyDraft.Classes.Add("primary");
            _applyDraft.Name = "ApplyDraft";
            _discardDraft.Name = "DiscardDraft";
            Grid.SetColumn(_discardDraft, 1);
            _applyDraft.Margin = new(0, 0, 4, 0);
            _applyDraft.Click += async (_, _) =>
            {
                if (_draftScope.SelectedIndex == 1) await ApplyWatermarkAsync();
                else await ApplyDrawingAsync();
            };
            _discardDraft.Click += async (_, _) => await RunAsync(token => _canvas.ResetDraftAsync(token));
            actions.Children.Add(buttons);
            _draftFooter.Name = "DraftFooter";
            _draftFooter.Child = actions;
            _draftFooter.Background = AppTheme.SurfaceTint;
            _draftFooter.BorderBrush = AppTheme.Outline;
            _draftFooter.BorderThickness = new(0, 1, 0, 0);
            Grid.SetRow(_draftFooter, 2);
            root.Children.Add(_draftFooter);
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
            _color.LostFocus += (_, _) => UpdateBrush();
            _brush.LostFocus += (_, _) => UpdateBrush();
            _filled.IsCheckedChanged += (_, _) => _canvas.Filled = _filled.IsChecked == true;
            ShowProperties("AnimationFrames");
            UpdateDraftActions();
            return new Border
            {
                Background = AppTheme.Surface, BorderBrush = AppTheme.Outline,
                BorderThickness = new(1), CornerRadius = new(6), ClipToBounds = true, Margin = new(8, 0, 0, 0),
                Child = root
            };
        }

        private void UpdateDraftActions()
        {
            _draftFooter.IsVisible = _canvas.HasChanges || _propertyMode is
                "AnimationText" or "AnimationImageProperties" or "AnimationSelect" or
                "AnimationBrush" or "AnimationEraser" or "AnimationLine" or "AnimationRectangle" or "AnimationEllipse";
            _applyDraft.IsEnabled = _canvas.HasChanges;
            _draftScope.IsEnabled = _canvas.HasOverlay && !_canvas.HasRasterChanges;
            if (!_draftScope.IsEnabled) _draftScope.SelectedIndex = 0;
            _discardDraft.IsEnabled = _canvas.HasChanges;
        }

        private static void Detach(Control control)
        {
            if (control.Parent is Panel panel) panel.Children.Remove(control);
            else if (control.Parent is ContentControl content) content.Content = null;
        }

        private static void AddControl(StackPanel panel, Control control)
        {
            Detach(control);
            panel.Children.Add(control);
        }

        private void ShowProperties(string mode, bool reveal = true)
        {
            if (reveal) SetPropertiesVisible(true);
            _propertyMode = mode;
            _propertyTitle.Bind(TextBlock.TextProperty, LiveText.Resource(mode));
            _canvas.Tool = AnimationEditorTool.Select;
            foreach (var (key, button) in _toolButtons)
            {
                button.Classes.Set("active-tool", key == mode);
                button.Classes.Set("Primary", key == mode);
                button.Classes.Set("Secondary", key != mode);
            }

            var panel = Page();
            _propertyContent.Content = panel;
            switch (mode)
            {
                case "AnimationFrames":
                    panel.Children.Add(Row("AnimationDelay", _delay));
                    panel.Children.Add(ActionButton("AnimationSetDelay", () =>
                    {
                        var frames = SelectedFrames();
                        if (!HasFrameSelection(frames)) return Task.CompletedTask;
                        var delay = Value(_delay);
                        return MutateAsync(d => d.SetDelay(frames, delay));
                    }));
                    panel.Children.Add(Row("AnimationLoops", _loops));
                    panel.Children.Add(ActionButton("AnimationSetLoops", () =>
                    {
                        var loops = (uint)Value(_loops);
                        return MutateAsync(d => d.SetIterations(loops));
                    }));
                    Hint(panel, "AnimationTimingHint");
                    break;
                case "AnimationResize":
                    panel.Children.Add(FieldPair("AnimationWidth", _width, "AnimationHeight", _height));
                    AddControl(panel, _keepRatio);
                    panel.Children.Add(ActionButton("AnimationResize", () =>
                    {
                        var options = new MediaProcessOptions { Width = Value(_width), Height = Value(_height) };
                        return EditAsync((d, t) => d.Transform(options, t));
                    }));
                    Hint(panel, "AnimationWholeAnimationHint");
                    break;
                case "AnimationCrop":
                case "AnimationSelection":
                    _canvas.Tool = AnimationEditorTool.Selection;
                    Hint(panel, "AnimationSelectRegion");
                    panel.Children.Add(ActionButton("AnimationCrop", CropAsync));
                    Hint(panel, "AnimationWholeAnimationHint");
                    break;
                case "AnimationExport":
                    panel.Children.Add(Row("AnimationFormat", _format));
                    panel.Children.Add(Row("AnimationExportQuality", _exportQuality));
                    panel.Children.Add(Row("AnimationColors", _colors));
                    AddControl(panel, _lossless);
                    AddControl(panel, _dither);
                    panel.Children.Add(ActionButton("AnimationExport", ExportAsync));
                    Hint(panel, "AnimationExportHint");
                    break;
                case "AnimationText":
                    BuildTextProperties(panel);
                    break;
                case "AnimationImageProperties":
                case "AnimationSelect":
                    BuildImageProperties(panel);
                    break;
                case "AnimationEffects":
                    _canvas.Tool = AnimationEditorTool.Selection;
                    panel.Children.Add(Row("AnimationEffectSize", _effectAmount));
                    var effects = new[]
                    {
                        ("AnimationMosaic", PixelEffectKind.Mosaic), ("AnimationBlur", PixelEffectKind.Blur),
                        ("AnimationGrayscale", PixelEffectKind.Grayscale), ("AnimationInvert", PixelEffectKind.Invert)
                    };
                    foreach (var row in effects.Chunk(2))
                        panel.Children.Add(ActionGroup(row.Select(effect => CompactAction(effect.Item1, effect.Item1,
                            () => ApplyEffectAsync(new(effect.Item2, EffectRegion(), BlockSize: Value(_effectAmount),
                                Radius: Value(_effectAmount))))).ToArray()));
                    panel.Children.Add(FieldPair("AnimationBrightness", _brightness, "AnimationContrast", _contrast));
                    panel.Children.Add(ActionButton("AnimationApplyEffect",
                        () => ApplyEffectAsync(new(PixelEffectKind.BrightnessContrast, EffectRegion(),
                            Brightness: Value(_brightness) / 100d, Contrast: Value(_contrast) / 100d))));
                    Hint(panel, "AnimationEffectHint");
                    break;
                case "AnimationPan":
                    _canvas.Tool = AnimationEditorTool.Pan;
                    Hint(panel, "AnimationPanHint");
                    break;
                default:
                    _canvas.Tool = mode switch
                    {
                        "AnimationEraser" => AnimationEditorTool.Eraser,
                        "AnimationLine" => AnimationEditorTool.Line,
                        "AnimationRectangle" => AnimationEditorTool.Rectangle,
                        "AnimationEllipse" => AnimationEditorTool.Ellipse, _ => AnimationEditorTool.Brush
                    };
                    AddBrushControls(panel, mode);
                    break;
            }

            UpdateDraftActions();
        }

        private void BuildTextProperties(StackPanel panel)
        {
            Section(panel, "AnimationTextContent");
            AddControl(panel, _text);
            panel.Children.Add(FieldPair("AnimationFontFamily", _fontFamily, "AnimationFontSize", _font));
            var styles = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16 };
            Detach(_bold);
            Detach(_italic);
            styles.Children.Add(_bold);
            styles.Children.Add(_italic);
            panel.Children.Add(styles);
            panel.Children.Add(FieldPair("AnimationColor", _color, "AnimationOpacity", _opacity));
            panel.Children.Add(ActionGroup(
                CompactAction("AnimationAddText", "AnimationInsertShort", () => RunAsync(_ =>
                {
                    _canvas.AddText(_text.Text ?? "", Value(_font), Color.Parse(_color.Text ?? "#FFFFFFFF"),
                        _fontFamily.Text, _bold.IsChecked == true, _italic.IsChecked == true, Value(_opacity) / 100d);
                    return Task.CompletedTask;
                })),
                CompactAction("AnimationUpdateText", "AnimationUpdateShort", () => RunAsync(_ =>
                {
                    _canvas.UpdateSelectedText(_text.Text ?? "", Value(_font), Color.Parse(_color.Text ?? "#FFFFFFFF"),
                        _fontFamily.Text, _bold.IsChecked == true, _italic.IsChecked == true, Value(_opacity) / 100d);
                    return Task.CompletedTask;
                }))));
            AddLayerControls(panel);
        }

        private void BuildImageProperties(StackPanel panel)
        {
            Section(panel, "AnimationImageSize");
            panel.Children.Add(FieldPair("AnimationWidth", _imageWidth, "AnimationHeight", _imageHeight));
            panel.Children.Add(Row("AnimationOpacity", _opacity));
            panel.Children.Add(ActionGroup(
                CompactAction("AnimationAddImage", "AnimationInsertShort", AddImageAsync),
                CompactAction("AnimationUpdateImage", "AnimationUpdateShort", () => RunAsync(_ =>
                {
                    _canvas.UpdateSelectedImage(Value(_imageWidth), Value(_imageHeight), Value(_opacity) / 100d);
                    return Task.CompletedTask;
                }))));
            AddLayerControls(panel);
            Hint(panel, "AnimationImageLayerHint");
        }

        private void AddLayerControls(StackPanel panel)
        {
            Section(panel, "AnimationLayerPosition");
            panel.Children.Add(FieldPair("AnimationPositionX", _layerX, "AnimationPositionY", _layerY));
            panel.Children.Add(ActionButton("AnimationSetPosition", () =>
            {
                if (_canvas.SelectedBounds is { } bounds)
                    _canvas.SetSelectedBounds(Value(_layerX), Value(_layerY), bounds.Width, bounds.Height);
                return Task.CompletedTask;
            }));
            Section(panel, "AnimationLayerOrder");
            panel.Children.Add(ActionGroup(
                CompactAction("AnimationBringFront", "AnimationFrontShort", () =>
                {
                    _canvas.BringSelectedToFront();
                    return Task.CompletedTask;
                }),
                CompactAction("AnimationSendBack", "AnimationBackShort", () =>
                {
                    _canvas.SendSelectedToBack();
                    return Task.CompletedTask;
                }),
                CompactAction("AnimationDeleteLayer", "AnimationDeleteShort", () =>
                {
                    _canvas.DeleteSelected();
                    return Task.CompletedTask;
                })));
        }

        private static void Section(StackPanel panel, string key) => panel.Children.Add(new TextBlock
        {
            [!TextBlock.TextProperty] = LiveText.Resource(key), Classes = { "editor-caption" },
            FontWeight = FontWeight.SemiBold,
            Foreground = AppTheme.Muted, Margin = new(0, panel.Children.Count == 0 ? 0 : 8, 0, 0)
        });

        private static Control FieldPair(string firstKey, Control first, string secondKey, Control second,
            string columns = "*,*")
        {
            var grid = new Grid { ColumnDefinitions = new(columns), RowDefinitions = new("Auto,Auto") };
            var values = new[] { (firstKey, first), (secondKey, second) };
            for (var i = 0; i < values.Length; i++)
            {
                var (key, control) = values[i];
                Detach(control);
                control.Width = double.NaN;
                control.HorizontalAlignment = HorizontalAlignment.Stretch;
                control.Margin = new(i == 0 ? 0 : 4, 0, i == 0 ? 4 : 0, 0);
                var label = new TextBlock
                {
                    [!TextBlock.TextProperty] = LiveText.Resource(key), Classes = { "editor-caption" },
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new(i == 0 ? 0 : 4, 0, i == 0 ? 4 : 0, 3)
                };
                Grid.SetColumn(label, i);
                Grid.SetColumn(control, i);
                Grid.SetRow(control, 1);
                grid.Children.Add(label);
                grid.Children.Add(control);
            }

            return grid;
        }

        private static Button CompactAction(string key, string caption, Func<Task> action)
        {
            var button = ActionButton(key, action);
            var geometry = key switch
            {
                "AnimationMosaic" =>
                    "M3,3 L10,3 10,10 3,10 Z M14,3 L21,3 21,10 14,10 Z M3,14 L10,14 10,21 3,21 Z M14,14 L21,14 21,21 14,21 Z",
                "AnimationBlur" => "M12,2 C8,8 4,10 4,15 A8,8 0 0 0 20,15 C20,10 16,8 12,2 Z M8,15 A4,4 0 0 0 12,19",
                "AnimationGrayscale" => "M12,2 A10,10 0 1 0 12,22 A10,10 0 1 0 12,2 M12,2 L12,22 M6,5 L6,19 M9,3 L9,21",
                "AnimationInvert" =>
                    "M12,2 A10,10 0 1 0 12,22 A10,10 0 1 0 12,2 M12,2 L12,22 M15,8 L21,8 M18,5 L18,11 M3,16 L9,16",
                "AnimationAddText" => RibbonIcon("AnimationAddText"),
                "AnimationAddImage" => RibbonIcon("AnimationAddImage"),
                "AnimationUpdateText" or "AnimationUpdateImage" => "M20,9 A9,9 0 1 0 20,17 M20,2 L20,9 13,9",
                "AnimationBringFront" => "M3,8 L16,8 16,21 3,21 Z M8,3 L21,3 21,16 M8,3 L8,6 M18,16 L21,16",
                "AnimationSendBack" => "M8,3 L21,3 21,16 8,16 Z M3,8 L6,8 M3,8 L3,21 16,21 16,18",
                _ => RibbonIcon("AnimationDelete")
            };
            return WithActionIcon(button, key, geometry,
                key is "AnimationBringFront" or "AnimationSendBack" or "AnimationDeleteLayer" ? null : caption);
        }

        private static Control ActionGroup(params Button[] buttons)
        {
            var grid = new Grid { ColumnDefinitions = new(string.Join(",", Enumerable.Repeat("*", buttons.Length))) };
            for (var i = 0; i < buttons.Length; i++)
            {
                var button = buttons[i];
                button.HorizontalAlignment = HorizontalAlignment.Stretch;
                button.Margin = new(i == 0 ? 0 : 3, 0, i == buttons.Length - 1 ? 0 : 3, 0);
                button.Padding = new(4, 4);
                Grid.SetColumn(button, i);
                grid.Children.Add(button);
            }

            return grid;
        }

        private void AddBrushControls(StackPanel panel, string mode)
        {
            if (mode != "AnimationEraser")
            {
                var palette = new WrapPanel();
                foreach (var color in new[]
                         {
                             Colors.Black, Colors.White, Colors.Red, Colors.Orange, Colors.Yellow, Colors.Green,
                             Colors.DeepSkyBlue, Colors.Purple
                         })
                {
                    var swatch = new Button
                    {
                        Width = 22, Height = 22, MinHeight = 0, Padding = new(0), Margin = new(2),
                        Background = new SolidColorBrush(color)
                    };
                    ToolTip.SetTip(swatch, color.ToString());
                    swatch.Click += (_, _) =>
                    {
                        _color.Text = color.ToString();
                        UpdateBrush();
                    };
                    palette.Children.Add(swatch);
                }

                panel.Children.Add(palette);
                panel.Children.Add(Row("AnimationColor", _color));
            }

            panel.Children.Add(Row("AnimationBrushSize", _brush));
            if (mode is "AnimationRectangle" or "AnimationEllipse") AddControl(panel, _filled);
            panel.Children.Add(ActionButton("AnimationSetBrush", () =>
            {
                UpdateBrush();
                return Task.CompletedTask;
            }));
            Hint(panel, "AnimationDrawingHint");
            UpdateBrush();
        }

        private void UpdateBrush()
        {
            if (Color.TryParse(_color.Text, out var color)) _canvas.BrushColor = color;
            _canvas.BrushSize = Value(_brush);
        }

        private void SyncLayerProperties()
        {
            if (_syncingProperties || _canvas.Tool != AnimationEditorTool.Select) return;
            _syncingProperties = true;
            try
            {
                if (_canvas.SelectedText is { } text)
                {
                    if (_propertyMode != "AnimationText") ShowProperties("AnimationText", false);
                    _text.Text = text;
                    _font.Value = (decimal)_canvas.SelectedTextSize;
                    _fontFamily.Text = _canvas.SelectedTextFamily;
                    _color.Text = _canvas.SelectedTextColor.ToString();
                    _bold.IsChecked = _canvas.SelectedTextBold;
                    _italic.IsChecked = _canvas.SelectedTextItalic;
                }
                else if (_canvas.SelectedImageSize is { } size)
                {
                    if (_propertyMode != "AnimationImageProperties") ShowProperties("AnimationImageProperties", false);
                    _imageWidth.Value = (decimal)size.Width;
                    _imageHeight.Value = (decimal)size.Height;
                }

                if (_canvas.SelectedBounds is { } bounds)
                {
                    _layerX.Value = (decimal)bounds.X;
                    _layerY.Value = (decimal)bounds.Y;
                    _opacity.Value = (decimal)(_canvas.SelectedOpacity * 100);
                }

                UpdateDraftActions();
            }
            finally
            {
                _syncingProperties = false;
            }
        }

        private static void Hint(StackPanel panel, string key) => panel.Children.Add(new TextBlock
        {
            [!TextBlock.TextProperty] = LiveText.Resource(key), TextWrapping = TextWrapping.Wrap,
            Classes = { "editor-caption" },
            Foreground = AppTheme.Muted,
            Margin = new(0, 8, 0, 0)
        });

        private static StackPanel Page() => new()
            { Margin = new(10, 8, 10, 8), Spacing = 4, VerticalAlignment = VerticalAlignment.Top };

        private static ScrollViewer Scroll(Control content) => new()
        {
            Content = content, VerticalContentAlignment = VerticalAlignment.Top,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled
        };

        private PixelRect? EffectRegion() => _canvas.Selection is { } rect
            ? new((int)rect.X, (int)rect.Y, Math.Max(1, (int)rect.Width), Math.Max(1, (int)rect.Height))
            : null;

        private Task ApplyEffectAsync(PixelEffect effect)
        {
            var frames = SelectedFrames();
            if (!HasFrameSelection(frames)) return Task.CompletedTask;
            return EditAsync((d, token) => d.ApplyEffects(frames, [effect], token));
        }

        private Task CropAsync()
        {
            if (EffectRegion() is not { } region)
            {
                SetStatus("AnimationSelectRegion");
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
                SetStatus("AnimationApplyFirst");
                return;
            }

            var frames = SelectedFrames();
            if (!HasFrameSelection(frames)) return;
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
