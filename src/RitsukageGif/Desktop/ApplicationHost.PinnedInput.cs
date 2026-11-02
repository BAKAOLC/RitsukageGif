namespace RitsukageGif.Desktop
{
    public sealed partial class ApplicationHost
    {
        private async Task ExecutePinnedKeyboardActionAsync(GlobalActionBinding action)
        {
            using var input = PinnedInputContext.Use(PinnedInputContext.Current | PinnedInputOrigin.Keyboard);
            await ExecuteActionAsync(action);
        }
    }
}
