using System.Globalization;

namespace RitsukageGif.Diagnostics
{
    internal static class DiagnosticEmergency
    {
        [ThreadStatic] private static bool _writing;

        internal static void Write(string operation, Exception? error = null)
        {
            if (_writing) return;
            _writing = true;
            var culture = CultureInfo.CurrentUICulture;
            try
            {
                CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
                Console.Error.WriteLine(string.Create(CultureInfo.InvariantCulture,
                    $"{DateTimeOffset.UtcNow:O} [Diagnostics] {operation}"));
                if (error is not null) Console.Error.WriteLine(error);
                var directory = Path.Combine(Path.GetTempPath(), "RitsukageGif", "logs");
                Directory.CreateDirectory(directory);
                var path = Path.Combine(directory, "emergency.log");
                if (File.Exists(path) && new FileInfo(path).Length >= 1024 * 1024)
                    File.Move(path, Path.Combine(directory, "emergency-previous.log"), true);
                using var file = new FileStream(path, FileMode.Append,
                    FileAccess.Write, FileShare.ReadWrite, 4096, FileOptions.WriteThrough);
                using var writer = new StreamWriter(file);
                writer.WriteLine(string.Create(CultureInfo.InvariantCulture,
                    $"{DateTimeOffset.UtcNow:O} [Diagnostics] {operation}"));
                if (error is not null) writer.WriteLine(error);
            }
            // ReSharper disable once EmptyGeneralCatchClause
            catch (Exception)
            {
            }
            finally
            {
                CultureInfo.CurrentUICulture = culture;
                _writing = false;
            }
        }
    }
}
