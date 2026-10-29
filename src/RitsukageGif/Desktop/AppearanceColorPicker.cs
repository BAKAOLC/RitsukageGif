using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace RitsukageGif.Desktop
{
    internal sealed class AppearanceColorPicker : ColorPicker
    {
        private bool _choosing;

        public AppearanceColorPicker()
        {
            AddHandler(PointerPressedEvent, OpenSystemPicker, RoutingStrategies.Tunnel);
            AddHandler(KeyDownEvent, OpenSystemPicker, RoutingStrategies.Tunnel);
        }

        protected override Type StyleKeyOverride => typeof(ColorPicker);
        internal Func<AppearanceDialogStyle>? DialogStyleProvider { get; init; }
        internal Func<IDisposable>? DialogScopeProvider { get; set; }

        private void OpenSystemPicker(object? sender, PointerPressedEventArgs args)
        {
            if (!args.GetCurrentPoint(this).Properties.IsLeftButtonPressed || !UseSystemPicker()) return;
            args.Handled = true;
            ChooseSystemColor();
        }

        private void OpenSystemPicker(object? sender, KeyEventArgs args)
        {
            if (args.Key is not (Key.Enter or Key.Space or Key.Down) || !UseSystemPicker()) return;
            args.Handled = true;
            ChooseSystemColor();
        }

        private bool UseSystemPicker()
        {
            return OperatingSystem.IsWindows() &&
                   (DialogStyleProvider?.Invoke() ?? AppTheme.CurrentAppearance.ColorDialogStyle) ==
                   AppearanceDialogStyle.System;
        }

        private void ChooseSystemColor()
        {
            if (_choosing) return;
            _choosing = true;
            try
            {
                using var scope = DialogScopeProvider?.Invoke();
                var owner = TopLevel.GetTopLevel(this)?.TryGetPlatformHandle()?.Handle ?? 0;
                if (WindowsColorDialog.Choose(owner, Color, IsAlphaEnabled) is { } color) Color = color;
            }
            finally
            {
                _choosing = false;
            }
        }
    }
}
