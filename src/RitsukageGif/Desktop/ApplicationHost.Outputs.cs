using System.Security;
using System.Text;
using Avalonia.Input;
using Avalonia.Platform.Storage;

namespace RitsukageGif.Desktop
{
    public sealed partial class ApplicationHost
    {
        private IReadOnlyList<string> BuildOutputPaths(WindowPreset? preset, string extension, int sequence)
        {
            var timestamp = DateTime.Now;
            var outputs = preset?.Outputs.Count > 0
                ? preset.Outputs
                : [new PresetOutput(_settings.OutputDirectory, "{timestamp}-{id}.{ext}")];
            var result = new List<string>();
            foreach (var output in outputs)
            {
                var directory = Path.GetFullPath(Environment.ExpandEnvironmentVariables(output.Directory));
                var name = PresetOutputNaming.Filename(output.Filename, preset?.Name ?? "",
                    _presets.Target?.Title ?? preset?.Title ?? "", _presets.Target?.Process ?? preset?.Process ?? "",
                    extension, sequence, timestamp);
                var path = Path.Combine(directory, name);
                var counter = 1;
                while (File.Exists(path) || result.Contains(path, StringComparer.OrdinalIgnoreCase))
                    path = Path.Combine(directory,
                        Path.GetFileNameWithoutExtension(name) + $"-{counter++}" + extension);
                result.Add(path);
            }

            return result;
        }

        private async Task CopyOutputsAsync(string source, IReadOnlyList<string> outputs, CancellationToken token)
        {
            var completed = new List<string> { source };
            try
            {
                foreach (var destination in outputs.Skip(1))
                {
                    token.ThrowIfCancellationRequested();
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    var staging = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
                    try
                    {
                        await using (var input =
                                     new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 131072,
                                         true))
                        await using (var output = new FileStream(staging, FileMode.CreateNew, FileAccess.Write,
                                         FileShare.None, 131072, true))
                            await input.CopyToAsync(output, token);
                        token.ThrowIfCancellationRequested();
                        File.Move(staging, destination, false);
                        completed.Add(destination);
                    }
                    finally
                    {
                        if (File.Exists(staging)) File.Delete(staging);
                    }
                }

                token.ThrowIfCancellationRequested();
            }
            catch
            {
                foreach (var path in completed)
                {
                    try
                    {
                        File.Delete(path);
                    }
                    catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                    {
                        System.Diagnostics.Trace.TraceError("Could not remove incomplete output {0}: {1}", path,
                            error.Message);
                    }
                }

                throw;
            }
        }

        internal async Task CopyAsync()
        {
            if (_lastPath == null) return;
            await CopyFilesAsync([_lastPath]);
        }

        internal async Task CopyFilesAsync(IReadOnlyList<string> paths)
        {
            var context = _services.Context;
            var clipboard = context.Clipboard;
            if (clipboard is null) return;
            var data = new DataTransfer();
            foreach (var path in paths)
            {
                var file = await context.StorageProvider.TryGetFileFromPathAsync(path);
                if (file == null) continue;
                var item = DataTransferItem.CreateFile(file);
                var escaped = SecurityElement.Escape(path);
                var qq = Encoding.UTF8.GetBytes(
                    $"<QQRichEditFormat><Info version=\"1001\"></Info><EditElement type=\"1\" filepath=\"{escaped}\" shortcut=\"\"></EditElement></QQRichEditFormat>");
                var html = ClipboardHtml.CreateFragment(path);
                if (OperatingSystem.IsWindows())
                    item.Set(DataFormat.CreateBytesPlatformFormat("HTML Format"), ClipboardHtml.CreateWindows(html));
                else
                    item.Set(
                        DataFormat.CreateStringPlatformFormat(OperatingSystem.IsMacOS() ? "public.html" : "text/html"),
                        html);
                item.Set(DataFormat.CreateBytesPlatformFormat("QQ_Unicode_RichEdit_Format"), qq);
                item.Set(DataFormat.CreateBytesPlatformFormat("QQ_RichEdit_Format"), qq);
                data.Add(item);
            }

            await clipboard.SetDataAsync(data);
            await clipboard.FlushAsync();
            QueueCleanup(clipboardPaths: paths);
        }
    }
}
