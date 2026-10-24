using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Avalonia;

namespace RitsukageGif.Desktop
{
    internal static partial class Program
    {
        internal static bool StartHidden { get; private set; }

        [LibraryImport("kernel32.dll")]
        private static partial int AttachConsole(uint processId);

        [STAThread]
        public static int Main(string[] args)
        {
            DiagnosticLog.Initialize();
            try
            {
                return Run(args);
            }
            catch (Exception error)
            {
                Trace.TraceError("Application terminated: {0}", error);
                Console.Error.WriteLine(error.Message);
                return 1;
            }
        }

        private static int Run(string[] args)
        {
            UiText.SetLanguage(AppSettings.Load().Language);
            if (args is ["--language-server"])
            {
                new ScriptLanguageServer(async command =>
                {
                    if (!Mutex.TryOpenExisting(CommandPipe.MutexName, out var running))
                        Process.Start(new ProcessStartInfo(Environment.ProcessPath!, "--serve")
                                { UseShellExecute = true, WindowStyle = ProcessWindowStyle.Hidden })
                            ?.Dispose();
                    running?.Dispose();
                    var reply = await CommandPipe.SendAsync(command, CancellationToken.None);
                    return System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(reply,
                        CommandJson.Default.CommandReply));
                }).RunAsync(Console.OpenStandardInput(), Console.OpenStandardOutput()).GetAwaiter().GetResult();
                return 0;
            }

            if (args.Length > 0 && args[0] != "--serve")
            {
                if (OperatingSystem.IsWindows() && AttachConsole(uint.MaxValue) != 0)
                    Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
                try
                {
                    if (args.Length >= 3 && args[0] == "preset" && args[1] == "run")
                        args[2] = Path.GetFullPath(args[2]);
                    if (args is ["editor", var editorFile]) args[1] = Path.GetFullPath(editorFile);
                    CommandReply reply;
                    if (args is ["--help"] or ["help"])
                    {
                        reply = new CommandReply(true, "help", UiText.Get("CommandHelp"));
                    }
                    else
                    {
                        if (!Mutex.TryOpenExisting(CommandPipe.MutexName, out var running))
                            Process.Start(new ProcessStartInfo(Environment.ProcessPath!, "--serve")
                                {
                                    UseShellExecute = true, WindowStyle = ProcessWindowStyle.Hidden
                                })
                                ?.Dispose();
                        running?.Dispose();
                        reply = CommandPipe.SendAsync(args, CancellationToken.None).GetAwaiter().GetResult();
                    }

                    Console.WriteLine(JsonSerializer.Serialize(reply, CommandJson.Default.CommandReply));
                    return reply.Success ? 0 : 1;
                }
                catch (Exception ex)
                {
                    Console.WriteLine(JsonSerializer.Serialize(new CommandReply(false, "error", ex.Message),
                        CommandJson.Default.CommandReply));
                    return 1;
                }
            }

            using var instance = new Mutex(true, CommandPipe.MutexName, out var created);
            if (!created)
            {
                if (args is ["--serve"]) return 0;
                try
                {
                    var reply = CommandPipe.SendAsync(["show"], CancellationToken.None).GetAwaiter().GetResult();
                    return reply.Success ? 0 : 1;
                }
                catch (Exception error) when (error is IOException or OperationCanceledException or TimeoutException)
                {
                    return 1;
                }
            }

            StartHidden = args is ["--serve"];
            if (OperatingSystem.IsLinux() && !DesktopCapture.UsesSourceSelection) X11ErrorTrap.Initialize();
            AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace().StartWithClassicDesktopLifetime(args);
            GC.KeepAlive(instance);
            return 0;
        }
    }
}
