using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.VisualTree;

namespace RitsukageGif.Desktop
{
    internal sealed partial class AnimationEditorWindow
    {
        private readonly Grid _body = new() { ColumnDefinitions = new("*,256"), Margin = new(8, 0) };
        private Control? _sidebar;

        private readonly TextBlock _zoomLabel = new()
            { Text = "100%", VerticalAlignment = VerticalAlignment.Center, Margin = new(8, 0) };

        private readonly TextBlock _frameSummary = new()
            { VerticalAlignment = VerticalAlignment.Center };

        private readonly Dictionary<string, Button> _toolButtons = [];
        private string _propertyMode = "AnimationFrames";

        private Control BuildRibbon()
        {
            var ribbon = new TabControl
                { Name = "AnimationRibbon", Margin = new(8, 6, 8, 6), Background = AppTheme.Surface };
            var file = ToolStrip();
            file.Children.Add(ActionButton("EditorNew", NewAsync));
            file.Children.Add(ActionButton("EditorOpen", OpenAsync));
            file.Children.Add(PropertyButton("AnimationExport", "AnimationExport"));
            file.Children.Add(ActionButton("AnimationUndo", UndoAsync));
            file.Children.Add(ActionButton("AnimationRedo", RedoAsync));
            var frames = ToolStrip();
            frames.Children.Add(ActionButton("AnimationAddFrame", AddBlankAsync));
            frames.Children.Add(ActionButton("AnimationImport", ImportAsync));
            frames.Children.Add(ActionButton("AnimationDuplicate", DuplicateFramesAsync));
            frames.Children.Add(ActionButton("AnimationDelete", DeleteFramesAsync));
            frames.Children.Add(ActionButton("AnimationPrevious", () => MoveFrameAsync(-1)));
            frames.Children.Add(ActionButton("AnimationNext", () => MoveFrameAsync(1)));
            frames.Children.Add(PropertyButton("AnimationTiming", "AnimationFrames"));
            var insert = ToolStrip();
            var imageButton = ActionButton("AnimationAddImage", async () =>
            {
                ShowProperties("AnimationImageProperties");
                await AddImageAsync();
            });
            _toolButtons["AnimationImageProperties"] = imageButton;
            insert.Children.Add(imageButton);
            insert.Children.Add(PropertyButton("AnimationAddText", "AnimationText"));
            insert.Children.Add(PropertyButton("AnimationSelect", "AnimationSelect"));
            var draw = ToolStrip();
            foreach (var key in new[]
                     {
                         "AnimationBrush", "AnimationEraser", "AnimationLine", "AnimationRectangle", "AnimationEllipse",
                         "AnimationSelection"
                     })
                draw.Children.Add(PropertyButton(key, key));
            var image = ToolStrip();
            image.Children.Add(PropertyButton("AnimationResize", "AnimationResize"));
            image.Children.Add(PropertyButton("AnimationCrop", "AnimationCrop"));
            image.Children.Add(ActionButton("AnimationRotate",
                () => EditAsync((d, t) => d.Transform(new() { Rotation = 90 }, t))));
            image.Children.Add(ActionButton("AnimationFlipHorizontal",
                () => EditAsync((d, t) => d.Transform(new() { FlipHorizontal = true }, t))));
            image.Children.Add(ActionButton("AnimationFlipVertical",
                () => EditAsync((d, t) => d.Transform(new() { FlipVertical = true }, t))));
            image.Children.Add(PropertyButton("AnimationEffects", "AnimationEffects"));
            var view = ToolStrip();
            view.Children.Add(ActionButton("AnimationFit", () =>
            {
                _canvas.Fit();
                return Task.CompletedTask;
            }));
            view.Children.Add(ActionButton("AnimationActualSize", () =>
            {
                _canvas.Zoom = 1;
                return Task.CompletedTask;
            }));
            view.Children.Add(ActionButton("AnimationZoomOut", () =>
            {
                _canvas.Zoom /= 1.25;
                return Task.CompletedTask;
            }));
            view.Children.Add(ActionButton("AnimationZoomIn", () =>
            {
                _canvas.Zoom *= 1.25;
                return Task.CompletedTask;
            }));
            view.Children.Add(PropertyButton("AnimationPan", "AnimationPan"));
            view.Children.Add(ActionButton("AnimationPropertiesToggle", () =>
            {
                SetPropertiesVisible(_sidebar?.IsVisible != true);
                return Task.CompletedTask;
            }));
            ribbon.ItemsSource = new[]
            {
                RibbonTab("AnimationFile", file), RibbonTab("AnimationFrames", frames),
                RibbonTab("AnimationInsert", insert), RibbonTab("AnimationDrawing", draw),
                RibbonTab("AnimationImage", image), RibbonTab("AnimationView", view)
            };
            return new Border
            {
                Child = ribbon, Background = AppTheme.Surface, BorderBrush = AppTheme.Outline,
                BorderThickness = new(0, 0, 0, 1)
            };
        }

        private static WrapPanel ToolStrip() =>
            new() { Orientation = Orientation.Horizontal, Margin = new(0, 5, 0, 0) };

        private static TabItem RibbonTab(string key, WrapPanel strip)
        {
            var buttons = strip.Children.OfType<Button>().ToArray();
            strip.Children.Clear();
            var groups = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new(0, 4, 0, 2) };
            var split = key switch
            {
                "AnimationFile" => 3, "AnimationFrames" => 4, "AnimationImage" => 2, "AnimationView" => 4,
                _ => buttons.Length
            };
            for (var start = 0; start < buttons.Length; start += split)
            {
                var group = new StackPanel { Spacing = 2 };
                var tools = new StackPanel { Orientation = Orientation.Horizontal };
                for (var i = start; i < Math.Min(buttons.Length, start + split); i++)
                {
                    var button = buttons[i];
                    button.Classes.Add("tool");
                    button.Classes.Add("Secondary");
                    var tool = (string)button.Tag!;
                    var content = new StackPanel { Spacing = 4, HorizontalAlignment = HorizontalAlignment.Center };
                    content.Children.Add(new Avalonia.Controls.Shapes.Path
                    {
                        Data = Geometry.Parse(RibbonIcon(tool)), Stretch = Stretch.Uniform,
                        [!WidthProperty] = new DynamicResourceExtension(IconSizeResource),
                        [!HeightProperty] = new DynamicResourceExtension(IconSizeResource),
                        [!Avalonia.Controls.Shapes.Shape.StrokeProperty] =
                            button[!Avalonia.Controls.Primitives.TemplatedControl.ForegroundProperty],
                        StrokeThickness = 1.5,
                        HorizontalAlignment = HorizontalAlignment.Center
                    });
                    content.Children.Add(new TextBlock
                        { [!TextBlock.TextProperty] = LiveText.Resource(tool), TextAlignment = TextAlignment.Center });
                    button.Content = content;
                    button.Padding = new(8, 5);
                    var tip = new StackPanel { Spacing = 2 };
                    tip.Children.Add(new TextBlock { [!TextBlock.TextProperty] = LiveText.Resource(tool) });
                    if (Shortcut(tool) is { } shortcut)
                        tip.Children.Add(new TextBlock { Text = shortcut, Classes = { "editor-caption" } });
                    ToolTip.SetTip(button, tip);
                    tools.Children.Add(button);
                }

                group.Children.Add(tools);
                var caption = key switch
                {
                    "AnimationFile" when start > 0 => "AnimationEditHistory",
                    "AnimationFrames" when start > 0 => "AnimationFrameOrder",
                    "AnimationImage" when start == 0 => "AnimationCanvasGroup",
                    "AnimationView" when start == 0 => "AnimationZoomGroup",
                    _ => key
                };
                group.Children.Add(new TextBlock
                {
                    [!TextBlock.TextProperty] = LiveText.Resource(caption), Classes = { "editor-caption" },
                    Foreground = AppTheme.Muted,
                    TextAlignment = TextAlignment.Center
                });
                groups.Children.Add(new Border
                {
                    Child = group, Padding = new(0, 0, 6, 0), BorderBrush = AppTheme.Outline,
                    BorderThickness = new(0, 0, 1, 0)
                });
            }

            return new TabItem
            {
                Header = Heading(key), Padding = new(12, 4),
                Content = new ScrollViewer
                {
                    Content = groups,
                    HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                    VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled
                }
            };
        }

        private static string? Shortcut(string key) => key switch
        {
            "EditorOpen" => "Ctrl+O", "AnimationExport" => "Ctrl+S", "AnimationUndo" => "Ctrl+Z",
            "AnimationRedo" => "Ctrl+Y", "AnimationDuplicate" => "Ctrl+D", "AnimationDelete" => "Delete", _ => null
        };

        private static string RibbonIcon(string key) => key switch
        {
            "EditorNew" => "M3,2 L12,2 19,9 19,22 3,22 Z M12,2 L12,9 19,9",
            "EditorOpen" => "M2,5 L8,5 10,8 22,8 18,21 2,21 Z",
            "AnimationExport" => "M3,3 L18,3 21,6 21,21 3,21 Z M7,3 L7,9 17,9 17,3 M7,21 L7,14 17,14 17,21",
            "AnimationUndo" => "M8,3 L2,9 8,15 M2,9 L14,9 Q23,9 20,21",
            "AnimationRedo" => "M16,3 L22,9 16,15 M22,9 L10,9 Q1,9 4,21",
            "AnimationAddImage" => "M2,3 L22,3 22,21 2,21 Z M3,18 L8,11 13,16 18,9 22,15 M6,7 L8,7 8,9 6,9 Z",
            "AnimationAddText" => "M3,4 L21,4 M12,4 L12,21 M7,21 L17,21",
            "AnimationSelect" => "M4,2 L4,20 9,15 13,22 17,20 13,13 21,13 Z",
            "AnimationBrush" => "M3,21 L6,14 18,2 22,6 10,18 Z M6,14 L10,18",
            "AnimationEraser" => "M3,15 L14,3 22,11 12,21 8,21 Z M8,10 L17,17",
            "AnimationLine" => "M3,21 L21,3",
            "AnimationRectangle" => "M3,4 L21,4 21,20 3,20 Z",
            "AnimationEllipse" => "M12,3 C24,3 24,21 12,21 C0,21 0,3 12,3 Z",
            "AnimationSelection" =>
                "M3,8 L3,3 8,3 M11,3 L14,3 M17,3 L21,3 21,8 M21,11 L21,14 M21,17 L21,21 17,21 M14,21 L11,21 M8,21 L3,21 3,17 M3,14 L3,11",
            "AnimationFit" => "M3,8 L3,3 8,3 M16,3 L21,3 21,8 M21,16 L21,21 16,21 M8,21 L3,21 3,16",
            "AnimationActualSize" => "M2,6 L5,3 5,21 M10,8 L10,9 M10,15 L10,16 M15,6 L18,3 18,21",
            "AnimationZoomOut" => "M10,2 A8,8 0 1 0 10,18 A8,8 0 1 0 10,2 M16,16 L23,23 M5,10 L15,10",
            "AnimationZoomIn" => "M10,2 A8,8 0 1 0 10,18 A8,8 0 1 0 10,2 M16,16 L23,23 M5,10 L15,10 M10,5 L10,15",
            "AnimationPan" =>
                "M12,1 L8,5 M12,1 L16,5 M12,1 L12,23 M12,23 L8,19 M12,23 L16,19 M1,12 L23,12 M1,12 L5,8 M1,12 L5,16 M23,12 L19,8 M23,12 L19,16",
            "AnimationPropertiesToggle" =>
                "M2,3 L22,3 22,21 2,21 Z M15,3 L15,21 M17,7 L20,7 M17,11 L20,11 M17,15 L20,15",
            "AnimationAddFrame" => "M2,3 L18,3 18,17 2,17 Z M15,12 L15,24 M9,18 L21,18",
            "AnimationImport" => "M3,12 L3,21 21,21 21,12 M12,2 L12,16 M7,11 L12,16 17,11",
            "AnimationDuplicate" => "M2,2 L17,2 17,7 M2,2 L2,17 7,17 M7,7 L22,7 22,22 7,22 Z",
            "AnimationDelete" => "M4,5 L20,5 M8,5 L8,2 16,2 16,5 M6,5 L7,22 17,22 18,5 M10,9 L10,18 M14,9 L14,18",
            "AnimationPrevious" => "M10,3 L2,12 10,21 M2,12 L22,12",
            "AnimationNext" => "M14,3 L22,12 14,21 M2,12 L22,12",
            "AnimationTiming" => "M12,2 A10,10 0 1 0 12,22 A10,10 0 1 0 12,2 M12,5 L12,12 17,15",
            "AnimationResize" =>
                "M3,9 L3,3 9,3 M15,21 L21,21 21,15 M3,3 L10,10 M21,21 L14,14 M3,14 L3,21 10,21 M14,3 L21,3 21,10",
            "AnimationCrop" => "M6,1 L6,18 23,18 M1,6 L18,6 18,23",
            "AnimationRotate" => "M18,3 L23,8 16,9 M22,8 A10,10 0 1 0 20,19 M7,8 L15,8 15,16 7,16 Z",
            "AnimationFlipHorizontal" => "M12,1 L12,23 M2,20 L9,5 9,20 Z M22,20 L15,5 15,20 Z",
            "AnimationFlipVertical" => "M1,12 L23,12 M4,2 L19,9 4,9 Z M4,22 L19,15 4,15 Z",
            "AnimationEffects" => "M3,20 L16,7 20,11 7,24 Z M17,1 L17,5 M22,4 L20,6 M23,10 L21,10 M11,3 L13,5",
            _ => throw new ArgumentOutOfRangeException(nameof(key), key, null)
        };

        private void SetPropertiesVisible(bool visible)
        {
            if (_sidebar is null) return;
            _sidebar.IsVisible = visible;
            _body.ColumnDefinitions[1].Width = new GridLength(visible ? 256 : 0);
        }

        private void UpdateZoomLabel() => _zoomLabel.Text = $"{_canvas.Zoom * 100:0}%";

        private Button PropertyButton(string key, string mode)
        {
            var button = ActionButton(key, () =>
            {
                ShowProperties(mode);
                return Task.CompletedTask;
            });
            _toolButtons[mode] = button;
            return button;
        }

        private Control BuildTransport()
        {
            var panel = new WrapPanel
                { Name = "AnimationTransport", Margin = new(8, 0, 8, 4), Orientation = Orientation.Horizontal };
            panel.Children.Add(ActionButton("AnimationFirstFrame", () => NavigateFrameAsync(0)));
            panel.Children.Add(ActionButton("AnimationPreviousFrame", () => NavigateFrameAsync(CurrentFrame - 1)));
            panel.Children.Add(ActionButton("AnimationPlay", () =>
            {
                TogglePlayback();
                return Task.CompletedTask;
            }));
            panel.Children.Add(ActionButton("AnimationNextFrame", () => NavigateFrameAsync(CurrentFrame + 1)));
            panel.Children.Add(ActionButton("AnimationLastFrame",
                () => NavigateFrameAsync((_document?.Frames.Count ?? 1) - 1)));
            var selectAll = ActionButton("AnimationSelectAllFrames", SelectAllFramesAsync);
            selectAll.Name = "SelectAllFrames";
            panel.Children.Add(selectAll);
            panel.Children.Add(_frameSummary);
            panel.Children.Add(_zoomLabel);
            foreach (var button in panel.Children.OfType<Button>())
            {
                button.Classes.Add("transport");
                var key = (string)button.Tag!;
                var icon = key switch
                {
                    "AnimationFirstFrame" => "M3,3 L3,21 M20,3 L8,12 20,21 Z",
                    "AnimationPreviousFrame" => "M17,3 L5,12 17,21 Z",
                    "AnimationPlay" => "M3,3 L13,12 3,21 Z M17,5 L22,5 22,19 17,19 Z",
                    "AnimationNextFrame" => "M7,3 L19,12 7,21 Z",
                    "AnimationLastFrame" => "M21,3 L21,21 M4,3 L16,12 4,21 Z",
                    _ => RibbonIcon("AnimationSelection")
                };
                WithActionIcon(button, key, icon, key == "AnimationSelectAllFrames" ? key : null);
                var isPlayback = button.Tag as string == "AnimationPlay";
                button.Classes.Set("active-tool", isPlayback);
                button.Classes.Add(isPlayback ? "Primary" : "Secondary");
            }

            return panel;
        }

        private static Button WithActionIcon(Button button, string tooltipKey, string geometry, string? captionKey)
        {
            var content = new StackPanel
                { Orientation = Orientation.Horizontal, Spacing = 5, HorizontalAlignment = HorizontalAlignment.Center };
            content.Children.Add(new Avalonia.Controls.Shapes.Path
            {
                Data = Geometry.Parse(geometry), Width = 16, Height = 16, Stretch = Stretch.Uniform,
                StrokeThickness = 1.6, VerticalAlignment = VerticalAlignment.Center,
                [!Avalonia.Controls.Shapes.Shape.StrokeProperty] =
                    button[!Avalonia.Controls.Primitives.TemplatedControl.ForegroundProperty]
            });
            if (captionKey is not null)
                content.Children.Add(new TextBlock
                {
                    [!TextBlock.TextProperty] = LiveText.Resource(captionKey),
                    VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap
                });
            button.Content = content;
            button.Bind(ToolTip.TipProperty, LiveText.Resource(tooltipKey));
            return button;
        }

        private ContextMenu BuildFrameMenu()
        {
            var menu = new ContextMenu();
            var items = new List<MenuItem>();
            foreach (var (key, action) in new (string, Func<Task>)[]
                     {
                         ("AnimationSelectAllFrames", SelectAllFramesAsync),
                         ("AnimationAddFrame", AddBlankAsync), ("AnimationImport", ImportAsync),
                         ("AnimationDuplicate", DuplicateFramesAsync), ("AnimationDelete", DeleteFramesAsync),
                         ("AnimationPrevious", () => MoveFrameAsync(-1)), ("AnimationNext", () => MoveFrameAsync(1)),
                         ("AnimationTiming", () =>
                         {
                             ShowProperties("AnimationFrames");
                             return Task.CompletedTask;
                         })
                     })
            {
                var item = new MenuItem
                {
                    [!Avalonia.Controls.Primitives.HeaderedSelectingItemsControl.HeaderProperty] =
                        LiveText.Resource(key)
                };
                item.Click += async (_, _) => await action();
                items.Add(item);
            }

            menu.ItemsSource = items;
            return menu;
        }

        private Task SelectAllFramesAsync()
        {
            if (_document is null) return Task.CompletedTask;
            _playback.Stop();
            var current = Math.Clamp(CurrentFrame, 0, _document.Frames.Count - 1);
            var selection = new List<int>(_document.Frames.Count) { current };
            for (var index = 0; index < _document.Frames.Count; index++)
                if (index != current)
                    selection.Add(index);
            _refreshing = true;
            try
            {
                _frames.SelectedItems = selection;
            }
            finally
            {
                _refreshing = false;
            }

            UpdateFrameSummary();
            return Task.CompletedTask;
        }

        private Task AddBlankAsync()
        {
            var index = CurrentFrame + 1;
            var delay = Value(_delay);
            return MutateAsync(d => d.AddBlank(index, delay));
        }

        private Task DuplicateFramesAsync()
        {
            var frames = SelectedFrames();
            if (!HasFrameSelection(frames)) return Task.CompletedTask;
            return MutateAsync(d => d.Duplicate(frames));
        }

        private Task DeleteFramesAsync()
        {
            var frames = SelectedFrames();
            if (!HasFrameSelection(frames)) return Task.CompletedTask;
            return MutateAsync(d => d.Delete(frames));
        }

        private Task NavigateFrameAsync(int index)
        {
            _playback.Stop();
            if (_document is not null) _frames.SelectedIndex = Math.Clamp(index, 0, _document.Frames.Count - 1);
            return _frameLoad;
        }

        private void UpdateFrameSummary()
        {
            if (_document is null) return;
            var frames = SelectedFrames();
            var duration = frames.Where(i => i >= 0 && i < _document.Frames.Count)
                .Sum(i => (long)_document.Frames[i].DelayMs);
            _frameSummary.Text = UiText.Format("AnimationSelectionSummary", CurrentFrame + 1, _document.Frames.Count,
                frames.Length, duration);
        }

        private async void HandleEditorKeyDown(object? sender, KeyEventArgs e)
        {
            if (_operation is not null || e.Handled) return;
            if (e.Source is Avalonia.Visual visual &&
                (visual is TextBox || visual.GetVisualAncestors().Any(x => x is TextBox))) return;
            var control = e.KeyModifiers.HasFlag(KeyModifiers.Control);
            if (e.Source is Button && !control) return;
            if (e.Source is Avalonia.Visual source &&
                (source is ComboBox or NumericUpDown or CompactNumberInput or Slider || source
                    .GetVisualAncestors().Any(x =>
                        x is ComboBox or NumericUpDown or CompactNumberInput or Slider
                            or Avalonia.Controls.Primitives.ToggleButton))) return;
            if (e.KeyModifiers.HasFlag(KeyModifiers.Shift) && e.Key is Key.Left or Key.Right) return;
            Func<Task>? action = control
                ? e.Key switch
                {
                    Key.O => OpenAsync, Key.S => ExportAsync, Key.Z => UndoAsync, Key.Y => RedoAsync,
                    Key.D => DuplicateFramesAsync, Key.A => SelectAllFramesAsync,
                    _ => null
                }
                : e.Key switch
                {
                    Key.Space => () =>
                    {
                        TogglePlayback();
                        return Task.CompletedTask;
                    },
                    Key.Left => () => NavigateFrameAsync(CurrentFrame - 1),
                    Key.Right => () => NavigateFrameAsync(CurrentFrame + 1),
                    Key.Home => () => NavigateFrameAsync(0),
                    Key.End => () => NavigateFrameAsync((_document?.Frames.Count ?? 1) - 1),
                    Key.Delete => () =>
                    {
                        var canvasFocused = e.Source is Avalonia.Visual sourceVisual &&
                                            (ReferenceEquals(sourceVisual, _canvas) ||
                                             sourceVisual.GetVisualAncestors().Contains(_canvas));
                        if (canvasFocused && _canvas.HasSelectedLayer)
                        {
                            _canvas.DeleteSelected();
                            return Task.CompletedTask;
                        }

                        return DeleteFramesAsync();
                    },
                    _ => null
                };
            if (action is null) return;
            e.Handled = true;
            await action();
        }
    }
}
