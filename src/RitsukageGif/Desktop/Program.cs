using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia;

namespace RitsukageGif.Desktop
{
    internal static partial class Program
    {
        private static readonly ILogger ProcessLogger = LogHub.GetLogger("Desktop.Process");
        internal static bool StartHidden { get; private set; }

        [LibraryImport("kernel32.dll")]
        private static partial int AttachConsole(uint processId);

        [STAThread]
        public static int Main(string[] args)
        {
            if (args is ["--version"])
            {
                if (OperatingSystem.IsWindows() && AttachConsole(uint.MaxValue) != 0)
                    Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
                Console.WriteLine(JsonSerializer.Serialize(new(true, "version", ApplicationVersion.Display),
                    CommandJson.Default.CommandReply));
                return 0;
            }

            try
            {
                var role = args is ["--language-server"] ? "language-server" :
                    args.Length > 0 && args[0] != "--serve" ? "command" : "desktop";
                DiagnosticLog.Initialize(role);
                ProcessLogger.ZLogInformation(
                    $"Process startup. Role={role} ArgumentCount={args.Length} FileLogging={LogHub.IsInitialized}");
                var exitCode = Run(args);
                ProcessLogger.ZLogInformation(
                    $"Process completed. ExitCode={exitCode} PendingEntries={LogHub.PendingEntries} DroppedEntries={LogHub.DroppedEntries} ErrorDrops={LogHub.ErrorDrops} SinkFailures={LogHub.SinkFailures}");
                return exitCode;
            }
            catch (Win32Exception error) when (error.NativeErrorCode == 1223)
            {
                Trace.TraceInformation("Application startup cancelled: {0}", error.Message);
                Console.Error.WriteLine(error.Message);
                return 1223;
            }
            catch (Exception error)
            {
                ProcessLogger.ZLogCritical(error, $"Process terminated with an unhandled startup or lifetime failure.");
                Console.Error.WriteLine(error.Message);
                return 1;
            }
            finally
            {
                DiagnosticLog.Shutdown();
            }
        }

        private static int Run(string[] args)
        {
            var settings = AppSettings.Load();
            UiText.SetLanguage(settings.Language);
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
                    return JsonNode.Parse(JsonSerializer.Serialize(reply,
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
                    if (args is ["preset", "run", _, ..])
                        args[2] = Path.GetFullPath(args[2]);
                    if (args is ["editor" or "animation-editor", var editorFile])
                        args[1] = Path.GetFullPath(editorFile);
                    CommandReply reply;
                    if (args is ["--help"] or ["help"])
                    {
                        reply = new(true, "help", UiText.Get("CommandHelp"));
                    }
                    else
                    {
                        if (!Mutex.TryOpenExisting(CommandPipe.MutexName, out var running))
                            Process.Start(new ProcessStartInfo(Environment.ProcessPath!, "--serve")
                                {
                                    UseShellExecute = true, WindowStyle = ProcessWindowStyle.Hidden,
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
                    Console.WriteLine(JsonSerializer.Serialize(new(false, "error", ex.Message),
                        CommandJson.Default.CommandReply));
                    return 1;
                }
            }

            if (Mutex.TryOpenExisting(CommandPipe.MutexName, out var runningInstance))
                using (runningInstance)
                    return ActivateRunningInstance(args);

            if (StartupRegistrationService.RelaunchIfRequired(settings.System, args))
            {
                ProcessLogger.ZLogInformation($"Process handed off startup to an elevated instance.");
                return 0;
            }

            using var instance = new Mutex(true, CommandPipe.MutexName, out var created);
            if (!created) return ActivateRunningInstance(args);

            StartHidden = args is ["--serve"];
            if (OperatingSystem.IsLinux() && !DesktopCapture.UsesSourceSelection) X11ErrorTrap.Initialize();
            var lifetimeExitCode = AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace()
                .StartWithClassicDesktopLifetime(args);
            ProcessLogger.ZLogInformation($"Desktop lifetime returned. ExitCode={lifetimeExitCode}");
            GC.KeepAlive(instance);
            return lifetimeExitCode;
        }

        private static int ActivateRunningInstance(string[] args)
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
    }
}
