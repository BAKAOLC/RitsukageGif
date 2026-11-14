using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace RitsukageGif.Desktop
{
    internal sealed class AppearanceColorPicker : ColorPicker
    {
        private bool _choosing;
        private DropDownButton? _button;

        protected override Type StyleKeyOverride => typeof(ColorPicker);
        internal Func<AppearanceDialogStyle>? DialogStyleProvider { get; init; }
        internal Func<IDisposable>? DialogScopeProvider { get; set; }
        internal Action<Color>? PreviewColor { get; init; }

        protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
        {
            if (_button is not null) _button.Click -= OpenDialog;
            base.OnApplyTemplate(e);
            _button = this.GetVisualDescendants().OfType<DropDownButton>()
                .FirstOrDefault();
            if (_button is null) return;
            _button.Flyout = null;
            _button.Click += OpenDialog;
        }

        private void OpenDialog(object? sender, RoutedEventArgs args)
        {
            _ = OpenDialogAsync();
        }

        private async Task OpenDialogAsync()
        {
            if (_choosing) return;
            var topLevel = TopLevel.GetTopLevel(this);
            while (topLevel is PopupRoot popup) topLevel = popup.ParentTopLevel;
            if (topLevel is not Window owner) return;
            _choosing = true;
            var original = Color;
            try
            {
                using var scope = DialogScopeProvider?.Invoke();
                var style = DialogStyleProvider?.Invoke() ?? AppTheme.CurrentAppearance.ColorDialogStyle;
                var dialog = new AppearanceColorDialog(style, original, IsAlphaEnabled, PreviewColor);
                if (style == AppearanceDialogStyle.Modern && PinnedGroupPointer.IsSupported)
                    dialog.PreparePosition(PinnedGroupPointer.Read());
                if (await dialog.ShowDialog<Color?>(owner) is { } color) Color = color;
                else PreviewColor?.Invoke(original);
            }
            catch (Exception error)
            {
                PreviewColor?.Invoke(original);
                LogHub.GetLogger("Desktop.ColorPicker").ZLogWarning(error, $"The color picker could not be opened.");
            }
            finally
            {
                _choosing = false;
            }
        }
    }
}
