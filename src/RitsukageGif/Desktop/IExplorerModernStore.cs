namespace RitsukageGif.Desktop
{
    internal interface IExplorerModernStore
    {
        ExplorerModernEntry? Read();
        void Write(ExplorerModernEntry? entry);
    }
}
