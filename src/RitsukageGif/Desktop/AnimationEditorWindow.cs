using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
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
        private readonly CheckBox _allFrames = new() { Content = UiText.Get("AnimationAllFrames") };
        private readonly Button _cancel = new() { Content = UiText.Get("CancelProcessing"), IsVisible = false };
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
            Title = UiText.Get("AnimationEditorTitle");
            Width = 1100;
            Height = 760;
            MinWidth = 760;
            MinHeight = 520;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            var toolbar = new WrapPanel { Margin = new(8, 6), Orientation = Orientation.Horizontal };
            toolbar.Children.Add(ActionButton("EditorNew", NewAsync));
            toolbar.Children.Add(ActionButton("EditorOpen", OpenAsync));
            toolbar.Children.Add(ActionButton("AnimationImport", ImportAsync));
            toolbar.Children.Add(ActionButton("AnimationExport", ExportAsync));
            toolbar.Children.Add(ActionButton("AnimationUndo", UndoAsync));
            toolbar.Children.Add(ActionButton("AnimationRedo", RedoAsync));
            toolbar.Children.Add(ActionButton("AnimationPlay", () =>
            {
                TogglePlayback();
                return Task.CompletedTask;
            }));
            _editing.Children.Add(toolbar);
            var body = new Grid { ColumnDefinitions = new("*,280"), Margin = new(8, 0) };
            body.Children.Add(new Border
            {
                Background = new SolidColorBrush(Color.Parse("#E7E9ED")), BorderBrush = Brushes.LightGray,
                BorderThickness = new(1), ClipToBounds = true, Child = _canvas
            });
            var properties = BuildProperties();
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
                    panel.Children.Add(new AnimationEditorThumbnail(frame.Path));
                    panel.Children.Add(new TextBlock
                    {
                        Text = $"{index + 1} · {frame.DelayMs} ms", FontSize = 11, TextAlignment = TextAlignment.Center
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
            _frames.Margin = new(8, 6);
            Grid.SetRow(_frames, 2);
            _editing.Children.Add(_frames);
            var frameTools = new WrapPanel { Margin = new(8, 0, 8, 6), Orientation = Orientation.Horizontal };
            frameTools.Children.Add(ActionButton("AnimationAddFrame", () =>
            {
                var index = CurrentFrame + 1;
                var delay = Value(_delay);
                return MutateAsync(d => d.AddBlank(index, delay));
            }));
            frameTools.Children.Add(ActionButton("AnimationDuplicate", () =>
            {
                var frames = SelectedFrames();
                return MutateAsync(d => d.Duplicate(frames));
            }));
            frameTools.Children.Add(ActionButton("AnimationDelete", () =>
            {
                var frames = SelectedFrames();
                return MutateAsync(d => d.Delete(frames));
            }));
            frameTools.Children.Add(ActionButton("AnimationPrevious", () => MoveFrameAsync(-1)));
            frameTools.Children.Add(ActionButton("AnimationNext", () => MoveFrameAsync(1)));
            frameTools.Children.Add(_allFrames);
            Grid.SetRow(frameTools, 3);
            _editing.Children.Add(frameTools);
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
                _playback.Stop();
                _canvas.Dispose();
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
            KeyDown += async (_, e) =>
            {
                if (_operation is not null || e.Source is TextBox) return;
                if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.Z)
                {
                    e.Handled = true;
                    await UndoAsync();
                }
                else if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.Y)
                {
                    e.Handled = true;
                    await RedoAsync();
                }
                else if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.S)
                {
                    e.Handled = true;
                    await ExportAsync();
                }
            };
        }

        private int CurrentFrame => Math.Max(0, _frames.SelectedIndex);

        private int[] SelectedFrames() => _allFrames.IsChecked == true && _document is not null
            ? Enumerable.Range(0, _document.Frames.Count).ToArray()
            : _frames.SelectedItems?.Cast<int>().Order().ToArray() is { Length: > 0 } selected
                ? selected
                : [CurrentFrame];

        private static CompactNumberInput Number(int value, int minimum, int maximum) => new()
            { Minimum = minimum, Maximum = maximum, Value = value, Width = 86, Height = 26 };

        private static int Value(CompactNumberInput input) => (int)(input.Value ?? 0);

        private static Button ActionButton(string key, Func<Task> action)
        {
            var button = new Button { Content = UiText.Get(key), Margin = new(0, 0, 5, 4), Padding = new(8, 3) };
            button.Click += async (_, _) => await action();
            return button;
        }

        private static Control Row(string key, Control control)
        {
            var row = new Grid { ColumnDefinitions = new("*,Auto"), Margin = new(0, 0, 0, 7) };
            row.Children.Add(new TextBlock
            {
                Text = UiText.Get(key), VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap,
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
            _refreshing = true;
            _frames.ItemsSource = Enumerable.Range(0, _document.Frames.Count).ToArray();
            _frames.SelectedIndex = selected;
            _refreshing = false;
            _width.Value = _document.Width;
            _height.Value = _document.Height;
            _loops.Value = _document.Iterations;
            Title =
                $"{UiText.Get("AnimationEditorTitle")} — {(_path is null ? UiText.Get("EditorNew") : Path.GetFileName(_path))}{(_dirty ? " *" : "")}";
            ShowFrame();
        }

        private void ShowFrame() => _frameLoad = ShowFrameAsync(++_frameLoadVersion);

        private async Task ShowFrameAsync(int version)
        {
            if (_document is null || _document.Frames.Count == 0) return;
            if (_canvas.HasChanges && CurrentFrame != _displayedFrame)
            {
                _refreshing = true;
                _frames.SelectedIndex = _displayedFrame;
                _refreshing = false;
                _status.Text = UiText.Get("AnimationApplyFirst");
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
                if (version == _frameLoadVersion) _status.Text = exception.Message;
                return;
            }
            finally
            {
                if (version == _frameLoadVersion) _canvas.IsEnabled = true;
            }

            if (version != _frameLoadVersion || !ReferenceEquals(document, _document) || index != CurrentFrame) return;
            _displayedPath = frame.Path;
            _delay.Value = frame.DelayMs;
            _status.Text = UiText.Format("AnimationFrameStatus", CurrentFrame + 1, _document.Frames.Count,
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
                _status.Text = UiText.Get("AnimationApplyFirst");
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
