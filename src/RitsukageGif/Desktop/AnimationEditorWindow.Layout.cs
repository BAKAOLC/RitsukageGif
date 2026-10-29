using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.VisualTree;
using Path = Avalonia.Controls.Shapes.Path;

namespace RitsukageGif.Desktop
{
    internal sealed partial class AnimationEditorWindow
    {
        private readonly Grid _body = new() { ColumnDefinitions = new("*,256"), Margin = new(8, 0) };

        private readonly TextBlock _frameSummary = new()
            { VerticalAlignment = VerticalAlignment.Center };

        private readonly Control? _sidebar;

        private readonly Dictionary<string, Button> _toolButtons = [];

        private readonly TextBlock _zoomLabel = new()
            { Text = "100%", VerticalAlignment = VerticalAlignment.Center, Margin = new(8, 0) };

        private string _propertyMode = "AnimationFrames";

        private Border BuildRibbon()
        {
            var ribbon = new TabControl
                { Name = "AnimationRibbon", Margin = new(8, 6, 8, 6), Background = AppTheme.Surface };
            var file = ToolStrip();
            file.Children.Add(ActionButton("EditorNew", NewAsync));
            file.Children.Add(ActionButton("EditorOpen", OpenAsync));
            if (_imageMode)
            {
                file.Children.Add(ActionButton("ImageSave", () => SaveImageAsync(false)));
                file.Children.Add(ActionButton("ImageSaveAs", () => SaveImageAsync(true)));
                file.Children.Add(ActionButton("ImageCopy", CopyImageAsync));
            }

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
                         "AnimationBrush", "AnimationEraser", "AnimationLine", "AnimationArrow", "AnimationRectangle",
                         "AnimationEllipse",
                         "AnimationSelection",
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
            var tabs = new List<TabItem>
            {
                RibbonTab("AnimationFile", file), RibbonTab("AnimationFrames", frames),
                RibbonTab("AnimationInsert", insert), RibbonTab("AnimationDrawing", draw),
                RibbonTab("AnimationImage", image), RibbonTab("AnimationView", view),
            };
            if (_imageMode) tabs.RemoveAt(1);
            ribbon.ItemsSource = tabs;
            return new()
            {
                Child = ribbon, Background = AppTheme.Surface, BorderBrush = AppTheme.Outline,
                BorderThickness = new(0, 0, 0, 1),
            };
        }

        private static WrapPanel ToolStrip()
        {
            return new() { Orientation = Orientation.Horizontal, Margin = new(0, 5, 0, 0) };
        }

        private static TabItem RibbonTab(string key, WrapPanel strip)
        {
            var buttons = strip.Children.OfType<Button>().ToArray();
            strip.Children.Clear();
            var groups = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new(0, 4, 0, 2) };
            var split = key switch
            {
                "AnimationFile" => 3, "AnimationFrames" => 4, "AnimationImage" => 2, "AnimationView" => 4,
                _ => buttons.Length,
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
                    content.Children.Add(new Path
                    {
                        Data = Geometry.Parse(RibbonIcon(tool)), Stretch = Stretch.Uniform,
                        [!WidthProperty] = new DynamicResourceExtension(IconSizeResource),
                        [!HeightProperty] = new DynamicResourceExtension(IconSizeResource),
                        [!Shape.StrokeProperty] =
                            button[!ForegroundProperty],
                        StrokeThickness = 1.5,
                        HorizontalAlignment = HorizontalAlignment.Center,
                    });
                    content.Children.Add(new TextBlock
                        { [!TextBlock.TextProperty] = LiveText.Resource(tool), TextAlignment = TextAlignment.Center });
                    button.Content = content;
                    button.Padding = new(8, 5);
                    var tip = new StackPanel { Spacing = 2 };
                    tip.Children.Add(new TextBlock
                    {
                        [!TextBlock.TextProperty] = LiveText.Resource(tool),
                        HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center,
                    });
                    if (Shortcut(tool) is { } shortcut)
                        tip.Children.Add(new TextBlock
                        {
                            Text = shortcut, Classes = { "editor-caption" },
                            HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center,
                        });
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
                    _ => key,
                };
                group.Children.Add(new TextBlock
                {
                    [!TextBlock.TextProperty] = LiveText.Resource(caption), Classes = { "editor-caption" },
                    Foreground = AppTheme.Muted,
                    TextAlignment = TextAlignment.Center,
                });
                groups.Children.Add(new Border
                {
                    Child = group, Padding = new(0, 0, 6, 0), BorderBrush = AppTheme.Outline,
                    BorderThickness = new(0, 0, 1, 0),
                });
            }

            return new()
            {
                Header = Heading(key), Padding = new(12, 4),
                Content = new ScrollViewer
                {
                    Content = groups,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                },
            };
        }

        private static string? Shortcut(string key)
        {
            return key switch
            {
                "EditorOpen" => "Ctrl+O", "AnimationExport" => "Ctrl+S", "AnimationUndo" => "Ctrl+Z",
                "AnimationRedo" => "Ctrl+Y", "AnimationDuplicate" => "Ctrl+D", "AnimationDelete" => "Delete", _ => null,
            };
        }

        private static string RibbonIcon(string key)
        {
            return ToolbarIcon.Geometry(key);
        }

        private void SetPropertiesVisible(bool visible)
        {
            if (_sidebar is null) return;
            _sidebar.IsVisible = visible;
            _body.ColumnDefinitions[1].Width = new(visible ? 256 : 0);
        }

        private void UpdateZoomLabel()
        {
            _zoomLabel.Text = $"{_canvas.Zoom * 100:0}%";
        }

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

        private WrapPanel BuildTransport()
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
                    _ => RibbonIcon("AnimationSelection"),
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
            content.Children.Add(ToolbarIcon.Create(button, geometry));
            if (captionKey is not null)
                content.Children.Add(new TextBlock
                {
                    [!TextBlock.TextProperty] = LiveText.Resource(captionKey),
                    VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap,
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
                         }),
                     })
            {
                var item = new MenuItem
                {
                    [!HeaderedSelectingItemsControl.HeaderProperty] =
                        LiveText.Resource(key),
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

        private void HandleEditorKeyDown(object? sender, KeyEventArgs e)
        {
            _ = HandleEditorKeyDownAsync(e);
        }

        private async Task HandleEditorKeyDownAsync(KeyEventArgs e)
        {
            if (_operation is not null || e.Handled) return;
            if (e.Source is Visual visual &&
                (visual is TextBox || visual.GetVisualAncestors().Any(x => x is TextBox))) return;
            var control = e.KeyModifiers.HasFlag(KeyModifiers.Control);
            switch (e.Source)
            {
                case Button when !control:
                case Visual source when source is ComboBox or NumericUpDown or CompactNumberInput or Slider || source
                    .GetVisualAncestors().Any(x =>
                        x is ComboBox or NumericUpDown or CompactNumberInput or Slider or ToggleButton):
                    return;
            }

            if (e.KeyModifiers.HasFlag(KeyModifiers.Shift) && e.Key is Key.Left or Key.Right) return;
            Func<Task>? action = control
                ? e.Key switch
                {
                    Key.O => OpenAsync,
                    Key.S => _imageMode
                        ? () => SaveImageAsync(e.KeyModifiers.HasFlag(KeyModifiers.Shift))
                        : ExportAsync,
                    Key.C when _imageMode => CopyImageAsync, Key.Z => UndoAsync, Key.Y => RedoAsync,
                    Key.D when !_imageMode => DuplicateFramesAsync, Key.A when !_imageMode => SelectAllFramesAsync,
                    _ => null,
                }
                : e.Key switch
                {
                    Key.Space when !_imageMode => () =>
                    {
                        TogglePlayback();
                        return Task.CompletedTask;
                    },
                    Key.Left when !_imageMode => () => NavigateFrameAsync(CurrentFrame - 1),
                    Key.Right when !_imageMode => () => NavigateFrameAsync(CurrentFrame + 1),
                    Key.Home when !_imageMode => () => NavigateFrameAsync(0),
                    Key.End when !_imageMode => () => NavigateFrameAsync((_document?.Frames.Count ?? 1) - 1),
                    Key.Delete => () =>
                    {
                        var canvasFocused = e.Source is Visual sourceVisual &&
                                            (ReferenceEquals(sourceVisual, _canvas) ||
                                             sourceVisual.GetVisualAncestors().Contains(_canvas));
                        if (canvasFocused && _canvas.HasSelectedLayer)
                        {
                            _canvas.DeleteSelected();
                            return Task.CompletedTask;
                        }

                        return _imageMode ? Task.CompletedTask : DeleteFramesAsync();
                    },
                    _ => null,
                };
            if (action is null) return;
            e.Handled = true;
            try
            {
                await action();
            }
            catch (Exception error)
            {
                _status.Text = error.Message;
            }
        }
    }
}
