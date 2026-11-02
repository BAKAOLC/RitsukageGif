namespace RitsukageGif.Desktop
{
    internal static partial class SettingsTransfer
    {
        private static void ValidateColorFormats(CapturePreferences capture)
        {
            if (capture.ColorFormats.Count == 0) throw Invalid("CapturePreferences.ColorFormats");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            for (var index = 0; index < capture.ColorFormats.Count; index++)
            {
                var item = capture.ColorFormats[index];
                var path = $"CapturePreferences.ColorFormats[{index}]";
                if (string.IsNullOrWhiteSpace(item.Id) || string.IsNullOrWhiteSpace(item.Name) ||
                    string.IsNullOrEmpty(item.Template) || !ids.Add(item.Id)) throw Invalid(path);
                if (!SampleColorTokenRegistry.Default.TryCompile(item.Template, out _, out var error))
                    throw new ArgumentException($"{path}.Template ({error!.Offset + 1}): {error.Message}");
            }

            if (!ids.Contains(capture.MagnifierColorFormatId))
                throw Invalid("CapturePreferences.MagnifierColorFormatId");
        }
    }
}
