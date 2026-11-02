namespace RitsukageGif.Desktop
{
    internal sealed partial class ScreenshotAnnotation
    {
        private static readonly ILogger WatermarkLogger = LogHub.GetLogger("Desktop.Annotation.Watermark");

        // ReSharper disable once ReplaceWithFieldKeyword
        private string? _watermarkDisplayText;
        public DateTimeOffset WatermarkTimestamp { get; init; }

        public string DisplayText
        {
            get
            {
                if (Tool != ScreenshotTool.Watermark) return Text;
                if (_watermarkDisplayText is not null) return _watermarkDisplayText;
                if (WatermarkTextTemplate.TryCompile(Text, out var template, out var error))
                    return _watermarkDisplayText = template!.Format(WatermarkTimestamp);
                WatermarkLogger.ZLogWarning(
                    $"Watermark time template rendered as literal text. Offset={error!.Offset} Detail={error.Message}");
                return _watermarkDisplayText = Text;
            }
        }
    }
}
