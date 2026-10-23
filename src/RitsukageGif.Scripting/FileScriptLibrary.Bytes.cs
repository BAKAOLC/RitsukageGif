namespace RitsukageGif.Scripting
{
    public static partial class FileScriptLibrary
    {
        private static void RegisterBytes(ScriptTableBinding files, Func<string, string> resolve)
        {
            files.Function("readBytes", async (args, token) =>
            {
                var path = (string)args[0]!;
                try
                {
                    path = resolve(path);
                    var limit = (double)args[1]!;
                    // Byte limits must be exact integers, without tolerance or rounding.
                    // ReSharper disable once CompareOfFloatsByEqualityOperator
                    if (!double.IsFinite(limit) || limit < 1 || limit > DataScriptLibrary.MaximumBytes ||
                        limit != Math.Truncate(limit))
                        throw new ArgumentOutOfRangeException(nameof(args));
                    await using var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920,
                        FileOptions.Asynchronous | FileOptions.SequentialScan);
                    if (source.Length > limit) return TooLarge(path);
                    using var destination = new MemoryStream();
                    var buffer = new byte[81920];
                    while (true)
                    {
                        var count = await source.ReadAsync(buffer, token);
                        if (count == 0) break;
                        if (destination.Length + count > limit) return TooLarge(path);
                        destination.Write(buffer, 0, count);
                    }

                    return new ScriptResult(true,
                        new ScriptBytes(destination.GetBuffer().AsMemory(0, (int)destination.Length)));
                }
                catch (Exception exception) when (IsFileError(exception))
                {
                    token.ThrowIfCancellationRequested();
                    return new ScriptResult(false, ScriptServiceErrors.FromException(exception, path));
                }
            }, [new("path", "string"), new("maxBytes", "number", true, 1024d * 1024)], "Result<Bytes,ServiceError>");
            files.Function("writeBytes", async (args, token) =>
                {
                    var path = (string)args[0]!;
                    try
                    {
                        path = resolve(path);
                        var bytes = (ScriptBytes)args[1]!;
                        await WriteAtomically(path, (bool)args[2]!,
                            (stream, cancellation) => stream.WriteAsync(bytes.Data, cancellation).AsTask(), token);
                        return new ScriptResult(true, path);
                    }
                    catch (Exception exception) when (IsFileError(exception))
                    {
                        token.ThrowIfCancellationRequested();
                        return new ScriptResult(false, ScriptServiceErrors.FromException(exception, path));
                    }
                }, [new("path", "string"), new("data", "Bytes"), new("overwrite", "boolean", true, false)],
                "Result<string,ServiceError>");
        }
    }
}
