namespace RitsukageGif.Scripting
{
    public delegate ValueTask<object?> ScriptNative(IReadOnlyList<object?> arguments, CancellationToken cancellation);
}
