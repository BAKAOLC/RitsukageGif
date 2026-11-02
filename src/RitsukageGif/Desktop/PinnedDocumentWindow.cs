using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    internal sealed partial class PinnedDocumentWindow : Window, IPinnedWindow, IDisposable
    {
        private static readonly ILogger Logger = LogHub.GetLogger("Desktop.Pins.Documents");
        private readonly Func<bool> _canRestoreHidden;
        private readonly Grid _content;
        private readonly LayoutTransformControl _layout;
        private readonly CancellationTokenSource _lifetime = new();
        private readonly SimulatedWheelInput _simulatedWheel;

        private readonly TextBlock _status = new()
        {
            IsVisible = false, TextWrapping = TextWrapping.Wrap, Margin = new(8),
            Background = AppTheme.Surface, Foreground = AppTheme.Ink,
            VerticalAlignment = VerticalAlignment.Bottom,
        };

        private readonly Border _surface;
        private readonly ScrollViewer _viewport;
        private bool _busy;
        private bool _closed;
        private long _createdAtUtcTicks = DateTimeOffset.UtcNow.UtcTicks;
        private PinnedRichResourceLoader? _htmlResources;
        private nint _inputStyle;
        private bool _locked;
        private bool _mouseThrough;
        private double? _opacityBeforeThrough;
        private bool _selected;
        private IPinnedDocumentTextView? _textView;
        private double _zoom = 1;

        public PinnedDocumentWindow(PinnedDocument document, Func<bool> canRestoreHidden,
            PinnedImagePreferences preferences)
        {
            _simulatedWheel = new(this);
            Document = document;
            _canRestoreHidden = canRestoreHidden;
            PreferencesProvider = () => preferences;
            Selectable = preferences.SelectableText;
            var heading = document switch
            {
                PinnedFilesDocument files => Path.GetFileName(Path.TrimEndingDirectorySeparator(files.Paths[0])),
                PinnedFormulaDocument formula => formula.Sources[0],
                _ => document.PlainText,
            };
            Title = heading[..Math.Min(heading.Length, 80)].Split('\n', 2)[0].Trim();
            if (Title.Length > 80) Title = Title[..80];
            WindowDecorations = WindowDecorations.None;
            ShowInTaskbar = !canRestoreHidden();
            Topmost = preferences.Topmost;
            Opacity = preferences.Opacity;
            Width = document is PinnedColorDocument ? 240 : 440;
            Height = document is PinnedColorDocument ? 200 : 320;
            MinWidth = 120;
            MinHeight = 72;
            Background = Brushes.Transparent;
            _surface = new() { Background = AppTheme.Surface, Padding = new(10) };
            _layout = new() { Child = _surface };
            _viewport = new()
            {
                Content = _layout, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            };
            _content = new() { Children = { _viewport, _status } };
            Presentation = new(this, _content, () => PreferencesProvider());
            Presentation.Title.Changed += () => StateChanged?.Invoke(this, EventArgs.Empty);
            Presentation.MoveGroupRequested = () => RunAsync(EditGroupAsync);
            Content = _content;
            RebuildContent();
            RefreshPreferencePresentation();
            FitInitialContent();
            var menu = CreateMenu();
            _content.ContextMenu = menu;
            ConfigureInput(menu);
            Closed += (_, _) =>
            {
                _closed = true;
                _mouseClicks.Dispose();
                _textView?.ReleaseContent();
                _htmlResources?.Dispose();
                _documentBitmap?.Dispose();
                _lifetime.Cancel();
                if (!_busy) _lifetime.Dispose();
            };
            Opened += (_, _) => RestoreToScreen();
            PositionChanged += (_, _) => StateChanged?.Invoke(this, EventArgs.Empty);
            SizeChanged += (_, _) => StateChanged?.Invoke(this, EventArgs.Empty);
            _viewport.ScrollChanged += (_, _) => StateChanged?.Invoke(this, EventArgs.Empty);
            PropertyChanged += (_, args) =>
            {
                if (args.Property == OpacityProperty ||
                    args.Property == IsVisibleProperty || args.Property == TopmostProperty)
                    StateChanged?.Invoke(this, EventArgs.Empty);
            };
        }

        public PinnedWindowPresentation Presentation { get; }

        public PinnedDocument Document { get; }
        public string Id { get; private set; } = Guid.NewGuid().ToString("N");
        public string Group { get; private set; } = "";
        private bool IgnoreFormat { get; set; }
        private bool Selectable { get; set; }
        public bool WasDestroyed { get; private set; }
        public Func<PinnedImagePreferences> PreferencesProvider { get; init; }
        public Func<ScreenshotSavePreferences> SavePreferencesProvider { get; init; } = () => new();
        public Func<ScreenshotClipboardPreferences> ClipboardPreferencesProvider { get; init; } = () => new();
        public Func<string?> OutputDirectoryProvider { get; init; } = () => null;
        public Func<IReadOnlyList<PinnedGroupDefinition>> GroupsProvider { get; init; } = () => [];
        public Action<string, bool>? GroupVisibilityRequested { get; init; }
        public Action<IPinnedWindow, string>? GroupChangeRequested { get; init; }
        public Func<IPinnedWindow, Task>? NewGroupRequested { get; init; }
        public Action<IPinnedWindow>? SelectionRequested { get; init; }
        public Action<IPinnedWindow, Avalonia.PixelPoint>? MoveSelectionRequested { get; init; }
        public Action<IPinnedWindow, bool>? CloseRequested { get; init; }
        public Func<IPinnedWindow, PinnedImageArrangement, bool>? CanArrangeSelectionProvider { get; init; }

        public void Dispose()
        {
            if (!_closed) Close();
        }
        public Action<IPinnedWindow, PinnedImageArrangement>? ArrangeSelectionRequested { get; init; }
        public Func<IPinnedWindow, Task>? SaveSelectionRequested { get; init; }
        public Action<string>? SavedPathChanged { get; init; }

        public bool IsCloseLocked => PinnedInputContext.IsCloseBlocked(_locked, PreferencesProvider().LockRestrictions);
        public bool IsInteractionLocked => IsOperationLocked(PinnedLockRestrictions.Move);
        public bool IsIncomingDropLocked => IsOperationLocked(PinnedLockRestrictions.IncomingDrop);
        public bool CanArrangeSelection => !_closed && !IsInteractionLocked;
        public bool CanSaveSelection => !_closed;
        public CancellationToken OutputLifetime => _lifetime.Token;
        Window IPinnedWindow.Window => this;

        public PixelRect SelectionLayoutBounds => new(Position.X, Position.Y,
            Math.Max(1, (int)Math.Ceiling(Bounds.Width * RenderScaling)),
            Math.Max(1, (int)Math.Ceiling(Bounds.Height * RenderScaling)));

        public void SetGroup(string value)
        {
            if (value.Length != 0 && !Guid.TryParseExact(value, "N", out _))
                throw new ArgumentException("Invalid pin group identifier.", nameof(value));
            Group = value;
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        public void SetSelected(bool value)
        {
            _selected = value;
            RefreshPreferencePresentation();
        }

        public void HideImage()
        {
            Presentation.SetUserVisibility(false);
            if (_canRestoreHidden()) Hide();
            else WindowState = WindowState.Minimized;
        }

        public void RevealImage()
        {
            Presentation.SetUserVisibility(true);
            Show();
            WindowState = WindowState.Normal;
            RestoreToScreen();
        }

        public void DestroyForGroup()
        {
            WasDestroyed = true;
            Close();
        }

        public void CloseImage(bool destroy)
        {
            WasDestroyed = destroy;
            Close();
        }

        public void RestoreInteraction()
        {
            SetMouseThrough(false);
            _locked = false;
            RevealImage();
            RefreshPreferencePresentation();
        }

        public event EventHandler? StateChanged;

        private bool IsOperationLocked(PinnedLockRestrictions operation)
        {
            return _locked && (PreferencesProvider().LockRestrictions & operation) != 0;
        }

        public void RefreshPreferencePresentation()
        {
            Presentation.Title.Refresh();
            Presentation.Attention.Refresh();
            var preferences = PreferencesProvider();
            ShowInTaskbar = !_canRestoreHidden() || (!Topmost && preferences.ShowTaskbarWhenNotTopmost);
            _surface.BorderThickness = new(_selected ? 2 : 1);
            _surface.BorderBrush = _selected ? AppTheme.Accent : AppTheme.Outline;
            _surface.CornerRadius = new(3);
            _surface.BoxShadow = preferences.ShowWindowShadow
                ? new BoxShadows(new() { Blur = 12, OffsetY = 2, Color = Color.FromArgb(70, 0, 0, 0) })
                : default;
        }

        private void SetMouseThrough(bool value)
        {
            if (_mouseThrough == value) return;
            switch (value)
            {
                case true when !_canRestoreHidden():
                    throw new InvalidOperationException(UiText.Get("PinnedImageRestoreRequired"));
                case true:
                    _opacityBeforeThrough = Opacity;
                    Opacity = Math.Min(Opacity, PreferencesProvider().MouseThroughOpacity);
                    break;
            }

            try
            {
                PinnedWindowPlatform.SetMouseThrough(this, value, ref _inputStyle);
            }
            catch
            {
                if (value && _opacityBeforeThrough is { } previousOpacity)
                {
                    Opacity = previousOpacity;
                    _opacityBeforeThrough = null;
                }

                throw;
            }

            _mouseThrough = value;
            if (!value && _opacityBeforeThrough is { } opacity)
            {
                Opacity = opacity;
                _opacityBeforeThrough = null;
            }

            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        private void RestoreToScreen()
        {
            if (Screens.ScreenFromWindow(this) is not { } screen) return;
            var area = screen.WorkingArea;
            Position = new(Math.Clamp(Position.X, area.X, Math.Max(area.X, area.Right - (int)(Width * RenderScaling))),
                Math.Clamp(Position.Y, area.Y, Math.Max(area.Y, area.Bottom - (int)(Height * RenderScaling))));
        }

        private void FitInitialContent()
        {
            if (Document is PinnedColorDocument) return;
            var area = Screens.Primary?.WorkingArea;
            var maximumWidth = Math.Min(800, area is { } screen ? screen.Width / RenderScaling * 0.8 : 800);
            var maximumHeight = Math.Min(720, area is { } bounds ? bounds.Height / RenderScaling * 0.8 : 720);
            if (_textView is PinnedTextView view)
            {
                var sample = new TextBlock
                {
                    Text = view.Text[..Math.Min(view.Text.Length, 4096)], FontSize = view.FontSize,
                    FontFamily = view.FontFamily, TextWrapping = TextWrapping.Wrap,
                };
                sample.Measure(new(Math.Max(1, maximumWidth - 20), Math.Max(1, maximumHeight - 20)));
                Width = Math.Clamp(sample.DesiredSize.Width + 24, MinWidth, Math.Max(MinWidth, maximumWidth));
                Height = Math.Clamp(sample.DesiredSize.Height + 24, MinHeight, Math.Max(MinHeight, maximumHeight));
            }
            else
            {
                var rich = _textView as PinnedRichView;
                rich?.PrepareForFit();
                _surface.Measure(new(maximumWidth, maximumHeight));
                var desiredWidth = rich is null
                    ? _surface.DesiredSize.Width
                    : _surface.DesiredSize.Width - rich.DesiredSize.Width + rich.PreferredWidth;
                Width = Math.Clamp(desiredWidth, MinWidth, Math.Max(MinWidth, maximumWidth));
                Height = Math.Clamp(_surface.DesiredSize.Height, MinHeight, Math.Max(MinHeight, maximumHeight));
            }
        }
    }
}
