namespace RitsukageGif.Desktop
{
    [Flags]
    public enum CaptureWheelFunctions
    {
        None = 0,
        CandidateHierarchy = 1,
        ResizeSelection = 2,
        All = CandidateHierarchy | ResizeSelection,
    }
}
