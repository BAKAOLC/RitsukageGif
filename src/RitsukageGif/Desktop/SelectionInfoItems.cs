namespace RitsukageGif.Desktop
{
    [Flags]
    public enum SelectionInfoItems
    {
        None = 0,
        Coordinates = 1,
        Size = 2,
        Unit = 4,
        All = Coordinates | Size | Unit,
    }
}
