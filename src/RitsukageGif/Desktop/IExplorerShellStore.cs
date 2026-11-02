namespace RitsukageGif.Desktop
{
    internal interface IExplorerShellStore
    {
        ExplorerShellEntry? Read(string path);
        void Write(string path, ExplorerShellEntry entry);
        void Remove(string path);
    }
}
