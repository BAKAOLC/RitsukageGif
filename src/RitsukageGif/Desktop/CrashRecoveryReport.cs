using System.Text.Json.Serialization;

namespace RitsukageGif.Desktop
{
    internal sealed record CrashRecoveryReport
    {
        [JsonIgnore] public string Path { get; init; } = "";
        public string SessionId { get; init; } = "";
        public int ProcessId { get; init; }
        public DateTimeOffset ProcessStartedUtc { get; init; }
        public DateTimeOffset StartedUtc { get; init; }
        public string Version { get; init; } = "";
        public string LastOperation { get; init; } = "Desktop startup";
        public DateTimeOffset LastOperationUtc { get; init; }
        public string Reason { get; init; } = "Running";
        public int? ExitCode { get; init; }
        public string? Detail { get; init; }
        public bool Acknowledged { get; init; }
    }
}
