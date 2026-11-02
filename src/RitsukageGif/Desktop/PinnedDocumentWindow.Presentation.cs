namespace RitsukageGif.Desktop
{
    internal sealed partial class PinnedDocumentWindow
    {
        private async Task EditGroupAsync()
        {
            if (NewGroupRequested is not null) await NewGroupRequested(this);
        }
    }
}
