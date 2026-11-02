using System.Text.Json.Serialization;

namespace RitsukageGif.Desktop
{
    [JsonSerializable(typeof(PrintPageFile[]))]
    [JsonSerializable(typeof(PrinterPageMetrics))]
    internal partial class DocumentPrintJsonContext : JsonSerializerContext;
}
