using System.Buffers;
using Utf8StringInterpolation;
using ZLogger;

namespace RitsukageGif.Diagnostics
{
    internal sealed class DiagnosticBudgetFormatter(IZLoggerFormatter formatter, DiagnosticLogBudget budget)
        : IZLoggerFormatter
    {
        public bool WithLineBreak => false;

        public void FormatLogEntry(IBufferWriter<byte> writer, IZLoggerEntry entry)
        {
            try
            {
                formatter.FormatLogEntry(writer, entry);
                if (entry.LogInfo.ScopeState is { IsEmpty: false } scope)
                    foreach (var property in scope.Properties)
                        if (property.Key != "{OriginalFormat}")
                            Utf8String.Format(writer, $" [{property.Key}={property.Value}]");
            }
            finally
            {
                try
                {
                    writer.Write("\n"u8);
                }
                finally
                {
                    budget.Release();
                }
            }
        }
    }
}
