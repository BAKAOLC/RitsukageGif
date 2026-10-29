using System.Buffers;
using System.Globalization;
using Utf8StringInterpolation;

namespace RitsukageGif.Diagnostics
{
    internal static class DiagnosticExceptionFormatter
    {
        internal static void Format(IBufferWriter<byte> writer, Exception error)
        {
            var culture = CultureInfo.CurrentUICulture;
            try
            {
                CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
                Utf8String.Format(writer, $"\n{error.ToString()}");
            }
            finally
            {
                CultureInfo.CurrentUICulture = culture;
            }
        }
    }
}
