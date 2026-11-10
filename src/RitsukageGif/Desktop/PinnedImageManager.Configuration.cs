namespace RitsukageGif.Desktop
{
    internal sealed partial class PinnedImageManager
    {
        internal Func<bool> TrayVisibleProvider { get; set; } = () => false;
        internal Func<Task>? OpenConfigurationRequested { get; set; }
    }
}
