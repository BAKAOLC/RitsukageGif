using System.Diagnostics;
using System.Text;

namespace RitsukageGif.Desktop
{
    internal sealed class DiagnosticLog(string path) : TraceListener
    {
        private const int MaximumBytes = 1024 * 1024;
        private static readonly string Version = typeof(DiagnosticLog).Assembly.GetName().Version?.ToString() ?? "";
        private readonly object _gate = new();

        internal static string FilePath { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RitsukageGif", "logs",
            "application.log");

        internal static void Initialize()
        {
            Trace.Listeners.Add(new DiagnosticLog(FilePath));
            AppDomain.CurrentDomain.UnhandledException += (_, args) =>
                Trace.TraceError("Unhandled exception: {0}", args.ExceptionObject);
            TaskScheduler.UnobservedTaskException += (_, args) =>
                Trace.TraceError("Unobserved task exception: {0}", args.Exception);
        }

        public override void Write(string? message) => WriteLine(message);

        public override void WriteLine(string? message)
        {
            if (string.IsNullOrWhiteSpace(message)) return;
            lock (_gate)
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    if (message.Length > MaximumBytes / 8) message = message[..(MaximumBytes / 8)];
                    var entry = $"{DateTimeOffset.Now:O} [{Version}] {message}{Environment.NewLine}";
                    if (File.Exists(path) &&
                        new FileInfo(path).Length + Encoding.UTF8.GetByteCount(entry) > MaximumBytes)
                        File.Move(path, Path.ChangeExtension(path, "previous.log"), true);
                    File.AppendAllText(path, entry, Encoding.UTF8);
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                {
                    // Logging must never replace the original failure with a second exception.
                }
            }
        }
    }
}
