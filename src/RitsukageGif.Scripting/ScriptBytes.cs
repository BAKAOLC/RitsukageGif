namespace RitsukageGif.Scripting
{
    public sealed class ScriptBytes
    {
        internal ScriptBytes(ReadOnlyMemory<byte> data) => Data = data;
        internal ReadOnlyMemory<byte> Data { get; }
        public int Length => Data.Length;
    }
}
