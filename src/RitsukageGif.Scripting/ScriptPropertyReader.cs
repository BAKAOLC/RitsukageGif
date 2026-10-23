namespace RitsukageGif.Scripting
{
    public delegate bool ScriptPropertyReader<in T>(T instance, out object? value);
}
