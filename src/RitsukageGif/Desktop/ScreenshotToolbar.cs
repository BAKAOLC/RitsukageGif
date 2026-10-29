using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace RitsukageGif.Desktop
{
    internal sealed partial class ScreenshotToolbar : Border, IDisposable
    {
        private static readonly IBrush ToolbarSurface = AppTheme.Surface;
        private static readonly IBrush ActiveTool = AppTheme.Accent;

        private static readonly AttachedProperty<IDisposable?> TooltipBindingProperty =
            AvaloniaProperty.RegisterAttached<ScreenshotToolbar, Button, IDisposable?>("TooltipBinding");

        private readonly StackPanel _body = new() { Spacing = 6 };
        private readonly Button? _cancel;
        private readonly WrapPanel _commandRow;
        private readonly Border _contextFrame;
        private readonly Button _copy;
        private readonly Button _delete;
        private readonly Func<IDisposable>? _dialogScopeProvider;
        private readonly TextBlock _error = new() { Foreground = Brushes.OrangeRed, TextWrapping = TextWrapping.Wrap };
        private readonly PixelRect _monitor;
        private readonly Button _more;
        private readonly Flyout _moreCommands = new() { OverlayDismissEventPassThrough = true };
        private readonly Button _moveHandle;
        private readonly Button _outputButton;
        private readonly ComboBox _outputColor;
        private readonly Flyout _outputProperties = new() { OverlayDismissEventPassThrough = true };
        private readonly Button? _pin;
        private readonly AnnotationPropertyPresenter _propertyPresenter;
        private readonly List<Button> _recognitionButtons = [];
        private readonly Button _redo;
        private readonly Button _save;
        private readonly Button? _scroll;
        private readonly ScreenshotAnnotationSession _session;
        private readonly IReadOnlyList<SelectionCommandDefinition> _shortcutBindings;
        private readonly Dictionary<ScreenshotTool, Button> _tools = [];
        private readonly Button _undo;
        private bool _disposed;
        private bool? _lastHdrAvailable;
        private bool _ownsToolbar;
        private PixelRect _placedRegion;
        private double _placedScale;
        private Size _placedSize;
        private PixelPoint _placementMouse;
        private bool _refreshing;
        private string? _scrollTip;

        public ScreenshotToolbar(ScreenshotAnnotationSession session, PixelRect monitor, double scale,
            IReadOnlyList<SelectionCommandDefinition> shortcutBindings, Action<SelectionCommand>? execute = null,
            bool imageEditing = false, Func<IDisposable>? dialogScopeProvider = null)
        {
            _session = session;
            _dialogScopeProvider = dialogScopeProvider;
            _shortcutBindings = shortcutBindings;
            _monitor = monitor;
            Scale = scale;
            HorizontalAlignment = HorizontalAlignment.Left;
            VerticalAlignment = VerticalAlignment.Top;
            Background = Brushes.Transparent;
            BorderBrush = AppTheme.Outline;
            BorderThickness = new(0);
            CornerRadius = new(8);
            Padding = new(0);
            Child = new ThemeVariantScope { Child = _body };
            _commandRow = new() { Orientation = Orientation.Horizontal };
            InitializeAdditionalTools();
            _selectToolButton = _tools[ScreenshotTool.Select];
            _undo = RegisterAction(ToolbarActionId.Undo, session.Undo);
            _redo = RegisterAction(ToolbarActionId.Redo, session.Redo);
            _delete = RegisterAction(ToolbarActionId.DeleteAnnotation, session.DeleteSelectedAnnotation);
            _copy = RegisterAction(ToolbarActionId.Copy, () => session.RequestComplete(ScreenshotOutputAction.Copy));
            _save = RegisterAction(ToolbarActionId.Save, () => session.RequestComplete(ScreenshotOutputAction.Save));
            if (!imageEditing)
            {
                _scroll = RegisterAction(ToolbarActionId.ScrollCapture, () => session.RequestScrolling());
                _pin = RegisterAction(ToolbarActionId.Pin, () => session.RequestComplete(ScreenshotOutputAction.Pin));
            }

            if (execute is not null)
                _cancel = RegisterAction(ToolbarActionId.Cancel, () => execute(SelectionCommand.Cancel));
            _extraCommands.Add(SelectionCommand.ToggleAnnotations,
                RegisterAction(ToolbarActionId.ToggleAnnotations, session.ToggleAnnotations));
            _extraCommands.Add(SelectionCommand.ClearAnnotations,
                RegisterAction(ToolbarActionId.ClearAnnotations, session.ClearAnnotations));
            _extraCommands.Add(SelectionCommand.RenumberAnnotations,
                RegisterAction(ToolbarActionId.RenumberAnnotations, () => session.RenumberAnnotations()));
            _recognitionButtons.Add(RegisterAction(ToolbarActionId.RecognizeText,
                () => session.RequestRecognition(RecognitionMode.Text)));
            _recognitionButtons.Add(RegisterAction(ToolbarActionId.RecognizeBarcode,
                () => session.RequestRecognition(RecognitionMode.Barcode)));
            _outputColor = new() { MinWidth = 100, MinHeight = 24, Margin = new(6, 0) };
            _outputColor.Bind(ToolTip.TipProperty, LiveText.Resource("OutputColorMode"));
            _outputColor.SelectionChanged += (_, _) =>
            {
                if (!_refreshing && _outputColor.SelectedIndex >= 0)
                    session.OutputColorMode = (OutputColorMode)_outputColor.SelectedIndex;
            };
            var outputLabel = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
            outputLabel.Bind(TextBlock.TextProperty, LiveText.Resource("OutputColorMode"));
            _outputProperties.Content = new ThemeVariantScope
            {
                Child = new StackPanel { Spacing = 8, Children = { outputLabel, _outputColor } },
            };
            _outputButton = RegisterAction(ToolbarActionId.OutputColor, () => _outputProperties.ShowAt(_outputButton!));
            _more = SmallButton("ScreenshotMore", ShowMoreMenu);
            InitializeCustomizationMenu();
            _moveHandle = CreateMoveHandle();
            _commandRow.Children.Add(_moveHandle);
            _commandRow.Children.Add(_toolStrip);
            _commandRow.Children.Add(Group(_functionStrip));
            _commandRow.Children.Add(_more);
            _propertyPresenter = new(session, LoadWatermarkImageAsync, dialogScopeProvider);
            _contextFrame = Frame(_propertyPresenter, 3);
            _contextFrame.IsVisible = false;
            _body.Children.Add(Frame(_commandRow, 5));
            _body.Children.Add(_contextFrame);
            _body.Children.Add(_error);
            InitializePrivacyCommands();
            _session.Changed += SessionChanged;
            _session.ArrowCommentRequested += ArrowCommentRequested;
            UiText.LanguageChanged += RefreshHints;
            SessionChanged();
            RefreshHints();
        }

        public double Scale { get; set; }

        public bool IsDocked
        {
            get;
            set
            {
                field = value;
                _moveHandle.IsVisible = !value;
            }
        }

        public Rect OccupiedBounds => IsVisible && Opacity > 0
            ? new Rect(Margin.Left, Margin.Top, Bounds.Width > 0 ? Bounds.Width : DesiredSize.Width,
                Bounds.Height > 0 ? Bounds.Height : DesiredSize.Height)
            : default;

        public void Dispose()
        {
            _disposed = true;
            _propertyPresenter.Dispose();
            HideToolGroups();
            _outputProperties.Hide();
            _moreCommands.Hide();
            _session.Changed -= SessionChanged;
            _session.ArrowCommentRequested -= ArrowCommentRequested;
            UiText.LanguageChanged -= RefreshHints;
        }

        private static Border Frame(Control content, double padding)
        {
            return new()
            {
                Background = ToolbarSurface, BorderBrush = AppTheme.Outline,
                BorderThickness = new(1), CornerRadius = new(8), Padding = new(padding),
                HorizontalAlignment = HorizontalAlignment.Left, Child = content,
            };
        }

        private static Border Group(Control content)
        {
            return new()
            {
                Child = content, BorderBrush = AppTheme.Outline, BorderThickness = new(1, 0, 0, 0),
                Padding = new(3, 0, 0, 0), Margin = new(3, 0, 0, 0),
            };
        }

        private static Button SmallButton(string key, Action action, string? geometry = null)
        {
            var extent = AppTheme.CurrentAppearance.ToolbarIconSize switch
            {
                ToolbarIconSize.Small => 24,
                ToolbarIconSize.Large => 36,
                _ => 28,
            };
            var button = new Button
            {
                Tag = key, Width = extent, Height = extent, MinWidth = 0, MinHeight = 0,
                Padding = new(5), Margin = new(0), Foreground = AppTheme.Ink, Background = Brushes.Transparent,
                BorderThickness = new(0),
                Focusable = false,
            };
            button.Classes.Add("tool");
            var path = FluentToolbarIcon.Geometry(key) ?? geometry ?? ToolbarIcon.Geometry(key);
            var icon = key == "ScreenshotMore"
                ? ToolbarIcon.CreateInViewport(button, path)
                : ToolbarIcon.Create(button, path);
            icon.Width = icon.Height = extent - 12;
            button.Content = icon;
            button.SetValue(TooltipBindingProperty, button.Bind(ToolTip.TipProperty, LiveText.Resource(key)));
            button.Bind(AutomationProperties.NameProperty, LiveText.Resource(key));
            button.Click += (_, _) => action();
            return button;
        }

        private Button MenuButton(string key, Action action, string? geometry = null, bool close = true)
        {
            var button = SmallButton(key, () =>
            {
                if (close) _moreCommands.Hide();
                action();
            }, geometry);
            button.Width = double.NaN;
            button.Height = 30;
            button.HorizontalAlignment = HorizontalAlignment.Stretch;
            button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            var icon = (Control)button.Content!;
            button.Content = null;
            var label = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
            label.Bind(TextBlock.TextProperty, LiveText.Resource(key));
            var content = new Grid { ColumnDefinitions = new("18,*"), ColumnSpacing = 8 };
            content.Children.Add(icon);
            Grid.SetColumn(label, 1);
            content.Children.Add(label);
            button.Content = content;
            return button;
        }

        private void SelectTool(ScreenshotTool tool)
        {
            _session.CancelPrivacyDetection();
            _moreCommands.Hide();
            HideToolGroups();
            if (_session.Tool != tool || tool == ScreenshotTool.Select)
            {
                _session.ClearAnnotationSelection();
                _session.Tool = tool;
            }

            _propertyPresenter.Refresh(tool);
            _contextFrame.IsVisible = _propertyPresenter.HasContext && !_session.IsPrivacyDetectionActive;
        }

        private void UpdateGroup(Button button, ScreenshotTool tool, bool active)
        {
            var key = (string)_tools[tool].Tag!;
            if (!Equals(button.Tag, key))
            {
                button.Tag = key;
                SetToolButtonContent(button, key, tool);
                button.Bind(AutomationProperties.NameProperty, LiveText.Resource(key));
                SetShortcutTip(button, SelectionCommand.SelectTool, tool);
            }

            button.IsEnabled = !_session.IsBusy && (tool == ScreenshotTool.Select || _session.CanAnnotate);
            button.Background = active ? ActiveTool : Brushes.Transparent;
            button.Foreground = active ? Brushes.White : AppTheme.Ink;
            button.Classes.Set("active-tool", active);
            button.Classes.Set("primary", active);
        }

        private void SetShortcutTip(Button button, SelectionCommand command, ScreenshotTool? tool = null,
            string? detailKey = null)
        {
            var content = new StackPanel { Spacing = 6 };
            content.Children.Add(new TextBlock
            {
                Text = UiText.Get((string)button.Tag!), TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
            });
            if (detailKey is not null)
                content.Children.Add(new TextBlock
                {
                    Text = UiText.Get(detailKey), MaxWidth = 280, TextWrapping = TextWrapping.Wrap,
                    TextAlignment = TextAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center,
                });
            var keys = new WrapPanel
                { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            foreach (var gesture in _shortcutBindings.Where(binding => binding.Command == command &&
                                                                       (tool is null || binding.Tool == tool))
                         .Select(binding => binding.Gesture).Where(gesture => !string.IsNullOrWhiteSpace(gesture))
                         .Distinct())
                keys.Children.Add(new Border
                {
                    BorderBrush = AppTheme.Outline, BorderThickness = new(1), CornerRadius = new(3),
                    Padding = new(5, 2), Margin = new(2, 0),
                    Child = new TextBlock { Text = KeyGestureDisplay.Format(gesture), FontSize = 11 },
                });
            if (keys.Children.Count > 0) content.Children.Add(keys);
            button.GetValue(TooltipBindingProperty)?.Dispose();
            button.ClearValue(TooltipBindingProperty);
            ToolTip.SetTip(button, content);
        }

        private void RefreshHints()
        {
            if (_disposed) return;
            if (!Dispatcher.UIThread.CheckAccess())
            {
                Dispatcher.UIThread.Post(RefreshHints);
                return;
            }

            _propertyPresenter.Reset();
            _lastHdrAvailable = null;
            _scrollTip = null;
            SessionChanged();
            foreach (var pair in _tools)
                SetShortcutTip(pair.Value, SelectionCommand.SelectTool, pair.Key);
            foreach (var (button, tool) in _groupTools) SetShortcutTip(button, SelectionCommand.SelectTool, tool);
            if (_selectToolButton is not null)
                SetShortcutTip(_selectToolButton, SelectionCommand.SelectTool, ScreenshotTool.Select);
            foreach (var pair in _extraCommands) SetShortcutTip(pair.Value, pair.Key);
            SetShortcutTip(_copy, SelectionCommand.Copy);
            SetShortcutTip(_save, SelectionCommand.Save);
            SetShortcutTip(_undo, SelectionCommand.Undo);
            SetShortcutTip(_redo, SelectionCommand.Redo);
            SetShortcutTip(_recognitionButtons[0], SelectionCommand.RecognizeText);
            SetShortcutTip(_recognitionButtons[1], SelectionCommand.RecognizeQr);
        }

        private void SessionChanged()
        {
            RefreshToolbarPreferences();
            RefreshPrivacyCommands();
            var drawing = _session is
            {
                IsDrawing: true, IsDrawingPolyline: false, IsBusy: false,
                IsPrivacyDetectionActive: false,
            };
            Opacity = drawing ? 0 : 1;
            IsHitTestVisible = !drawing;
            if (_session.IsBusy)
            {
                _outputProperties.Hide();
                _moreCommands.Hide();
                HideToolGroups();
            }

            foreach (var button in _recognitionButtons)
                button.IsEnabled = _session is { CanAnnotate: true, IsDrawing: false, IsBusy: false };
            _undo.IsEnabled = _session is { CanUndo: true, IsBusy: false };
            _redo.IsEnabled = _session is { CanRedo: true, IsBusy: false };
            _copy.IsEnabled = _save.IsEnabled = _more.IsEnabled = !_session.IsBusy;
            _outputButton.IsEnabled = !_session.IsBusy;
            _pin?.IsEnabled = !_session.IsBusy;
            _cancel?.IsEnabled = true;
            if (_scroll is not null)
            {
                _scroll.IsEnabled = _session.CanStartScrolling;
                var tip = _session.OutputColorMode != OutputColorMode.Sdr ? "ScreenshotScrollRequiresSdr" :
                    _session.HasAnnotations ? "ScreenshotScrollUndoAnnotations" : "ScreenshotScroll";
                if (_scrollTip != tip)
                {
                    _scrollTip = tip;
                    SetShortcutTip(_scroll, SelectionCommand.ScrollVertical,
                        detailKey: tip == "ScreenshotScroll" ? null : tip);
                }
            }

            _refreshing = true;
            try
            {
                if (_lastHdrAvailable != _session.CanOutputHdr)
                {
                    _outputColor.ItemsSource = _session.CanOutputHdr
                        ? new[] { UiText.Get("ColorModeSdr"), UiText.Get("ColorModeHdr"), UiText.Get("ColorModeBoth") }
                        : new[] { UiText.Get("ColorModeSdr") };
                    _lastHdrAvailable = _session.CanOutputHdr;
                }

                _outputColor.SelectedIndex = (int)_session.OutputColorMode;
            }
            finally
            {
                _refreshing = false;
            }

            _outputButton.IsVisible = _outputButton.Parent is not null && _session.CanOutputHdr;
            _delete.IsEnabled = _session is { HasSelectedAnnotation: true, IsBusy: false };
            foreach (var button in _extraCommands.Values)
                button.IsEnabled = _session is { IsBusy: false, HasAnnotations: true };
            var tool = _session.SelectedTool ?? _session.Tool;
            _propertyPresenter.Refresh(tool);
            _propertyPresenter.IsEnabled = !_session.IsBusy;
            _contextFrame.IsVisible = _propertyPresenter.HasContext && !_session.IsPrivacyDetectionActive;
            RefreshToolGroups(_session.Tool);

            _error.Text = _session.Error;
            _error.IsVisible = !string.IsNullOrEmpty(_session.Error);
            foreach (var pair in _tools)
            {
                pair.Value.IsEnabled = !_session.IsBusy && (pair.Key == ScreenshotTool.Select || _session.CanAnnotate);
                pair.Value.Background = Brushes.Transparent;
                pair.Value.Classes.Remove("active-tool");
                ((RadioButton)pair.Value).IsChecked = pair.Key == _session.Tool;
            }

            if (_ownsToolbar && !_placedRegion.IsEmpty) Refresh(_placedRegion, _placementMouse, true);
        }

        public void Refresh(PixelRect selected, PixelPoint mouse, bool ownsToolbar)
        {
            _ownsToolbar = ownsToolbar;
            _placementMouse = mouse;
            IsVisible = ownsToolbar && !selected.IsEmpty;
            if (!IsVisible) _placedSize = default;
            if (IsDocked)
            {
                Margin = default;
                Width = double.NaN;
                MaxWidth = double.PositiveInfinity;
                MaxHeight = 180;
                HorizontalAlignment = HorizontalAlignment.Stretch;
                return;
            }

            if (!IsVisible) return;
            var available = new Size(Math.Max(1, Math.Min(600, _monitor.Width / Scale - 16)),
                Math.Max(1, _monitor.Height / Scale - 16));
            MaxWidth = available.Width;
            MaxHeight = available.Height;
            var horizontalChrome = Padding.Left + Padding.Right + BorderThickness.Left + BorderThickness.Right;
            _body.Measure(available);
            Width = Math.Min(Math.Ceiling((_body.DesiredSize.Width + horizontalChrome) * Scale) / Scale + 2,
                available.Width);
            var verticalChrome = Padding.Top + Padding.Bottom + BorderThickness.Top + BorderThickness.Bottom;
            _body.Measure(new(Math.Max(1, Width - horizontalChrome), Math.Max(1, available.Height - verticalChrome)));
            var width = Width;
            var height = Math.Min(_body.DesiredSize.Height + verticalChrome, available.Height);
            var measured = new Size(width, height);
            if (_placedRegion == selected && _placedSize == measured && _placedScale.Equals(Scale)) return;
            _placedRegion = selected;
            _placedSize = measured;
            _placedScale = Scale;
            var selection = new Rect((selected.X - _monitor.X) / Scale, (selected.Y - _monitor.Y) / Scale,
                selected.Width / Scale, selected.Height / Scale);
            var viewport = new Size(_monitor.Width / Scale, _monitor.Height / Scale);
            var position = TryGetManualPosition(viewport, measured, out var manual)
                ? manual
                : ScreenshotToolbarLayout.Place(viewport, selection, measured);
            Margin = new(position.X, position.Y, 0, 0);
        }
    }
}
