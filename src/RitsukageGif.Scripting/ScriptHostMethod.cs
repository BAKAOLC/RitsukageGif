namespace RitsukageGif.Scripting
{
    public delegate ValueTask<object?> ScriptHostMethod<in T>(T instance, IReadOnlyList<object?> arguments,
        CancellationToken cancellation);
}
