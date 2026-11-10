using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    internal sealed class PinnedAnnotationToolbar : Window, IDisposable
    {
        private static readonly ILogger Logger = LogHub.GetLogger("Desktop.Pins.Toolbar");
        private readonly ScreenshotAnnotationSession _session;
        private readonly Window _owner;
        private readonly AnnotationToolsStrip _tools;
        private readonly AnnotationPropertyPresenter _properties;
        private readonly Border _propertyFrame;
        private readonly Button _undo;
        private readonly Button _save;
        private readonly Button _done;
        private readonly Func<IReadOnlyList<SelectionCommandDefinition>> _shortcuts;
        private object? _preferences;
        private bool _disposed;
        private bool _manualPosition;
        private bool _refreshing;

        internal PinnedAnnotationToolbar(Window owner, ScreenshotAnnotationSession session,
            Func<IReadOnlyList<SelectionCommandDefinition>> shortcuts, Action save, Action done)
        {
            _owner = owner;
            _session = session;
            _shortcuts = shortcuts;
            ShowInTaskbar = false;
            ShowActivated = false;
            WindowDecorations = WindowDecorations.None;
            CanResize = false;
            SizeToContent = SizeToContent.WidthAndHeight;
            this[!TopmostProperty] = owner[!TopmostProperty];
            Background = Brushes.Transparent;
            TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
            var handle = ScreenshotToolbar.SmallButton("ToolbarMove", () => { });
            handle.Width = 14;
            handle.Padding = new(2, 0);
            if (handle.Content is Control icon)
            {
                icon.Width = 8;
                icon.Height = 14;
            }

            handle.AddHandler(PointerPressedEvent, (_, args) =>
            {
                if (!args.GetCurrentPoint(handle).Properties.IsLeftButtonPressed) return;
                args.Handled = true;
                if (args.ClickCount == 2)
                {
                    _manualPosition = false;
                    Place();
                }
                else
                {
                    _manualPosition = true;
                    BeginMoveDrag(args);
                }
            }, RoutingStrategies.Tunnel, true);
            row.Children.Add(handle);
            _tools = new(session, () => _ = AdjustToolsAsync());
            row.Children.Add(_tools);
            _undo = ScreenshotToolbar.SmallButton("AnimationUndo", session.Undo);
            _undo.Margin = new(4, 0, 0, 0);
            row.Children.Add(_undo);
            _save = ScreenshotToolbar.SmallButton("ScreenshotSave", save);
            row.Children.Add(_save);
            _done = ScreenshotToolbar.SmallButton("PinnedAnnotationDone", done,
                ToolbarIcon.GetDefinition("ScreenshotApplyText"));
            row.Children.Add(_done);
            _properties = new(session);
            _propertyFrame = Frame(_properties);
            Content = new ThemeVariantScope
            {
                Child = new StackPanel { Spacing = 4, Children = { Frame(row), _propertyFrame } },
            };
            _session.Changed += Refresh;
            _session.ArrowCommentRequested += EditComment;
            UiText.LanguageChanged += RefreshHints;
            SelectionCommands.BindingsChanged += RefreshHints;
            _owner.PositionChanged += OwnerPositionChanged;
            _owner.SizeChanged += OwnerSizeChanged;
            _owner.ScalingChanged += OwnerScalingChanged;
            ScalingChanged += OwnerScalingChanged;
            SizeChanged += OwnerSizeChanged;
            KeyDown += (_, args) => _owner.RaiseEvent(args);
            KeyUp += (_, args) => _owner.RaiseEvent(args);
            Refresh();
            RefreshHints();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _session.Changed -= Refresh;
            _session.ArrowCommentRequested -= EditComment;
            UiText.LanguageChanged -= RefreshHints;
            SelectionCommands.BindingsChanged -= RefreshHints;
            _owner.PositionChanged -= OwnerPositionChanged;
            _owner.SizeChanged -= OwnerSizeChanged;
            _owner.ScalingChanged -= OwnerScalingChanged;
            ScalingChanged -= OwnerScalingChanged;
            SizeChanged -= OwnerSizeChanged;
            _properties.Dispose();
            _tools.Dispose();
            Close();
        }

        private static Border Frame(Control content)
        {
            return new()
            {
                Child = content, Padding = new(3), CornerRadius = new(8),
                BorderBrush = AppTheme.Outline, BorderThickness = new(1), Background = AppTheme.Surface,
                HorizontalAlignment = HorizontalAlignment.Left,
            };
        }

        private void Refresh()
        {
            if (_disposed || _refreshing) return;
            _refreshing = true;
            try
            {
                var preferences = _session.ToolbarPreferencesIdentity;
                if (!ReferenceEquals(preferences, _preferences))
                {
                    _preferences = preferences;
                    _tools.Rebuild([
                        .. _session.ToolbarPreferences.Columns.Select(IReadOnlyList<ScreenshotTool> (column) => column),
                    ]);
                }

                _properties.Refresh(_session.Tool);
                _propertyFrame.IsVisible = _properties.HasContext;
                _undo.IsEnabled = _session is { IsBusy: false, CanUndo: true };
                _save.IsEnabled = _done.IsEnabled = _session is { IsBusy: false, IsDrawing: false };
            }
            finally
            {
                _refreshing = false;
            }
        }

        private void RefreshHints()
        {
            if (_disposed) return;
            var bindings = _shortcuts();
            _tools.RefreshShortcuts(bindings);
            ScreenshotToolbar.SetShortcutTip(_undo, SelectionCommand.Undo, bindings);
            ScreenshotToolbar.SetShortcutTip(_save, SelectionCommand.Save, bindings);
            ScreenshotToolbar.SetShortcutTip(_done, SelectionCommand.Confirm, bindings);
        }

        private void EditComment()
        {
            _properties.EditComment();
            _propertyFrame.IsVisible = _properties.HasContext;
        }

        private void OwnerPositionChanged(object? sender, PixelPointEventArgs args)
        {
            Place();
        }

        private void OwnerSizeChanged(object? sender, SizeChangedEventArgs args)
        {
            Place();
        }

        internal void Place()
        {
            if (_disposed || _manualPosition) return;
            var screen = _owner.Screens.ScreenFromWindow(_owner) ?? _owner.Screens.Primary;
            if (screen is null) return;
            var area = screen.WorkingArea;
            var scale = _owner.RenderScaling;
            var location = ScreenshotToolbarLayout.Place(new(area.Width / scale, area.Height / scale),
                new((_owner.Position.X - area.X) / scale, (_owner.Position.Y - area.Y) / scale,
                    _owner.Bounds.Width, _owner.Bounds.Height), Bounds.Size);
            Position = new(area.X + (int)Math.Round(location.X * scale), area.Y + (int)Math.Round(location.Y * scale));
        }

        private void OwnerScalingChanged(object? sender, EventArgs args)
        {
            Place();
        }

        private async Task AdjustToolsAsync()
        {
            try
            {
                _tools.HideFlyouts();
                _properties.HideFlyouts();
                if (await new ToolbarToolsWindow(_session.ToolbarPreferences).ShowDialog<ToolbarPreferences?>(_owner) is
                        { } result && !_disposed)
                    _session.UpdateToolbarPreferences(result, true);
            }
            catch (Exception error)
            {
                Logger.ZLogWarning(error, $"Pinned annotation toolbar configuration failed.");
                if (!_disposed) _session.SetExportState(false, error.Message);
            }
        }
    }
}
