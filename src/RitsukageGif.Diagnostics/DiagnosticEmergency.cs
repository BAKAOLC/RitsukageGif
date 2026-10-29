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
