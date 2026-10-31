using System.Diagnostics;
using System.IO.Compression;
using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace RitsukageGif.Desktop
{
    internal static class DiagnosticsActions
    {
        internal static string LogDirectory => LogHub.DirectoryPath ??
                                               Path.Combine(ApplicationStorage.DataDirectory, "logs");

        internal static IEnumerable<string> LogDirectories => new[]
        {
            LogDirectory, Path.Combine(ApplicationStorage.DataDirectory, "logs"),
            Path.Combine(Path.GetTempPath(), "RitsukageGif", "logs"),
        }.Distinct(StringComparer.OrdinalIgnoreCase);

        internal static void OpenLogs()
        {
            PlatformShell.OpenDirectory(LogDirectory);
        }

        internal static void ReportIssue()
        {
            var body =
                $"Version: {ApplicationVersion.Display}\nOS: {Environment.OSVersion}\n\nSteps to reproduce:\n\nExpected behavior:\n\nActual behavior:\n\nPlease review and attach the exported diagnostic archive. It is not uploaded automatically.";
            Process.Start(new ProcessStartInfo("https://github.com/BAKAOLC/RitsukageGif/issues/new?body=" +
                                               Uri.EscapeDataString(body)) { UseShellExecute = true })?.Dispose();
        }

        internal static async Task<bool> ExportAsync(Window owner, CancellationToken token = default)
        {
            var selected = await owner.StorageProvider.SaveFilePickerAsync(new()
            {
                Title = UiText.Get("DiagnosticsExport"),
                SuggestedFileName = $"RitsukageGif-diagnostics-{DateTime.Now:yyyyMMdd-HHmmss}.zip",
                DefaultExtension = "zip", ShowOverwritePrompt = true,
                FileTypeChoices = [new("ZIP") { Patterns = ["*.zip"] }],
            });
            if (selected?.TryGetLocalPath() is not { } path) return false;
            await Task.Run(() => Export(path, token), token);
            return true;
        }

        private static void Export(string path, CancellationToken token)
        {
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var archive = ZipFile.Open(temporary, ZipArchiveMode.Create))
                {
                    var directoryIndex = 0;
                    foreach (var directory in LogDirectories)
                    {
                        if (!Directory.Exists(directory)) continue;
                        foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
                        {
                            token.ThrowIfCancellationRequested();
                            if (Path.GetExtension(file) is not (".log" or ".txt" or ".json")) continue;
                            FileStream input;
                            try
                            {
                                input = new(file, FileMode.Open, FileAccess.Read,
                                    FileShare.ReadWrite | FileShare.Delete);
                            }
                            catch (FileNotFoundException)
                            {
                                continue;
                            }
                            catch (DirectoryNotFoundException)
                            {
                                continue;
                            }

                            using (input)
                            {
                                var entry = archive.CreateEntry($"logs-{directoryIndex}/" +
                                                                Path.GetRelativePath(directory, file)
                                                                    .Replace('\\', '/'), CompressionLevel.Fastest);
                                using var output = entry.Open();
                                input.CopyTo(output);
                            }
                        }

                        directoryIndex++;
                    }
                }

                token.ThrowIfCancellationRequested();
                File.Move(temporary, path, true);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }
    }
}
