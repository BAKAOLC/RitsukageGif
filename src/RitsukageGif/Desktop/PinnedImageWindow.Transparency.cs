using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Styling;
using ImageMagick;

namespace RitsukageGif.Desktop
{
    // ReSharper disable once ClassCannotBeInstantiated
    internal sealed partial class PinnedImageWindow
    {
        private readonly PinnedImageTransparencyBackground _transparentBackground = new() { IsHitTestVisible = false };
        private PinnedTransparentLayerMode? _transparentLayer;
        private byte[]? _transparentSource;

        private bool CanChooseTransparentLayer
        {
            get
            {
                if (!CanUseInlineTranslation || Presentation.HasRadiusMetadata) return false;
                if (!ReferenceEquals(_transparentSource, EffectivePng))
                {
                    _transparentSource = EffectivePng;
                    using var image = new MagickImage();
                    image.Ping(_transparentSource);
                    field = image.HasAlpha;
                }

                return field;
            }
        }

        private void RefreshTransparentBackground()
        {
            _content.Background = CanUseInlineTranslation ? Brushes.Transparent : AppTheme.Workspace;
            var mode = CanChooseTransparentLayer
                ? _transparentLayer ?? CurrentPreferences.TransparentLayer
                : PinnedTransparentLayerMode.IgnoreMouse;
            var region = DisplayRegion;
            var scaleX = _transparentBackground.Bounds.Width > 0
                ? _transparentBackground.Bounds.Width / region.Width
                : CurrentPinZoom / RenderScaling;
            var scaleY = _transparentBackground.Bounds.Height > 0
                ? _transparentBackground.Bounds.Height / region.Height
                : CurrentPinZoom / RenderScaling;
            _transparentBackground.Update(new(mode,
                (int)(RenderScaling * 12) / RenderScaling,
                new(region.X * scaleX, region.Y * scaleY),
                ActualThemeVariant == ThemeVariant.Dark));
        }

        private PinnedHdrPreviewBackground SnapshotHdrPreviewBackground(CapturedFrame source)
        {
            var mode = CanChooseTransparentLayer
                ? _transparentLayer ?? CurrentPreferences.TransparentLayer
                : PinnedTransparentLayerMode.IgnoreMouse;
            var white = OperatingSystem.IsMacOS()
                ? source.ColorInfo.SdrWhiteNits / FrameColorInfo.ScRgbReferenceWhiteNits
                : _hdrPreview?.CurrentDisplayWhiteLevel ?? 1;
            return new(mode, DisplayRegion, (int)(RenderScaling * 12),
                ActualThemeVariant == ThemeVariant.Dark, white);
        }

        private MenuItem CreateTransparentLayerMenu(ContextMenu menu)
        {
            var parent = new MenuItem
            {
                Tag = "transparent-layer",
                [!HeaderedSelectingItemsControl.HeaderProperty] = LiveText.Resource("PinnedTransparentLayer"),
            };
            foreach (var (mode, label) in new[]
                     {
                         (PinnedTransparentLayerMode.IgnoreMouse, "PinnedTransparentIgnoreMouse"),
                         (PinnedTransparentLayerMode.AcceptingMouse, "PinnedTransparentAcceptingMouse"),
                         (PinnedTransparentLayerMode.Checkerboard, "PinnedTransparentCheckerboard"),
                     })
            {
                var item = new MenuItem
                {
                    Tag = mode, ToggleType = MenuItemToggleType.Radio,
                    [!HeaderedSelectingItemsControl.HeaderProperty] = LiveText.Resource(label),
                };
                item.Click += (_, _) =>
                {
                    _transparentLayer = mode;
                    RefreshTransparentBackground();
                    RefreshHdrPreview();
                    StateChanged?.Invoke(this, EventArgs.Empty);
                };
                ToolTip.SetShowOnDisabled(item, true);
                parent.Items.Add(item);
            }

            menu.Opened += (_, _) =>
            {
                parent.IsVisible = CanChooseTransparentLayer;
                foreach (var item in parent.Items.OfType<MenuItem>())
                {
                    item.IsChecked = item.Tag is PinnedTransparentLayerMode selected &&
                                     selected == (_transparentLayer ?? CurrentPreferences.TransparentLayer);
                    item.IsEnabled = item.Tag is not PinnedTransparentLayerMode.IgnoreMouse ||
                                     CanIgnoreTransparentInput;
                    ToolTip.SetTip(item, item.IsEnabled ? null : UiText.Get("PinnedTransparentIgnoreMouseUnavailable"));
                }

                RefreshTransparentBackground();
            };
            return parent;
        }
    }
}
