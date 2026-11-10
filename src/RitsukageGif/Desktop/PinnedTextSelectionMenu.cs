using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Primitives.PopupPositioning;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Path = Avalonia.Controls.Shapes.Path;

namespace RitsukageGif.Desktop
{
    internal sealed class PinnedTextSelectionMenu : Popup, IDisposable
    {
        private static readonly FuncControlTemplate<Button> ButtonTemplate = new((owner, _) => new Border
        {
            CornerRadius = new(6),
            [!Border.BackgroundProperty] = owner[!TemplatedControl.BackgroundProperty],
            [!Border.BorderBrushProperty] = owner[!TemplatedControl.BorderBrushProperty],
            [!Border.BorderThicknessProperty] = owner[!TemplatedControl.BorderThicknessProperty],
            [!Decorator.PaddingProperty] = owner[!TemplatedControl.PaddingProperty],
            Child = new ContentPresenter
            {
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
                [!ContentPresenter.ContentProperty] = owner[!ContentControl.ContentProperty],
            },
        });

        private static Color[]? _lastColors;
        private readonly Window _owner;
        private readonly Func<PinnedImagePreferences> _preferences;
        private readonly Func<ImageTextMarkKind, Color, bool> _toggle;
        private readonly Action<ImageTextMarkKind, Color> _recolor;
        private readonly Action<ImageTextMarkKind> _remove;
        private readonly Canvas _canvas = new();
        private readonly Path _arrow = new() { Fill = AppTheme.Surface, Width = 32, Height = 9 };
        private readonly Border _body;
        private readonly Border _palette;
        private readonly StackPanel _swatches = new() { Orientation = Orientation.Horizontal, Spacing = 6 };
        private readonly Dictionary<ImageTextMarkKind, Button> _buttons = [];
        private ImageTextMarkKind? _activeKind;
        private Color? _selectedColor;
        private Size _paletteSize;
        private Rect _target;

        internal PinnedTextSelectionMenu(Window owner, Func<PinnedImagePreferences> preferences,
            Func<ImageTextMarkKind, Color, bool> toggle, Action<ImageTextMarkKind, Color> recolor,
            Action<ImageTextMarkKind> remove, Action copy, Action clearSelection)
        {
            _owner = owner;
            _preferences = preferences;
            _toggle = toggle;
            _recolor = recolor;
            _remove = remove;
            _palette = new()
            {
                Background = AppTheme.Surface, BorderBrush = AppTheme.Outline, BorderThickness = new(1),
                CornerRadius = new(9), Padding = new(11), Child = _swatches, IsVisible = false,
            };
            PlacementTarget = owner;
            Placement = PlacementMode.AnchorAndGravity;
            PlacementAnchor = PopupAnchor.TopLeft;
            PlacementGravity = PopupGravity.BottomRight;
            PlacementConstraintAdjustment = PopupPositionerConstraintAdjustment.None;
            IsLightDismissEnabled = true;
            OverlayDismissEventPassThrough = true;
            var actions = new StackPanel { Orientation = Orientation.Horizontal };
            var copyButton = ActionButton("PinnedTextMenuCopy", "ScreenshotCopy", "ScreenshotCopy");
            copyButton.Click += (_, _) =>
            {
                IsOpen = false;
                copy();
            };
            actions.Children.Add(copyButton);
            AddMark(ImageTextMarkKind.Highlight, "ScreenshotAnnotationHighlighter", "ScreenshotHighlight");
            AddMark(ImageTextMarkKind.WavyUnderline, "PinnedTextWave", "PinnedTextWave");
            AddMark(ImageTextMarkKind.Underline, "PinnedTextUnderline", "PinnedTextUnderline");
            AddMark(ImageTextMarkKind.Strikethrough, "PinnedTextStrike", "PinnedTextStrikethrough");
            var bodyContent = new Grid();
            bodyContent.Children.Add(actions);
            var close = ChromeButton("PinnedTextMenuClose");
            close.Width = close.Height = 18;
            close.Content = ToolbarIcon.Create(close, ToolbarIcon.GetDefinition("Cancel"));
            close.HorizontalAlignment = HorizontalAlignment.Right;
            close.VerticalAlignment = VerticalAlignment.Top;
            close.Margin = new(-4);
            close.Click += (_, _) =>
            {
                IsOpen = false;
                clearSelection();
            };
            close.Bind(ToolTip.TipProperty, LiveText.Resource("Close"));
            bodyContent.Children.Add(close);
            _body = new()
            {
                Background = AppTheme.Surface, BorderBrush = AppTheme.Outline, BorderThickness = new(1),
                CornerRadius = new(9), Padding = new(5), Child = bodyContent,
            };
            _canvas.Children.Add(_arrow);
            _canvas.Children.Add(_body);
            _canvas.Children.Add(_palette);
            Child = _canvas;
            return;

            void AddMark(ImageTextMarkKind kind, string label, string icon)
            {
                var button = ActionButton("PinnedTextMenu" + kind, label, icon);
                _buttons.Add(kind, button);
                actions.Children.Add(button);
                button.Click += (_, _) =>
                {
                    var applied = _toggle(kind, _lastColors![(int)kind]);
                    _activeKind = applied ? kind : null;
                    _selectedColor = applied ? _lastColors[(int)kind] : null;
                    _palette.IsVisible = applied;
                    RefreshButtons();
                    UpdatePlacement();
                };
                button.PointerEntered += (_, _) => RefreshButton(button, _activeKind == kind);
                button.PointerExited += (_, _) => RefreshButton(button, _activeKind == kind);
            }
        }

        public void Dispose()
        {
            IsOpen = false;
            Child = null;
        }

        internal void Open(Rect target)
        {
            var preferences = _preferences().Normalize();
            if (!preferences.ShowTextPopupMenu) return;
            _target = target;
            var colors = preferences.TextHighlightColors.Select(Color.Parse).ToArray();
            _lastColors ??= [colors[0], colors[0], colors[0], colors[0]];
            _activeKind = null;
            _selectedColor = null;
            _swatches.Children.Clear();
            foreach (var color in colors)
            {
                var button = ChromeButton("PinnedTextMenuColor");
                button.Width = button.Height = 22;
                button.Tag = color;
                button.Content = new Border
                {
                    Width = 20, Height = 20, Background = new SolidColorBrush(color), CornerRadius = new(5),
                    BorderBrush = AppTheme.Muted, BorderThickness = new(1),
                };
                ToolTip.SetTip(button, color.ToString());
                button.Click += (_, _) =>
                {
                    if (_activeKind is not { } kind) return;
                    _lastColors[(int)kind] = color;
                    _selectedColor = color;
                    _recolor(kind, color);
                    RefreshButtons();
                };
                _swatches.Children.Add(button);
            }

            var clear = ChromeButton("PinnedTextMenuClearMark");
            clear.Width = clear.Height = 20;
            clear.Content = ToolbarIcon.Create(clear, ToolbarIcon.GetDefinition("Cancel"));
            clear.Bind(ToolTip.TipProperty, LiveText.Resource("AnimationDelete"));
            clear.Click += (_, _) =>
            {
                if (_activeKind is not { } kind) return;
                _remove(kind);
                _selectedColor = null;
                RefreshButtons();
            };
            _swatches.Children.Add(clear);
            _palette.IsVisible = true;
            _palette.Measure(new(double.PositiveInfinity, double.PositiveInfinity));
            _paletteSize = _palette.DesiredSize;
            _palette.IsVisible = false;
            RefreshButtons();
            UpdatePlacement();
            IsOpen = true;
        }

        private void UpdatePlacement()
        {
            _body.Measure(new(double.PositiveInfinity, double.PositiveInfinity));
            var point = _owner.PointToScreen(_target.Center);
            var screen = _owner.Screens.ScreenFromPoint(point) ?? _owner.Screens.Primary;
            var workArea = screen is null
                ? new Rect(_owner.Bounds.Size)
                : new Rect(
                    _owner.PointToClient(screen.WorkingArea.Position),
                    _owner.PointToClient(new(screen.WorkingArea.Right, screen.WorkingArea.Bottom)));
            var layout = TextSelectionMenuLayout.Calculate(_target, workArea, _body.DesiredSize, _paletteSize,
                _palette.IsVisible);
            _canvas.Width = layout.PopupBounds.Width;
            _canvas.Height = layout.PopupBounds.Height;
            Canvas.SetLeft(_body, layout.BodyBounds.X);
            Canvas.SetTop(_body, layout.BodyBounds.Y);
            Canvas.SetLeft(_palette, layout.PaletteBounds.X);
            Canvas.SetTop(_palette, layout.PaletteBounds.Y);
            Canvas.SetLeft(_arrow, layout.ArrowX - 16);
            Canvas.SetTop(_arrow, layout.Above ? layout.BodyBounds.Bottom : 0);
            _arrow.Data = Geometry.Parse(layout.Above ? "M0,0 L32,0 L16,9 Z" : "M0,9 L32,9 L16,0 Z");
            PlacementRect = new(layout.PopupBounds.Position, default(Size));
        }

        private void RefreshButtons()
        {
            foreach (var (kind, button) in _buttons) RefreshButton(button, _activeKind == kind);
            foreach (var button in _swatches.Children.OfType<Button>())
                button.BorderBrush = button.Tag is Color color && _selectedColor == color
                    ? AppTheme.Ink
                    : Brushes.Transparent;
        }

        private static void RefreshButton(Button button, bool selected)
        {
            button.Background = selected || button.IsPointerOver ? AppTheme.SurfaceTint : Brushes.Transparent;
        }

        private static Button ChromeButton(string name)
        {
            var button = new Button
            {
                Name = name, MinWidth = 0, MinHeight = 0, Padding = new(0), Focusable = false,
                Background = Brushes.Transparent, BorderBrush = Brushes.Transparent, BorderThickness = new(1),
                Foreground = AppTheme.Ink, Template = ButtonTemplate,
            };
            button.PointerEntered += (_, _) =>
            {
                if (button.Tag is not Color) RefreshButton(button, false);
            };
            button.PointerExited += (_, _) =>
            {
                if (button.Tag is not Color) RefreshButton(button, false);
            };
            return button;
        }

        private static Button ActionButton(string name, string label, string icon)
        {
            var button = ChromeButton(name);
            button.MinWidth = 62;
            button.Height = 62;
            button.Padding = new(6, 2);
            var content = new StackPanel { Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
            var glyph = ToolbarIcon.Create(button,
                ToolbarIcon.GetDefinition(icon == "PinnedTextWave" ? "PinnedTextWaveWithUnderline" : icon));
            glyph.Width = glyph.Height = 20;
            glyph.HorizontalAlignment = HorizontalAlignment.Center;
            content.Children.Add(glyph);
            var caption = new TextBlock { FontSize = 12, TextAlignment = TextAlignment.Center };
            caption.Bind(TextBlock.TextProperty, LiveText.Resource(label));
            content.Children.Add(caption);
            button.Content = content;
            return button;
        }
    }
}
