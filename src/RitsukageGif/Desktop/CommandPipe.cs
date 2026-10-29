using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace RitsukageGif.Desktop
{
    internal sealed partial class CommandPipe : IDisposable
    {
        private static readonly ILogger PipeLogger = LogHub.GetLogger("Desktop.IPC");
        private readonly CancellationTokenSource _cancel = new();
        private readonly ConcurrentDictionary<Task, byte> _clients = new();
        private readonly Task _server;

        public CommandPipe(Func<string[], Task<CommandReply>> execute, string? name = null)
        {
            _server = ServeAsync(execute, name ?? Name, _cancel.Token);
        }

        private static string Name { get; } = CreateName();

        internal static string MutexName => (OperatingSystem.IsWindows() ? "Local\\" : "") + Name;

        public void Dispose()
        {
            _cancel.Cancel();
            // All accept/read operations observe cancellation; no UI-thread blocking join.
            _ = Task.WhenAll(_clients.Keys.Append(_server)).ContinueWith(_ => _cancel.Dispose(), TaskScheduler.Default);
        }

        private static string CreateName()
        {
            var identity = Environment.UserName + ":" + (OperatingSystem.IsWindows()
                ? Process.GetCurrentProcess().SessionId.ToString()
                : Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR") ??
                  Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
            if (ApplicationStorage.IsCustomDirectory)
            {
                var directory = Path.TrimEndingDirectorySeparator(ApplicationStorage.DataDirectory);
                if (OperatingSystem.IsWindows()) directory = directory.ToUpperInvariant();
                identity += ":" + directory;
            }

            return "RitsukageGif-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))[..24];
        }

        private async Task ServeAsync(Func<string[], Task<CommandReply>> execute, string name, CancellationToken token)
        {
            using var slots = new SemaphoreSlim(8);
            try
            {
                while (!token.IsCancellationRequested)
                {
                    await slots.WaitAsync(token);
                    var pipe = new NamedPipeServerStream(name, PipeDirection.InOut, 16, PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                    try
                    {
                        await pipe.WaitForConnectionAsync(token);
                    }
                    catch
                    {
                        await pipe.DisposeAsync();
                        throw;
                    }

                    var task = HandleLimitedAsync(pipe, execute, slots, token);
                    _clients.TryAdd(task, 0);
                    _ = task.ContinueWith(completed => _clients.TryRemove(completed, out _), CancellationToken.None,
                        TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
            }
            finally
            {
                await Task.WhenAll(_clients.Keys);
            }
        }

        private static async Task HandleLimitedAsync(NamedPipeServerStream pipe,
            Func<string[], Task<CommandReply>> execute,
            SemaphoreSlim slots, CancellationToken token)
        {
            try
            {
                await HandleAsync(pipe, execute, token);
            }
            finally
            {
                slots.Release();
            }
        }

        private static async Task HandleAsync(NamedPipeServerStream pipe, Func<string[], Task<CommandReply>> execute,
            CancellationToken token)
        {
            await using (pipe)
                try
                {
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                    timeout.CancelAfter(TimeSpan.FromSeconds(5));
                    var data = await ReadAsync(pipe, timeout.Token);
                    var command = JsonSerializer.Deserialize(data, CommandJson.Default.StringArray) ?? [];
                    var reply = await execute(command);
                    await WriteAsync(pipe, JsonSerializer.SerializeToUtf8Bytes(reply, CommandJson.Default.CommandReply),
                        token);
                }
                catch (Exception ex) when (ex is IOException or OperationCanceledException or JsonException)
                {
                    PipeLogger.ZLogDebug(ex,
                        $"IPC client disconnected or supplied an invalid request. ServerStopping={token.IsCancellationRequested}");
                }
        }

        public static async Task<CommandReply> SendAsync(string[] args, CancellationToken token, string? name = null)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            token = timeout.Token;
            await using var pipe = new NamedPipeClientStream(".", name ?? Name, PipeDirection.InOut,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            await pipe.ConnectAsync(5000, token);
            if (OperatingSystem.IsWindows() && args is ["show"])
                AllowServerActivation(pipe.SafePipeHandle);
            await WriteAsync(pipe, JsonSerializer.SerializeToUtf8Bytes(args, CommandJson.Default.StringArray), token);
            return JsonSerializer.Deserialize(await ReadAsync(pipe, token, 1024 * 1024),
                CommandJson.Default.CommandReply)!;
        }

        private static async Task<byte[]> ReadAsync(Stream stream, CancellationToken token, int maximum = 65536)
        {
            var header = new byte[4];
            await stream.ReadExactlyAsync(header, token);
            var length = BinaryPrimitives.ReadInt32LittleEndian(header);
            if (length < 0 || length > maximum) throw new IOException();
            var data = new byte[length];
            await stream.ReadExactlyAsync(data, token);
            return data;
        }

        private static async Task WriteAsync(Stream stream, byte[] data, CancellationToken token)
        {
            var header = new byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(header, data.Length);
            await stream.WriteAsync(header, token);
            await stream.WriteAsync(data, token);
            await stream.FlushAsync(token);
        }
    }
}
