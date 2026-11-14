using System.Globalization;
using System.Text;
using System.Text.Json;

namespace RitsukageGif.Desktop
{
    internal static partial class Program
    {
        private static int RunClient(string[] arguments)
        {
            string? ownedShellList = null;
            string? createdSession = null;
            var preserveTask = false;
            var completedReply = false;
            var cancellation = new CancellationTokenSource();
            var cancelGate = new Lock();
            var clientFinished = false;
            ConsoleCancelEventHandler cancel = (_, e) =>
            {
                e.Cancel = true;
                lock (cancelGate)
                    // ReSharper disable once AccessToModifiedClosure
                    if (!clientFinished)
                        // ReSharper disable once AccessToDisposedClosure
                        cancellation.Cancel();
            };
            Console.CancelKeyPress += cancel;
            try
            {
                var args = arguments;
                var task = false;
                var keepAlive = false;
                var reuseCore = false;
                var taskOptionSpecified = false;
                string? session = null;
                while (args.Length > 0 && args[0].StartsWith("--", StringComparison.Ordinal))
                    switch (args)
                    {
                        case ["--task", .. var rest]:
                            task = true;
                            args = rest;
                            break;
                        case ["--keep-alive", .. var rest]:
                            taskOptionSpecified = true;
                            keepAlive = true;
                            args = rest;
                            break;
                        case ["--exit-when-done", .. var rest]:
                            taskOptionSpecified = true;
                            keepAlive = false;
                            args = rest;
                            break;
                        case ["--reuse-core", .. var rest]:
                            taskOptionSpecified = true;
                            reuseCore = true;
                            args = rest;
                            break;
                        case ["--session", var id, .. var rest]:
                            session = id;
                            args = rest;
                            break;
                        default: goto OptionsComplete;
                    }

                OptionsComplete:
                if (args.Length == 0) throw new ArgumentException("A command is required.");
                if (!task && taskOptionSpecified) throw new ArgumentException("Task options require --task.");
                if (session is not null && task) throw new ArgumentException("Use either --task or --session.");
                args = PrepareCommand(args);
                if (args is ["pin-files-list", var shellList])
                    args[1] = ownedShellList = ValidateShellFileListPath(shellList);
                CommandReply reply;
                if (args is ["--help"] or ["help"])
                {
                    reply = new(true, "help", UiText.Get("CommandHelp"));
                }
                else
                {
                    var endpoint = session is null ? null : CommandPipe.SessionName(session);
                    var hasResident = false;
                    if (task && reuseCore)
                    {
                        hasResident = Mutex.TryOpenExisting(CommandPipe.MutexName, out var existingMutex);
                        existingMutex?.Dispose();
                    }

                    switch (task)
                    {
                        case true when !hasResident:
                        {
                            preserveTask = keepAlive;
                            createdSession = Guid.NewGuid().ToString("N");
                            endpoint = CommandPipe.SessionName(createdSession);
                            var start = CreateSelfStartInfo();
                            start.ArgumentList.Add("--task-host");
                            start.ArgumentList.Add(createdSession);
                            if (keepAlive) start.ArgumentList.Add("--keep-alive");
                            start.ArgumentList.Add("--parent");
                            start.ArgumentList.Add(Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
                            start.UseShellExecute = false;
                            start.CreateNoWindow = true;
                            BackgroundStandardStreams.StartBackground(start)?.Dispose();
                            args = ["task", "execute", .. args];
                            break;
                        }
                        case false when session is null:
                        {
                            var running = Mutex.TryOpenExisting(CommandPipe.MutexName, out var mutex);
                            mutex?.Dispose();
                            if (!running)
                            {
                                var start = CreateSelfStartInfo();
                                start.ArgumentList.Add("--serve");
                                start.UseShellExecute = false;
                                start.CreateNoWindow = true;
                                BackgroundStandardStreams.StartBackground(start)?.Dispose();
                            }

                            break;
                        }
                    }

                    reply = CommandPipe.SendAsync(args, cancellation.Token, endpoint).GetAwaiter().GetResult();
                }

                completedReply = true;
                Console.WriteLine(JsonSerializer.Serialize(reply, CommandJson.Default.CommandReply));
                return reply.Success ? 0 : reply.State == "cancelled" ? 130 : 1;
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
                Console.WriteLine(JsonSerializer.Serialize(
                    new(false, "cancelled", "Operation cancelled.", SessionId: preserveTask ? createdSession : null),
                    CommandJson.Default.CommandReply));
                return 130;
            }
            catch (Exception error)
            {
                Console.WriteLine(JsonSerializer.Serialize(
                    new(false, "error", error.Message, SessionId: preserveTask ? createdSession : null),
                    CommandJson.Default.CommandReply));
                return 1;
            }
            finally
            {
                Console.CancelKeyPress -= cancel;
                lock (cancelGate)
                {
                    clientFinished = true;
                    cancellation.Dispose();
                }

                if (ownedShellList is not null) File.Delete(ownedShellList);
                // Only this client's independently created endpoint can be shut down after a disconnected command.
                if (createdSession is not null && !preserveTask && !completedReply)
                    try
                    {
                        CommandPipe.SendAsync(["exit"], CancellationToken.None,
                            CommandPipe.SessionName(createdSession)).GetAwaiter().GetResult();
                    }
                    catch (Exception error) when
                        (error is IOException or OperationCanceledException or TimeoutException)
                    {
                    }
            }
        }

        private static string[] PrepareCommand(string[] args)
        {
            switch (args)
            {
                case ["script", ("file" or "eval" or "stdin") and var mode, .. var scriptArgs]:
                {
                    string source;
                    string text;
                    var options = scriptArgs;
                    if (mode == "stdin")
                    {
                        source = Path.Combine(Environment.CurrentDirectory, "stdin.rgscript");
                        using var input = Console.OpenStandardInput();
                        text = ReadScript(input);
                    }
                    else
                    {
                        if (scriptArgs.Length == 0) throw new ArgumentException("A script file or source is required.");
                        source = mode == "file"
                            ? Path.GetFullPath(scriptArgs[0])
                            : Path.Combine(Environment.CurrentDirectory, "eval.rgscript");
                        if (mode == "file")
                        {
                            using var input = File.OpenRead(source);
                            text = ReadScript(input);
                        }
                        else
                        {
                            text = scriptArgs[0];
                        }

                        options = scriptArgs[1..];
                    }

                    if (Encoding.UTF8.GetByteCount(text) > 1024 * 1024)
                        throw new ArgumentException("Script source exceeds 1 MiB.");
                    var id = Guid.NewGuid().ToString("N");
                    var preset = "";
                    var assignments = new List<string>();
                    for (var index = 0; index < options.Length; index += 2)
                    {
                        if (index + 1 == options.Length) throw new ArgumentException("An option value is missing.");
                        switch (options[index])
                        {
                            case "--arg": assignments.Add(options[index + 1]); break;
                            case "--id": id = options[index + 1]; break;
                            case "--preset": preset = options[index + 1]; break;
                            default: throw new ArgumentException("Unknown script option.");
                        }
                    }

                    return ["script", "execute", id, source, preset, text, .. assignments];
                }
                case ["preset", "run", _, ..]:
                case ["settings", "import" or "export", _]:
                    args[2] = Path.GetFullPath(args[2]);
                    break;
                case ["editor" or "animation-editor", _]:
                    args[1] = Path.GetFullPath(args[1]);
                    break;
                case ["pin-files", _, ..]:
                    for (var index = 1; index < args.Length; index++) args[index] = Path.GetFullPath(args[index]);
                    return PreparePinFilesCommand(args);
            }

            return args;
        }

        private static string ReadScript(Stream stream)
        {
            using var bytes = new MemoryStream();
            var buffer = new byte[8192];
            int count;
            while ((count = stream.Read(buffer)) != 0)
            {
                if (bytes.Length + count > 1024 * 1024) throw new ArgumentException("Script source exceeds 1 MiB.");
                bytes.Write(buffer, 0, count);
            }

            return new UTF8Encoding(false, true).GetString(bytes.GetBuffer(), 0, (int)bytes.Length).TrimStart('\uFEFF');
        }
    }
}
