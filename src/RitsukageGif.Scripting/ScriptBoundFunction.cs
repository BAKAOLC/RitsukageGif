namespace RitsukageGif.Scripting
{
    internal sealed record ScriptBoundFunction(ScriptCallableDescriptor Descriptor, ScriptNative Callback)
        : IScriptCallable;
}
