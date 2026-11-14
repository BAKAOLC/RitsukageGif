using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace RitsukageGif.Desktop
{
    internal sealed partial class ScreenshotToolbar : Border, IDisposable
    {
        private static readonly IBrush ToolbarSurface = AppTheme.Surface;

        private static readonly AttachedProperty<IDisposable?> TooltipBindingProperty =
            AvaloniaProperty.RegisterAttached<ScreenshotToolbar, Button, IDisposable?>("TooltipBinding");

        private readonly StackPanel _body = new() { Spacing = 3 };
        private readonly Button? _cancel;
        private readonly WrapPanel _commandRow;
        private readonly ScrollViewer _commandScroll;
        private readonly Border _contextFrame;
        private readonly Button _copy;
        private readonly Button _delete;
        private readonly Func<IDisposable>? _dialogScopeProvider;
        private readonly TextBlock _error = new() { Foreground = Brushes.OrangeRed, TextWrapping = TextWrapping.Wrap };
        private readonly PixelRect _monitor;
        private readonly Button _more;
        private readonly HoverPropertyPanel _moreCommands;
        private readonly Button _moveHandle;
        private readonly Button _outputButton;
        private readonly ComboBox _outputColor;
        private readonly Flyout _outputProperties = new() { OverlayDismissEventPassThrough = true };
        private readonly Button? _pin;
        private readonly AnnotationPropertyPresenter _propertyPresenter;
        private readonly Button _quickSave;
        private readonly List<Button> _recognitionButtons = [];
        private readonly Button? _record;
        private readonly Button _redo;
        private readonly Button _save;
        private readonly Button? _scroll;
        private readonly ScreenshotAnnotationSession _session;
        private readonly Func<IReadOnlyList<SelectionCommandDefinition>> _shortcutBindingsProvider;
        private readonly Button _undo;
        private bool _disposed;
        private bool? _lastHdrAvailable;
        private bool _ownsToolbar;
        private PixelRect _placedRegion;
        private double _placedScale;
        private Size _placedSize;
        private PixelPoint _placementMouse;
        private bool _refreshing;
        private IReadOnlyList<SelectionCommandDefinition> _shortcutBindings;

        public ScreenshotToolbar(ScreenshotAnnotationSession session, PixelRect monitor, double scale,
            IReadOnlyList<SelectionCommandDefinition> shortcutBindings, Action<SelectionCommand>? execute = null,
            bool imageEditing = false, Func<IDisposable>? dialogScopeProvider = null,
            Func<IReadOnlyList<SelectionCommandDefinition>>? shortcutBindingsProvider = null)
        {
            _session = session;
            _dialogScopeProvider = dialogScopeProvider;
            _shortcutBindings = shortcutBindings;
            _shortcutBindingsProvider = shortcutBindingsProvider ?? SelectionCommands.SnapshotBindings;
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
            _commandScroll = new()
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = _commandRow,
            };
            _annotationTools = new(session, () => _ = EditToolbarAsync(true));
            _toolStrip.Children.Add(_annotationTools);
            _undo = RegisterAction(ToolbarActionId.Undo, session.Undo);
            _redo = RegisterAction(ToolbarActionId.Redo, session.Redo);
            _delete = RegisterAction(ToolbarActionId.DeleteAnnotation, session.DeleteSelectedAnnotation);
            _copy = RegisterAction(ToolbarActionId.Copy, () => session.RequestComplete(ScreenshotOutputAction.Copy));
            _save = RegisterAction(ToolbarActionId.Save, () => session.RequestComplete(ScreenshotOutputAction.Save));
            _quickSave = RegisterAction(ToolbarActionId.QuickSave,
                () => session.RequestComplete(ScreenshotOutputAction.QuickSave));
            RegisterAction(ToolbarActionId.ExportPdf, () => session.RequestComplete(ScreenshotOutputAction.ExportPdf));
            if (ScreenshotPrintService.IsSupported)
                RegisterAction(ToolbarActionId.Print, () => session.RequestComplete(ScreenshotOutputAction.Print));
            if (!imageEditing)
            {
                RegisterAction(ToolbarActionId.Edit, () => session.RequestComplete(ScreenshotOutputAction.Edit));
                _scroll = RegisterAction(ToolbarActionId.ScrollCapture, () => session.RequestScrolling());
                _record = RegisterAction(ToolbarActionId.Record, session.RequestRecording);
                _pin = RegisterAction(ToolbarActionId.Pin, () => session.RequestComplete(ScreenshotOutputAction.Pin));
                RegisterAction(ToolbarActionId.WindowPin, session.RequestWindowPin);
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
                () => execute?.Invoke(SelectionCommand.RecognizeTextAndCopy)));
            _recognitionButtons.Add(RegisterAction(ToolbarActionId.RecognizeBarcode,
                () => session.RequestRecognition(RecognitionMode.Barcode)));
            _extraCommands.Add(SelectionCommand.CopyCodeText,
                RegisterAction(ToolbarActionId.CopyCodeText, () => execute?.Invoke(SelectionCommand.CopyCodeText)));
            _recognitionButtons.Add(RegisterAction(ToolbarActionId.Translate, session.RequestTranslation));
            _recognitionButtons.Add(RegisterAction(ToolbarActionId.RecognizeTable,
                () => session.RequestRecognition(RecognitionMode.Table)));
            _recognitionButtons.Add(RegisterAction(ToolbarActionId.RecognizeFormula,
                () => session.RequestRecognition(RecognitionMode.Formula)));
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
            _more = SmallButton("ScreenshotMore");
            _moreCommands = CreateMoreMenu();
            _moveHandle = CreateMoveHandle();
            _commandRow.Children.Add(_moveHandle);
            _commandRow.Children.Add(_toolStrip);
            _commandRow.Children.Add(Group(_undo));
            _commandRow.Children.Add(Group(_functionStrip));
            _commandRow.Children.Add(_more);
            _propertyPresenter = new(session, dialogScopeProvider);
            _contextFrame = Frame(_propertyPresenter, 3);
            _contextFrame.IsVisible = false;
            _body.Children.Add(Frame(_commandScroll, 3));
            _body.Children.Add(_contextFrame);
            _body.Children.Add(_error);
            _session.Changed += SessionChanged;
            _session.ArrowCommentRequested += ArrowCommentRequested;
            UiText.LanguageChanged += RefreshHints;
            SelectionCommands.BindingsChanged += ShortcutBindingsChanged;
            SessionChanged();
            RefreshHints();
            InitializePrintCapabilities();
        }

        public double Scale { get; set; }

        public bool IsDocked
        {
            get;
            set
            {
                field = value;
                _moveHandle.IsVisible = !value;
                _commandScroll.HorizontalScrollBarVisibility = value
                    ? ScrollBarVisibility.Disabled
                    : ScrollBarVisibility.Auto;
            }
        }

        public Rect OccupiedBounds => IsVisible && Opacity > 0
            ? new Rect(_desktopPlacement is null ? new(Margin.Left, Margin.Top) : _displayedPosition,
                new Size((!IsDocked && _placedSize.Width > 0 ? _placedSize.Width : Bounds.Width) * _displayedScale,
                    (!IsDocked && _placedSize.Height > 0 ? _placedSize.Height : Bounds.Height) * _displayedScale))
            : default;

        public void Dispose()
        {
            _disposed = true;
            _printCapabilityLifetime.Cancel();
            _printCapabilityLifetime.Dispose();
            _propertyPresenter.Dispose();
            _annotationTools.Dispose();
            HideToolGroups();
            _outputProperties.Hide();
            _moreCommands.Dispose();
            _session.Changed -= SessionChanged;
            _session.ArrowCommentRequested -= ArrowCommentRequested;
            UiText.LanguageChanged -= RefreshHints;
            SelectionCommands.BindingsChanged -= ShortcutBindingsChanged;
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
                Padding = new(2, 0, 0, 0), Margin = new(2, 0, 0, 0),
            };
        }

        internal static Button SmallButton(string key, Action? action = null, IconDefinition? geometry = null)
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
            var definition = ToolbarIcon.FindDefinition(key) ?? geometry ?? ToolbarIcon.GetDefinition(key);
            var icon = ToolbarIcon.Create(button, definition);
            icon.Width = icon.Height = extent - 12;
            button.Content = icon;
            button.SetValue(TooltipBindingProperty, button.Bind(ToolTip.TipProperty, LiveText.Resource(key)));
            button.Bind(AutomationProperties.NameProperty, LiveText.Resource(key));
            if (action is not null) button.Click += (_, _) => action();
            return button;
        }

        private Button MenuButton(string key, Action action, IconDefinition? geometry = null, bool close = true)
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

        private void SetShortcutTip(Button button, SelectionCommand command, ScreenshotTool? tool = null,
            string? detailKey = null)
        {
            SetShortcutTip(button, command, _shortcutBindings, tool, detailKey);
        }

        internal static void SetShortcutTip(Button button, SelectionCommand command,
            IReadOnlyList<SelectionCommandDefinition> bindings, ScreenshotTool? tool = null, string? detailKey = null)
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
            foreach (var gesture in bindings.Where(binding => binding.Command == command &&
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
            RefreshMoveHandleTip(_moveHandle);
            _lastHdrAvailable = null;
            SessionChanged();
            RefreshShortcutHints();
        }

        private void ShortcutBindingsChanged()
        {
            if (_disposed) return;
            if (!Dispatcher.UIThread.CheckAccess())
            {
                Dispatcher.UIThread.Post(ShortcutBindingsChanged);
                return;
            }

            _shortcutBindings = _shortcutBindingsProvider();
            RefreshShortcutHints();
        }

        private void RefreshShortcutHints()
        {
            _annotationTools.RefreshShortcuts(_shortcutBindings);
            foreach (var pair in _extraCommands) SetShortcutTip(pair.Value, pair.Key);
            SetShortcutTip(_copy, SelectionCommand.Copy);
            SetShortcutTip(_save, SelectionCommand.Save);
            SetShortcutTip(_quickSave, SelectionCommand.QuickSave);
            if (_actionButtons.TryGetValue(ToolbarActionId.Print, out var printAction))
                SetShortcutTip(printAction, SelectionCommand.Print,
                    detailKey: ScreenshotPrintService.IsAvailable ? null : "PrintUnavailable");
            if (_cancel is not null) SetShortcutTip(_cancel, SelectionCommand.Cancel);
            if (_actionButtons.TryGetValue(ToolbarActionId.WindowPin, out var windowPin))
                SetShortcutTip(windowPin, SelectionCommand.WindowPin);
            if (_record is not null) SetShortcutTip(_record, SelectionCommand.Record);
            if (_scroll is not null) SetShortcutTip(_scroll, SelectionCommand.ScrollVertical);
            SetShortcutTip(_undo, SelectionCommand.Undo);
            SetShortcutTip(_redo, SelectionCommand.Redo);
            SetShortcutTip(_recognitionButtons[0], SelectionCommand.RecognizeTextAndCopy);
            SetShortcutTip(_recognitionButtons[1], SelectionCommand.RecognizeQr);
            SetShortcutTip(_recognitionButtons[2], SelectionCommand.Translate);
            SetShortcutTip(_recognitionButtons[3], SelectionCommand.RecognizeTable);
            SetShortcutTip(_recognitionButtons[4], SelectionCommand.RecognizeFormula);
        }

        private void SessionChanged()
        {
            RefreshToolbarPreferences();
            var drawing = _session is
            {
                IsDrawing: true, IsDrawingPolyline: false, IsBusy: false,
                IsPrivacyDetectionActive: false,
            };
            _hideWhileDrawing = drawing;
            if (_desktopPlacement is null)
            {
                Opacity = drawing ? 0 : 1;
                IsHitTestVisible = !drawing;
            }
            else
            {
                UpdateDesktopVisibility();
            }

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
            _copy.IsEnabled = _save.IsEnabled = _quickSave.IsEnabled = _more.IsEnabled = !_session.IsBusy;
            if (_actionButtons.TryGetValue(ToolbarActionId.ExportPdf, out var documentAction))
                documentAction.IsEnabled = !_session.IsBusy;
            if (_actionButtons.TryGetValue(ToolbarActionId.Edit, out var editAction))
                editAction.IsEnabled = !_session.IsBusy;
            if (_actionButtons.TryGetValue(ToolbarActionId.Print, out var printAction))
                printAction.IsEnabled = !_session.IsBusy && ScreenshotPrintService.IsAvailable;
            _outputButton.IsEnabled = !_session.IsBusy;
            _pin?.IsEnabled = !_session.IsBusy;
            _record?.IsEnabled = _session.CanStartRecording;
            if (_actionButtons.TryGetValue(ToolbarActionId.WindowPin, out var windowPin))
                windowPin.IsEnabled = _session.CanPinWindow;
            _cancel?.IsEnabled = true;
            _scroll?.IsEnabled = _session.CanStartScrolling;

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
            _propertyPresenter.IsEnabled = _session is { IsBusy: false, PrivacyDetectionPending: false };
            _contextFrame.IsVisible = _propertyPresenter.HasContext;
            _annotationTools.Refresh();

            _error.Text = _session.Error;
            _error.IsVisible = !string.IsNullOrEmpty(_session.Error);
            if ((_ownsToolbar || (IsVisible && _desktopPlacement is not null)) && !_placedRegion.IsEmpty)
                Refresh(_placedRegion, _placementMouse, _ownsToolbar);
        }

        public void Refresh(PixelRect selected, PixelPoint mouse, bool ownsToolbar)
        {
            var desktopPlacement = _desktopPlacement;
            if (desktopPlacement is not null && _desktopOverlay is { } overlay)
                ownsToolbar = desktopPlacement.OwnsDesktopScreenshotToolbar(overlay, selected);

            _ownsToolbar = ownsToolbar;
            _placementMouse = mouse;
            var manual = desktopPlacement?.ManualScreenshotToolbar;
            var visible = !selected.IsEmpty && _session.ToolbarVisible;
            IsVisible = visible &&
                        (ownsToolbar || (manual is not null && desktopPlacement!.DesktopScreenshotToolbarVisible));
            if (!IsVisible)
            {
                _placedSize = default;
                HideToolGroups();
                _moreCommands.Hide();
                _outputProperties.Hide();
                _propertyPresenter.HideFlyouts();
            }

            if (IsDocked)
            {
                ArrangeFloatingLayers(false);
                _moreCommands.SetPlacement(PlacementMode.Bottom);
                Margin = default;
                Width = double.NaN;
                MaxWidth = double.PositiveInfinity;
                MaxHeight = 180;
                HorizontalAlignment = HorizontalAlignment.Stretch;
                return;
            }

            if (!IsVisible)
            {
                _desktopPrimary = false;
                return;
            }

            var viewport = new Size(_monitor.Width / Scale, _monitor.Height / Scale);
            var referenceViewport = manual?.Viewport ?? viewport;
            var referenceScale = manual?.Scale ?? Scale;
            var available = new Size(Math.Max(1, referenceViewport.Width - 16),
                Math.Max(1, referenceViewport.Height - 16));
            MaxWidth = available.Width;
            MaxHeight = available.Height;
            var horizontalChrome = Padding.Left + Padding.Right + BorderThickness.Left + BorderThickness.Right;
            _body.Measure(available);
            Width = Math.Min(Math.Ceiling((_body.DesiredSize.Width + horizontalChrome) * referenceScale) /
                referenceScale + 2,
                available.Width);
            var verticalChrome = Padding.Top + Padding.Bottom + BorderThickness.Top + BorderThickness.Bottom;
            _body.Measure(new(Math.Max(1, Width - horizontalChrome), Math.Max(1, available.Height - verticalChrome)));
            var width = Width;
            var height = Math.Min(_body.DesiredSize.Height + verticalChrome, available.Height);
            var measured = new Size(width, height);
            if (manual is not null)
            {
                _placedRegion = selected;
                _placedSize = measured;
                _placedScale = 0;
                UpdateDesktopDragPosition();
                return;
            }

            if (_placedRegion == selected && _placedSize == measured && _placedScale.Equals(Scale)) return;
            _placedRegion = selected;
            _placedSize = measured;
            _placedScale = Scale;
            var selection = new Rect((selected.X - _monitor.X) / Scale, (selected.Y - _monitor.Y) / Scale,
                selected.Width / Scale, selected.Height / Scale);
            if (!TryGetManualPosition(viewport, measured, out var position))
            {
                var placement = ScreenshotToolbarLayout.PlaceFloatingCapture(viewport, selection, measured);
                position = placement.Position;
                DesktopAbove = placement.Above;
                _moreCommands.SetPlacement(placement.Above ? PlacementMode.Top : PlacementMode.Bottom);
                ArrangeFloatingLayers(placement.Above);
            }

            if (desktopPlacement is null)
            {
                Margin = new(position.X, position.Y, 0, 0);
            }
            else
            {
                PlaceDesktop(position, 1);
                SetDesktopInteraction(true, true, true);
            }
        }

        private void ArrangeFloatingLayers(bool above)
        {
            var target = above ? 0 : 1;
            var current = _body.Children.IndexOf(_contextFrame);
            if (current != target) _body.Children.Move(current, target);
        }
    }
}
