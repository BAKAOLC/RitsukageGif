using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;

namespace RitsukageGif.Desktop
{
    // ReSharper disable once ClassCannotBeInstantiated
    internal sealed partial class PinnedImageWindow
    {
        private bool _shadowTracking;
        private PinnedImageShadow? _windowShadow;

        private void RefreshWindowShadow()
        {
            if (!_shadowTracking)
            {
                _shadowTracking = true;
                PositionChanged += ShadowOwnerChanged;
                SizeChanged += ShadowOwnerChanged;
                ScalingChanged += ShadowOwnerChanged;
                PropertyChanged += ShadowPropertyChanged;
                Closed += ShadowOwnerClosed;
            }

            var color = IsMouseThrough ? AppTheme.CurrentAppearance.ClickThroughShadeColor
                : CurrentPreferences.ShowWindowShadow ? 0x55000000u : 0u;
            if (_closed || !IsVisible || WindowState == WindowState.Minimized || color >> 24 == 0)
            {
                _windowShadow?.Dispose();
                _windowShadow = null;
                return;
            }

            try
            {
                _windowShadow ??= new(this);
                _windowShadow.Refresh(color);
            }
            catch (Exception error)
            {
                _windowShadow?.Dispose();
                _windowShadow = null;
                Trace.TraceWarning("Window shadow could not be displayed: {0}", error.Message);
            }
        }

        private void ShadowOwnerChanged(object? sender, EventArgs args)
        {
            RefreshWindowShadow();
        }

        private void ShadowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
        {
            if (args.Property == IsVisibleProperty || args.Property == WindowStateProperty ||
                args.Property == TopmostProperty || args.Property == OpacityProperty)
                RefreshWindowShadow();
        }

        private void ShadowOwnerClosed(object? sender, EventArgs args)
        {
            _windowShadow?.Dispose();
            _windowShadow = null;
            PositionChanged -= ShadowOwnerChanged;
            SizeChanged -= ShadowOwnerChanged;
            ScalingChanged -= ShadowOwnerChanged;
            PropertyChanged -= ShadowPropertyChanged;
            Closed -= ShadowOwnerClosed;
        }
    }
}
