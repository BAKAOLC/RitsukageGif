using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Threading;

namespace RitsukageGif.Desktop
{
    internal sealed partial class AnimationEditorWindow : Window
    {
        private readonly AnimationEditorCanvas _canvas = new();
        private readonly ListBox _frames = new() { SelectionMode = SelectionMode.Multiple, Height = 94 };
        private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
        private readonly Grid _editing = new() { RowDefinitions = new("Auto,*,Auto,Auto") };
        private readonly DispatcherTimer _playback = new();
        private readonly CompactNumberInput _delay = Number(100, 1, 3600000);
        private readonly CompactNumberInput _width = Number(640, 1, 16384);
        private readonly CompactNumberInput _height = Number(480, 1, 16384);
        private readonly CompactNumberInput _loops = Number(0, 0, 65535);

        private readonly Button _cancel = new()
            { [!ContentControl.ContentProperty] = LiveText.Resource("CancelProcessing"), IsVisible = false };

        private AnimationEditDocument? _document;
        private CancellationTokenSource? _operation;
        private string? _path;
        private string? _displayedPath;
        private Task _frameLoad = Task.CompletedTask;
        private int _frameLoadVersion;
        private int _displayedFrame;
        private readonly Stopwatch _playClock = new();
        private double _nextFrameAt;
        private bool _dirty, _allowClose, _confirming, _refreshing;

        public AnimationEditorWindow(string? initialPath = null)
        {
            ConfigureTypography();
            Foreground = AppTheme.Ink;
            Title = UiText.Get("AnimationEditorTitle");
            Width = 1280;
            Height = 800;
            MinWidth = 1280;
            MinHeight = 720;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = AppTheme.Workspace;
            _editing.Children.Add(BuildRibbon());
            var body = _body;
            body.Children.Add(new Border
            {
                Background = new SolidColorBrush(Color.Parse("#20232A")), BorderBrush = AppTheme.Outline,
                BorderThickness = new(1), CornerRadius = new(6), ClipToBounds = true, Child = _canvas
            });
            var properties = BuildProperties();
            _sidebar = properties;
            Grid.SetColumn(properties, 1);
            body.Children.Add(properties);
            Grid.SetRow(body, 1);
            _editing.Children.Add(body);
            _frames.ItemTemplate = new FuncDataTemplate<int>((index, _) =>
            {
                var panel = new StackPanel { Width = 82, Margin = new(3), Spacing = 3 };
                if (_document is { } document && index < document.Frames.Count)
                {
                    var frame = document.Frames[index];
                    panel.Children.Add(new AnimationEditorThumbnail(frame.Path, _thumbnailCache));
                    panel.Children.Add(new TextBlock
                    {
                        Text = $"{index + 1} · {frame.DelayMs} ms", Classes = { "editor-caption" },
                        TextAlignment = TextAlignment.Center
                    });
                }

                return panel;
            });
            _frames.ItemsPanel = new FuncTemplate<Panel?>(() => new VirtualizingStackPanel
                { Orientation = Orientation.Horizontal });
            ScrollViewer.SetHorizontalScrollBarVisibility(_frames, ScrollBarVisibility.Auto);
            ScrollViewer.SetVerticalScrollBarVisibility(_frames, ScrollBarVisibility.Disabled);
            _frames.SelectionChanged += (_, _) =>
            {
                if (!_refreshing) ShowFrame();
            };
            _frames.Classes.Add("animation-timeline");
            _frames.Margin = new(8, 6);
            _frames.Background = AppTheme.Surface;
            _frames.BorderBrush = AppTheme.Outline;
            _frames.BorderThickness = new(1);
            _frames.CornerRadius = new(6);
            Grid.SetRow(_frames, 2);
            _editing.Children.Add(_frames);
            var frameTools = BuildTransport();
            Grid.SetRow(frameTools, 3);
            _editing.Children.Add(frameTools);
            _frames.ContextMenu = BuildFrameMenu();
            var root = new Grid { RowDefinitions = new("*,Auto") };
            root.Children.Add(_editing);
            var bottom = new DockPanel { Margin = new(10, 2, 10, 8) };
            DockPanel.SetDock(_cancel, Dock.Right);
            bottom.Children.Add(_cancel);
            bottom.Children.Add(_status);
            Grid.SetRow(bottom, 1);
            root.Children.Add(bottom);
            Content = root;
            _cancel.Click += (_, _) => _operation?.Cancel();
            _playback.Tick += (_, _) => AdvancePlayback();
            Closing += OnClosing;
            Closed += (_, _) =>
            {
                _languageClosed = true;
                UiText.LanguageChanged -= RefreshLanguage;
                _playback.Stop();
                _canvas.Dispose();
                _thumbnailCache.Dispose();
                _document?.Dispose();
            };
            Opened += async (_, _) =>
            {
                if (initialPath is not null) await LoadAsync(initialPath);
                else
                    await RunAsync(async token =>
                    {
                        _document = await Task.Run(() => AnimationEditDocument.Create(640, 480), token);
                        Refresh();
                    });
            };
            UiText.LanguageChanged += RefreshLanguage;
            AddHandler(Avalonia.Input.InputElement.KeyDownEvent, HandleEditorKeyDown,
                Avalonia.Interactivity.RoutingStrategies.Tunnel);
            _frames.PointerPressed += (_, _) => _playback.Stop();
            _canvas.ViewChanged += (_, _) => UpdateZoomLabel();
            _canvas.DraftChanged += (_, _) => UpdateDraftActions();
            _canvas.SelectionChanged += (_, _) => SyncLayerProperties();
        }

        private int CurrentFrame => _frames.SelectedIndex >= 0 ? _frames.SelectedIndex : _displayedFrame;

        private int[] SelectedFrames() => _frames.SelectedItems?.Cast<int>().Order().ToArray() ?? [];

        private bool HasFrameSelection(int[] frames)
        {
            if (frames.Length > 0) return true;
            SetStatus("AnimationSelectFrames");
            return false;
        }

        private static CompactNumberInput Number(int value, int minimum, int maximum)
        {
            var input = new CompactNumberInput { Minimum = minimum, Maximum = maximum, Value = value, Width = 86 };
            input[!FontSizeProperty] = new DynamicResourceExtension(EditorFontResource);
            input[!HeightProperty] = new DynamicResourceExtension(ButtonHeightResource);
            return input;
        }

        private static int Value(CompactNumberInput input) => (int)(input.Value ?? 0);

        private static Button ActionButton(string key, Func<Task> action)
        {
            var button = new Button
            {
                Content = new TextBlock { [!TextBlock.TextProperty] = LiveText.Resource(key) }, Tag = key,
                Margin = new(0, 0, 4, 3), Padding = new(8, 4),
                HorizontalContentAlignment = HorizontalAlignment.Center
            };
            button.Click += async (_, _) => await action();
            return button;
        }

        private static Control Row(string key, Control control)
        {
            Detach(control);
            control.Margin = default;
            if (control is CompactNumberInput) control.Width = 86;
            else if (control is TextBox) control.Width = 110;
            var row = new Grid { ColumnDefinitions = new("*,Auto"), Margin = new(0, 0, 0, 7) };
            row.Children.Add(new TextBlock
            {
                [!TextBlock.TextProperty] = LiveText.Resource(key), VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                Margin = new(0, 0, 8, 0)
            });
            Grid.SetColumn(control, 1);
            row.Children.Add(control);
            return row;
        }

        private void Refresh()
        {
            if (_document is null) return;
            var selected = Math.Clamp(CurrentFrame, 0, Math.Max(0, _document.Frames.Count - 1));
            var selection = _frames.SelectedItems?.Cast<int>().Where(i => i >= 0 && i < _document.Frames.Count)
                .ToArray() ?? [];
            _refreshing = true;
            _frames.ItemsSource = Enumerable.Range(0, _document.Frames.Count).ToArray();
            _frames.SelectedIndex = selected;
            foreach (var index in selection)
                if (_frames.SelectedItems?.Contains(index) == false)
                    _frames.SelectedItems.Add(index);
            _refreshing = false;
            _width.Value = _document.Width;
            _height.Value = _document.Height;
            _loops.Value = _document.Iterations;
            UpdateWindowTitle();
            RequestThumbnailPrefetch();
            ShowFrame();
        }

        private void ShowFrame()
        {
            if (_frames.SelectedIndex < 0)
            {
                UpdateFrameSummary();
                return;
            }

            _frameLoad = ShowFrameAsync(++_frameLoadVersion);
        }

        private async Task ShowFrameAsync(int version)
        {
            if (_document is null || _document.Frames.Count == 0) return;
            if (_canvas.HasChanges && CurrentFrame != _displayedFrame)
            {
                _refreshing = true;
                _frames.SelectedIndex = _displayedFrame;
                _refreshing = false;
                SetStatus("AnimationApplyFirst");
                return;
            }

            var document = _document;
            var index = CurrentFrame;
            var frame = document.Frames[index];
            _displayedFrame = index;
            _canvas.IsEnabled = false;
            try
            {
                if (!_canvas.HasChanges && !string.Equals(_displayedPath, frame.Path, StringComparison.Ordinal))
                    await _canvas.LoadFrameAsync(frame.Path);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception exception)
            {
                if (version == _frameLoadVersion) SetStatusError(exception.Message);
                return;
            }
            finally
            {
                if (version == _frameLoadVersion) _canvas.IsEnabled = true;
            }

            if (version != _frameLoadVersion || !ReferenceEquals(document, _document) || index != CurrentFrame) return;
            _displayedPath = frame.Path;
            _delay.Value = frame.DelayMs;
            RequestThumbnailPrefetch();
            UpdateFrameSummary();
            SetStatus("AnimationFrameStatus", CurrentFrame + 1, _document.Frames.Count,
                _document.Width, _document.Height);
        }

        private void TogglePlayback()
        {
            if (_playback.IsEnabled)
            {
                _playback.Stop();
                return;
            }

            if (_canvas.HasChanges)
            {
                SetStatus("AnimationApplyFirst");
                return;
            }

            if (_document is null || _document.Frames.Count < 2) return;
            _playClock.Restart();
            _nextFrameAt = _document.Frames[CurrentFrame].DelayMs;
            _playback.Interval = TimeSpan.FromMilliseconds(10);
            _playback.Start();
        }

        private void AdvancePlayback()
        {
            if (_document is null) return;
            var index = CurrentFrame;
            while (_playClock.Elapsed.TotalMilliseconds >= _nextFrameAt)
            {
                index = (index + 1) % _document.Frames.Count;
                _nextFrameAt += _document.Frames[index].DelayMs;
            }

            if (index != CurrentFrame) _frames.SelectedIndex = index;
        }
    }
}
