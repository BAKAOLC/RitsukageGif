using System.Diagnostics;
using System.Globalization;

namespace RitsukageGif.Desktop
{
    internal sealed class DiagnosticLog : TraceListener
    {
        private static readonly ILogger TraceLogger = LogHub.GetLogger("Desktop.Framework");
        private static int _initialized;

        internal static string FilePath => LogHub.CurrentFilePath ??
                                           Path.Combine(ApplicationStorage.DataDirectory, "logs", "emergency.log");

        internal static void Initialize(string role = "desktop")
        {
            if (Interlocked.Exchange(ref _initialized, 1) != 0) return;
            LogHub.Initialize(ApplicationStorage.DataDirectory, ApplicationVersion.Display, role);
            Trace.Listeners.Add(new DiagnosticLog());
            AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            {
                if (args.ExceptionObject is Exception error)
                    TraceLogger.ZLogCritical(error, $"Unhandled process exception. Terminating={args.IsTerminating}");
                else
                    TraceLogger.ZLogCritical($"Unhandled process failure. Terminating={args.IsTerminating}");
                if (args.IsTerminating) Shutdown();
            };
            TaskScheduler.UnobservedTaskException += (_, args) =>
                TraceLogger.ZLogError(args.Exception, $"Unobserved background task exception.");
            AppDomain.CurrentDomain.ProcessExit += (_, _) =>
            {
                TraceLogger.ZLogInformation($"Process exit observed. ExitCode={Environment.ExitCode}");
                Shutdown();
            };
        }

        internal static void Shutdown()
        {
            LogHub.ShutdownAsync().AsTask().GetAwaiter().GetResult();
        }

        public override void Write(string? message)
        {
            WriteLine(message);
        }

        public override void WriteLine(string? message)
        {
            if (!string.IsNullOrWhiteSpace(message)) TraceLogger.ZLogDebug($"Framework diagnostic: {message}");
        }

        public override void TraceEvent(TraceEventCache? eventCache, string source, TraceEventType eventType,
            int id, string? message)
        {
            // ReSharper disable once SwitchStatementHandlesSomeKnownEnumValuesWithDefault
            switch (eventType)
            {
                case TraceEventType.Critical:
                    TraceLogger.ZLogCritical($"Legacy diagnostic. Source={source} EventId={id} Detail={message}");
                    break;
                case TraceEventType.Error:
                    TraceLogger.ZLogError($"Legacy diagnostic. Source={source} EventId={id} Detail={message}"); break;
                case TraceEventType.Warning:
                    TraceLogger.ZLogWarning($"Legacy diagnostic. Source={source} EventId={id} Detail={message}"); break;
                case TraceEventType.Information:
                    TraceLogger.ZLogInformation($"Legacy diagnostic. Source={source} EventId={id} Detail={message}");
                    break;
                default:
                    TraceLogger.ZLogDebug($"Legacy diagnostic. Source={source} EventId={id} Detail={message}"); break;
            }
        }

        public override void TraceEvent(TraceEventCache? eventCache, string source, TraceEventType eventType,
            int id, string? format, params object?[]? args)
        {
            TraceEvent(eventCache, source, eventType, id, args is { Length: > 0 }
                ? string.Format(CultureInfo.InvariantCulture, format ?? "", args)
                : format);
        }
    }
}
