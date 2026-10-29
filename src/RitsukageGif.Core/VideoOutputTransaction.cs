using System.Runtime.ExceptionServices;

namespace RitsukageGif.Core
{
    internal static class VideoOutputTransaction
    {
        private static readonly ILogger Logger = LogHub.GetLogger("Core.Output");

        internal static void Publish(IReadOnlyList<string> staged, IReadOnlyList<string> destinations, bool overwrite)
        {
            if (staged.Count == 0 || staged.Count != destinations.Count)
                throw new ArgumentException(UiText.Get("InvalidRecording"), nameof(destinations));
            var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
            if (destinations.Select(Path.GetFullPath).Distinct(comparer).Count() != destinations.Count ||
                staged.Select(Path.GetFullPath).Intersect(destinations.Select(Path.GetFullPath), comparer).Any())
                throw new ArgumentException(UiText.Get("InvalidRecording"), nameof(destinations));
            var backups = new string?[destinations.Count];
            var leases = new OutputFileLease?[destinations.Count];
            try
            {
                for (var index = 0; index < staged.Count; index++) leases[index] = new(staged[index]);
                for (var index = 0; index < destinations.Count; index++)
                {
                    var destination = destinations[index];
                    if (overwrite && File.Exists(destination))
                    {
                        var backup = destination + "." + Guid.NewGuid().ToString("N") + ".backup";
                        File.Move(destination, backup);
                        backups[index] = backup;
                    }

                    leases[index]!.Publish(destination);
                }
            }
            catch (Exception failure)
            {
                Logger.ZLogWarning(failure,
                    $"Video output publication failed; rolling back: outputs={destinations.Count}, exceptionType={failure.GetType().Name}, hResult={failure.HResult}");
                var errors = new List<Exception>();
                for (var index = destinations.Count - 1; index >= 0; index--)
                {
                    try
                    {
                        leases[index]?.Rollback();
                    }
                    catch (Exception error)
                    {
                        errors.Add(error);
                    }
                    finally
                    {
                        leases[index]?.Dispose();
                        leases[index] = null;
                    }

                    try
                    {
                        if (backups[index] is { } backup) File.Move(backup, destinations[index]);
                    }
                    catch (Exception error)
                    {
                        errors.Add(error);
                    }
                }

                if (errors.Count > 0)
                {
                    var aggregate = new AggregateException([failure, .. errors]);
                    Logger.ZLogError(aggregate,
                        $"Video output rollback encountered additional failures: count={errors.Count}");
                    throw aggregate;
                }

                ExceptionDispatchInfo.Capture(failure).Throw();
            }
            finally
            {
                foreach (var lease in leases) lease?.Dispose();
            }

            foreach (var backup in backups)
            {
                if (backup is null) continue;
                try
                {
                    File.Delete(backup);
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                {
                    Logger.ZLogWarning(error,
                        $"Committed video output backup cleanup deferred: exceptionType={error.GetType().Name}, hResult={error.HResult}");
                }
            }
        }
    }
}
