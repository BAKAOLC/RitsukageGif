using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Threading;

namespace RitsukageGif.Desktop
{
    internal sealed partial class PinnedWindowPresentation
    {
        internal const double ShadeInset = 15;

        private readonly PinnedWindowShade _shade = new()
        {
            Margin = new(ShadeInset), IsHitTestVisible = false,
        };
        private Func<PinnedImagePreferences> _shadePreferences = null!;

        private readonly PinnedWindowShadeAnimator _shadeAnimator = new();
        private readonly DispatcherTimer _shadeTimer = new() { Interval = TimeSpan.FromMilliseconds(33) };
        private uint _shadeActiveColor;
        private bool _shadeLocked;
        private bool _shadeThrough;
        private bool _shadeThumbnail;

        internal Grid Frame { get; private set; } = null!;
        private bool WindowShadow { get; set; }
        internal CaptureWindowShape? WindowShape => _shade.WindowShape;
        internal bool HasRadiusMetadata => WindowShape is not null;
        internal event Action? WindowShadowChanged;

        internal void SetWindowShape(CaptureWindowShape? shape)
        {
            if (shape is { IsValid: false }) throw new ArgumentOutOfRangeException(nameof(shape));
            _shade.WindowShape = shape;
        }

        internal void SetShapeSourceSize(Size sourceSize)
        {
            _shade.SourceLogicalSize = sourceSize;
        }

        private void InitializeShade(Grid content, Func<PinnedImagePreferences> preferences)
        {
            _shadePreferences = preferences;
            WindowShadow = preferences().ShowWindowShadow;
            _shadeActiveColor = AppTheme.CurrentAppearance.AccentColor;
            content.Background = _owner.Background;
            content.Margin = new(ShadeInset);
            Frame = new() { Children = { _shade, content } };
            _owner.Background = Brushes.Transparent;
            _owner.TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
            _shadeTimer.Tick += (_, _) =>
            {
                if (_shadeAnimator.Advance())
                {
                    _shadeTimer.Stop();
                    if (WindowShadow) return;
                }

                RefreshShadeBrush();
            };
            _owner.Opened += (_, _) =>
            {
                if (!_shadeAnimator.Complete) _shadeTimer.Start();
            };
            _owner.Closed += (_, _) => _shadeTimer.Stop();
            _owner.Activated += (_, _) => RefreshShadeBrush();
            _owner.Deactivated += (_, _) => RefreshShadeBrush();
            RefreshShadeBrush();
        }

        private void ReinitializeShadeAfterDataLoad(bool storedEnabled = true)
        {
            WindowShadow = _shadePreferences().ShowWindowShadow && storedEnabled;
            _shadeAnimator.Reset();
            if (_owner.IsVisible) _shadeTimer.Start();
            RefreshShadeBrush();
        }

        private void SetWindowShadow(bool enabled)
        {
            if (WindowShadow == enabled) return;
            WindowShadow = enabled;
            RefreshShadeBrush();
            WindowShadowChanged?.Invoke();
        }

        internal void RefreshShade(bool locked, bool through, bool thumbnail, string group,
            IReadOnlyList<PinnedGroupDefinition> groups)
        {
            _shadeLocked = locked;
            _shadeThrough = through;
            _shadeThumbnail = thumbnail;
            var definition = groups.FirstOrDefault(item => item.Id == group);
            _shadeActiveColor = Color.TryParse(definition?.Color, out var color)
                ? color.ToUInt32()
                : AppTheme.CurrentAppearance.AccentColor;
            RefreshShadeBrush();
        }

        private void RefreshShadeBrush()
        {
            var appearance = AppTheme.CurrentAppearance;
            var color = _shadeThrough ? appearance.ClickThroughShadeColor
                : !_owner.IsActive ? _shadeThumbnail ? 0xFF969696u : 0xFF646464u
                : _shadeLocked ? appearance.LockedWindowColor : _shadeActiveColor;
            _shade.Shadow = _shadeAnimator.GetColor(color, WindowShadow) is { } animatedColor
                ? new BoxShadows(new() { Blur = ShadeInset, Spread = 2, Color = animatedColor })
                : default;
        }

        internal MenuItem CreateWindowShadeMenu(ContextMenu menu)
        {
            var item = new MenuItem
            {
                Tag = "window-shade", ToggleType = MenuItemToggleType.CheckBox,
                [!HeaderedSelectingItemsControl.HeaderProperty] = LiveText.Resource("PinnedMenuWindowShade"),
            };
            item.Click += (_, _) => SetWindowShadow(item.IsChecked);
            menu.Opened += (_, _) => item.IsChecked = WindowShadow;
            return item;
        }

        internal Avalonia.PixelPoint ContentOriginPosition(PixelPoint contentOrigin)
        {
            var inset = (int)Math.Round(ShadeInset * _owner.RenderScaling);
            return new(contentOrigin.X - inset, contentOrigin.Y - inset);
        }
    }
}
