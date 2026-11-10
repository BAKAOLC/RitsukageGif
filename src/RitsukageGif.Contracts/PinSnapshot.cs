namespace RitsukageGif.Contracts
{
    public sealed record PinSnapshot(ReadOnlyMemory<byte> Image, ReadOnlyMemory<byte>? Hdr, OutputColorMode Color);
}
