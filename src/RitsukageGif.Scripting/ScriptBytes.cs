namespace RitsukageGif.Scripting
{
    public sealed class ScriptBytes(ReadOnlyMemory<byte> data)
    {
        public ReadOnlyMemory<byte> Data { get; } = data;
        public int Length => Data.Length;
    }
}
