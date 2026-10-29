using System.Globalization;
using System.Text;

namespace RitsukageGif.Scripting
{
    public static partial class FileScriptLibrary
    {
        private const int MaximumTextBytes = 16 * 1024 * 1024;

        public static void Register(ScriptBindingRegistry bindings, string? baseDirectory = null)
        {
            var root = Path.GetFullPath(baseDirectory ?? Environment.CurrentDirectory);

            ScriptServiceErrors.Register(bindings);
            bindings.RegisterObjectSchema("FileInfo",
            [
                new("path", "string"), new("name", "string"), new("extension", "string"),
                new("size", "number"), new("isDirectory", "boolean"), new("modifiedUtc", "string"),
            ]);
            var files = bindings.RegisterTable("files");
            ScriptParameter[] pathParameter = [new("path", "string")];
            files.Function("info", Wrap(args => Info(Resolve((string)args[0]!))), pathParameter,
                "Result<FileInfo,ServiceError>");
            files.Function("exists", Wrap(args => Exists(Resolve((string)args[0]!))), pathParameter,
                "Result<boolean,ServiceError>");
            files.Function("readText", async (args, token) =>
                {
                    var path = (string)args[0]!;
                    try
                    {
                        token.ThrowIfCancellationRequested();
                        path = Resolve(path);
                        var limit = (double)args[1]!;
                        if (!double.IsFinite(limit) || limit < 1 || limit > MaximumTextBytes ||
                            !limit.Equals(Math.Truncate(limit)))
                            throw new ArgumentOutOfRangeException(nameof(args));
                        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                            8192, FileOptions.Asynchronous | FileOptions.SequentialScan);
                        if (stream.Length > limit) return TooLarge(path);
                        using var bytes = new MemoryStream();
                        var buffer = new byte[8192];
                        while (true)
                        {
                            var count = await stream.ReadAsync(buffer, token);
                            if (count == 0) break;
                            if (bytes.Length + count > limit) return TooLarge(path);
                            bytes.Write(buffer, 0, count);
                        }

                        bytes.Position = 0;
                        using var reader = new StreamReader(bytes, new UTF8Encoding(false, true), true);
                        return new ScriptResult(true, await reader.ReadToEndAsync(token));
                    }
                    catch (Exception exception) when (IsFileError(exception))
                    {
                        token.ThrowIfCancellationRequested();
                        return new ScriptResult(false, ScriptServiceErrors.FromException(exception, path));
                    }
                }, [.. pathParameter, new("maxBytes", "number", true, 1024d * 1024)],
                "Result<string,ServiceError>");
            files.Function("writeText", async (args, token) =>
                {
                    var path = (string)args[0]!;
                    try
                    {
                        token.ThrowIfCancellationRequested();
                        path = Resolve(path);
                        var text = (string)args[1]!;
                        var encoding = new UTF8Encoding(false, true);
                        if (encoding.GetByteCount(text) > MaximumTextBytes) return TooLarge(path);
                        var data = encoding.GetBytes(text);
                        await WriteAtomically(path, (bool)args[2]!,
                            (stream, cancellation) => stream.WriteAsync(data, cancellation).AsTask(), token);
                        return new ScriptResult(true, path);
                    }
                    catch (Exception exception) when (IsFileError(exception))
                    {
                        token.ThrowIfCancellationRequested();
                        return new ScriptResult(false, ScriptServiceErrors.FromException(exception, path));
                    }
                }, [.. pathParameter, new("text", "string"), new("overwrite", "boolean", true, false)],
                "Result<string,ServiceError>");
            files.Function("copy", async (args, token) =>
            {
                var path = (string)args[0]!;
                try
                {
                    token.ThrowIfCancellationRequested();
                    path = Resolve(path);
                    var destination = Resolve((string)args[1]!);
                    if (string.Equals(path, destination, OperatingSystem.IsWindows()
                            ? StringComparison.OrdinalIgnoreCase
                            : StringComparison.Ordinal))
                        throw new ArgumentException(nameof(destination));
                    await using var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                        81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
                    await WriteAtomically(destination, (bool)args[2]!,
                        source.CopyToAsync, token);
                    return new ScriptResult(true, destination);
                }
                catch (Exception exception) when (IsFileError(exception))
                {
                    token.ThrowIfCancellationRequested();
                    return new ScriptResult(false, ScriptServiceErrors.FromException(exception, path));
                }
            }, [
                new("source", "string"), new("destination", "string"),
                new("overwrite", "boolean", true, false),
            ], "Result<string,ServiceError>");
            files.Function("move", Wrap(args =>
            {
                var source = Resolve((string)args[0]!);
                var destination = Resolve((string)args[1]!);
                File.Move(source, destination, (bool)args[2]!);
                return destination;
            }), [
                new("source", "string"), new("destination", "string"),
                new("overwrite", "boolean", true, false),
            ], "Result<string,ServiceError>");
            files.Function("createDirectory",
                Wrap(args => Directory.CreateDirectory(Resolve((string)args[0]!)).FullName),
                pathParameter, "Result<string,ServiceError>");
            files.Function("list", (args, token) =>
            {
                var path = (string)args[0]!;
                try
                {
                    token.ThrowIfCancellationRequested();
                    path = Resolve(path);
                    var entries = new List<object?>();
                    foreach (var entry in Directory.EnumerateFileSystemEntries(path))
                    {
                        token.ThrowIfCancellationRequested();
                        if (entries.Count == 10000) return ValueTask.FromResult<object?>(TooLarge(path));
                        entries.Add(Info(entry));
                    }

                    return ValueTask.FromResult<object?>(new ScriptResult(true, entries));
                }
                catch (Exception exception) when (IsFileError(exception))
                {
                    token.ThrowIfCancellationRequested();
                    return ValueTask.FromResult<object?>(new ScriptResult(false,
                        ScriptServiceErrors.FromException(exception, path)));
                }
            }, pathParameter, "Result<FileInfo[],ServiceError>");
            RegisterPaths(bindings, Resolve);
            RegisterBytes(files, Resolve);
            return;

            string Resolve(string path)
            {
                return Path.GetFullPath(path, root);
            }
        }

        private static bool IsFileError(Exception exception)
        {
            return exception is IOException or UnauthorizedAccessException or ArgumentException
                or NotSupportedException;
        }

        private static ScriptResult TooLarge(string path)
        {
            return new(false,
                ScriptServiceErrors.Create("limitExceeded", UiText.Get("ServiceLimitExceeded"), path));
        }

        private static ScriptNative Wrap(Func<IReadOnlyList<object?>, object?> action)
        {
            return (args, token) =>
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    var value = action(args);
                    token.ThrowIfCancellationRequested();
                    return ValueTask.FromResult<object?>(new ScriptResult(true, value));
                }
                catch (Exception exception) when (IsFileError(exception))
                {
                    token.ThrowIfCancellationRequested();
                    return ValueTask.FromResult<object?>(new ScriptResult(false,
                        ScriptServiceErrors.FromException(exception, args.Count == 0 ? null : args[0] as string)));
                }
            };
        }

        private static bool Exists(string path)
        {
            try
            {
                _ = File.GetAttributes(path);
                return true;
            }
            catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
            {
                return false;
            }
        }

        private static Dictionary<string, object?> Info(string path)
        {
            var isDirectory = (File.GetAttributes(path) & FileAttributes.Directory) != 0;
            FileSystemInfo info = isDirectory ? new DirectoryInfo(path) : new FileInfo(path);
            return new()
            {
                ["path"] = info.FullName, ["name"] = info.Name, ["extension"] = info.Extension,
                ["size"] = info is FileInfo file ? file.Length : 0d,
                ["isDirectory"] = isDirectory,
                ["modifiedUtc"] = info.LastWriteTimeUtc.ToString("O", CultureInfo.InvariantCulture),
            };
        }
    }
}
