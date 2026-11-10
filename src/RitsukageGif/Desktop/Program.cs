using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
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
        internal static TaskHostOptions? TaskOptions { get; private set; }

        [LibraryImport("kernel32.dll")]
        private static partial int AttachConsole(uint processId);

        [LibraryImport("user32.dll", EntryPoint = "MessageBoxW", StringMarshalling = StringMarshalling.Utf16)]
        // ReSharper disable once UnusedMethodReturnValue.Local
        private static partial int ShowStartupError(nint owner, string text, string title, uint type);

        [STAThread]
        public static int Main(string[] args)
        {
            switch (args)
            {
                case
                    [
                        "--speech-worker", ("capabilities" or "speak") and var speechMode, var speechDirectory,
                        var speechParent,
                    ]
                    when int.TryParse(speechParent, out var speechParentId):
                    return SpeechProcess.RunWorker(speechMode, speechDirectory, speechParentId);
                case ["--speech-worker", ..]: return 2;
                case ["--print-worker", var printInput, var printOwner, var printParent, var printTitle]
                    when long.TryParse(printOwner, CultureInfo.InvariantCulture, out var printWindow) &&
                         int.TryParse(printParent, out var printParentId) && OperatingSystem.IsWindows():
                    return ScreenshotPrintProcess.RunWorker(printInput, (nint)printWindow, printParentId, printTitle);
                case ["--print-worker", ..]: return 2;
                case ["--table-print-worker", var printDirectory, var tableOwner, var tableParent, var tableTitle]
                    when long.TryParse(tableOwner, CultureInfo.InvariantCulture, out var tableWindow) &&
                         int.TryParse(tableParent, out var tableParentId) && OperatingSystem.IsWindows():
                    return ScreenshotPrintProcess.RunTableWorker(printDirectory, (nint)tableWindow, tableParentId,
                        tableTitle);
                case ["--table-print-worker", ..]: return 2;
                case ["--structured-recognition-worker", var models, var parent]
                    when int.TryParse(parent, out var parentId):
                    return StructuredRecognitionProcess.RunWorker(models, parentId);
                case ["--structured-recognition-worker", ..]: return 2;
                case ["--animation-import-worker", var input, var directory]:
                    return AnimationImportProcess.RunWorker(input, directory);
                case ["--animation-import-worker", ..]: return 2;
                case ["--diagnostic-monitor", var process, var session] when int.TryParse(process, out var processId):
                    return CrashSessionMonitor.Run(processId, session);
                case ["--diagnostic-monitor", ..]: return 2;
                case ["--export-script-metadata", var metadataOutput]:
                    return ExportScriptMetadata(metadataOutput);
                case ["--export-script-metadata", ..]: return 2;
                case ["--version"]:
                    if (OperatingSystem.IsWindows() && AttachConsole(uint.MaxValue) != 0)
                        Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
                    Console.WriteLine(JsonSerializer.Serialize(new(true, "version", ApplicationVersion.Display),
                        CommandJson.Default.CommandReply));
                    return 0;
            }

            try
            {
                var role = args is ["--language-server"] ? "language-server" :
                    args is ["--task-host", ..] ? "task" :
                    args.Length == 0 || args is ["--serve"] or ["--silent"] or ["--startup"] ? "desktop" : "command";
                DiagnosticLog.Initialize(role);
                BackgroundStandardStreams.DetachIfBackground(args);
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
                CrashRecoverySession.Current?.RecordFailure(error);
                ProcessLogger.ZLogCritical(error, $"Process terminated with an unhandled startup or lifetime failure.");
                Console.Error.WriteLine(error.Message);
                if (args.Length == 0 || args is ["--serve"])
                {
                    var directory = LogHub.DirectoryPath ?? Path.Combine(Path.GetTempPath(), "RitsukageGif", "logs");
                    var report = CrashRecoverySession.Current?.ReportPath ??
                                 CrashRecoverySession.RecordStartupFailure(directory, error);
                    if (OperatingSystem.IsWindows())
                        ShowStartupError(0,
                            UiText.Format("DiagnosticsStartupFailure", error.Message, report ?? directory),
                            UiText.Get("FatalErrorTitle"), 0x10);
                }

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
            switch (args)
            {
                case ["--language-server"]:
                    new ScriptLanguageServer(async command =>
                        {
                            if (!Mutex.TryOpenExisting(CommandPipe.MutexName, out var running))
                            {
                                var start = CreateSelfStartInfo();
                                start.ArgumentList.Add("--serve");
                                BackgroundStandardStreams.StartBackground(start)?.Dispose();
                            }

                            running?.Dispose();
                            var reply = await CommandPipe.SendAsync(command, CancellationToken.None);
                            return JsonNode.Parse(JsonSerializer.Serialize(reply,
                                CommandJson.Default.CommandReply));
                        }, AutomationScriptLibrary.CreateBindings())
                        .RunAsync(Console.OpenStandardInput(), Console.OpenStandardOutput()).GetAwaiter().GetResult();
                    return 0;
                case ["--task-host", var sessionId, ..]:
                {
                    _ = CommandPipe.SessionName(sessionId);
                    var keepAlive = false;
                    int? parentProcess = null;
                    for (var index = 2; index < args.Length; index++)
                        switch (args[index])
                        {
                            case "--keep-alive": keepAlive = true; break;
                            case "--parent" when index + 1 < args.Length &&
                                                 int.TryParse(args[index + 1], out var parentId):
                                parentProcess = parentId;
                                index++;
                                break;
                            default: throw new ArgumentException("Invalid task host option.");
                        }

                    TaskOptions = new(sessionId, keepAlive, parentProcess);
                    StartHidden = true;
                    return RunApplicationLifetime([], null);
                }
                case not ([] or ["--serve"] or ["--silent"] or ["--startup"]):
                    if (OperatingSystem.IsWindows() && AttachConsole(uint.MaxValue) != 0)
                        Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
                    return RunClient(args);
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

            StartHidden = args is ["--serve"] or ["--silent"] ||
                          (args is ["--startup"] && settings.System.StartupHidden);
            return RunApplicationLifetime(args, instance);
        }

        private static int RunApplicationLifetime(string[] args, object? lifetimeOwner)
        {
            var recovery = CrashRecoverySession.Begin(DiagnosticsActions.LogDirectory);
            if (recovery.SessionPath is { } sessionPath)
                try
                {
                    var monitor = CreateSelfStartInfo();
                    monitor.ArgumentList.Add("--diagnostic-monitor");
                    monitor.ArgumentList.Add(Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
                    monitor.ArgumentList.Add(sessionPath);
                    Process.Start(monitor)?.Dispose();
                }
                catch (Exception error) when (error is Win32Exception or IOException or NotSupportedException)
                {
                    ProcessLogger.ZLogWarning(error,
                        $"Crash monitor could not start; restart detection remains available.");
                }

            if (OperatingSystem.IsLinux() && !DesktopCapture.UsesSourceSelection) X11ErrorTrap.Initialize();
            var lifetimeExitCode = AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace()
                .StartWithClassicDesktopLifetime(args);
            ProcessLogger.ZLogInformation($"Desktop lifetime returned. ExitCode={lifetimeExitCode}");
            recovery.Complete(Application.Current is App { CompletedExpectedTaskShutdown: true }
                ? 0
                : lifetimeExitCode);
            GC.KeepAlive(lifetimeOwner);
            return lifetimeExitCode;
        }

        internal static ProcessStartInfo CreateSelfStartInfo()
        {
            var start = new ProcessStartInfo(Environment.ProcessPath!)
            {
                UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
            };
            if (string.Equals(Path.GetFileNameWithoutExtension(Environment.ProcessPath), "dotnet",
                    StringComparison.OrdinalIgnoreCase))
                start.ArgumentList.Add(Environment.GetCommandLineArgs()[0]);
            return start;
        }

        private static int ActivateRunningInstance(string[] args)
        {
            if (args is ["--serve"] or ["--silent"] ||
                (args is ["--startup"] && AppSettings.Load().System.StartupHidden)) return 0;
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
