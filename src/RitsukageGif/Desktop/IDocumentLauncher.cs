namespace RitsukageGif.Desktop
{
    internal interface IDocumentLauncher
    {
        Task OpenAsync(string path, CancellationToken token);
    }
}
