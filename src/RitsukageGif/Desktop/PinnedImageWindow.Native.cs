namespace RitsukageGif.Desktop
{
    // ReSharper disable once ClassCannotBeInstantiated
    internal sealed partial class PinnedImageWindow
    {
        private nint _inputStyle;

        private void ApplyMouseThrough(bool enabled)
        {
            PinnedWindowPlatform.SetMouseThrough(this, enabled, ref _inputStyle);
            InvalidateInputUi();
        }
    }
}
