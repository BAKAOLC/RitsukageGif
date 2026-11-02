namespace RitsukageGif.Contracts
{
    public readonly record struct SampleColorContext(
        byte Red,
        byte Green,
        byte Blue,
        byte Alpha,
        PixelPoint Pointer,
        PixelRect Selection);
}
