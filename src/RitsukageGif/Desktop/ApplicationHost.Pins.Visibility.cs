namespace RitsukageGif.Desktop
{
    public sealed partial class ApplicationHost
    {
        private Task ToggleAllPinnedVisibilityAsync()
        {
            _pinnedImages.ToggleAllPinnedVisibility();
            return Task.CompletedTask;
        }
    }
}
