namespace RitsukageGif.Scripting
{
    public sealed class ScriptValueSerializationException(string code, string message) : Exception(message)
    {
        public string Code { get; } = code;
    }
}
