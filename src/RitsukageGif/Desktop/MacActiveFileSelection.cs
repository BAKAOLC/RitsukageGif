using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace RitsukageGif.Desktop
{
    internal static class MacActiveFileSelection
    {
        private const string Script = """
                                      use framework "Foundation"
                                      use scripting additions
                                      set selectedPaths to {}
                                      if application "Finder" is running then
                                          tell application "Finder"
                                              if frontmost then
                                                  repeat with selectedItem in selection
                                                      set end of selectedPaths to POSIX path of (selectedItem as alias)
                                                  end repeat
                                              end if
                                          end tell
                                      end if
                                      set jsonData to current application's NSJSONSerialization's dataWithJSONObject:selectedPaths options:0 |error|:(missing value)
                                      set outputHandle to current application's NSFileHandle's fileHandleWithStandardOutput()
                                      outputHandle's writeData:jsonData
                                      return ""
                                      """;

        internal static async Task<IReadOnlyList<string>> ReadAsync(CancellationToken token)
        {
            var start = new ProcessStartInfo("/usr/bin/osascript")
            {
                UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
            };
            start.ArgumentList.Add("-e");
            start.ArgumentList.Add(Script);
            using var process = Process.Start(start) ??
                                throw new IOException("Unable to start Finder selection reader.");
            var output = process.StandardOutput.ReadToEndAsync(token);
            var error = process.StandardError.ReadToEndAsync(token);
            try
            {
                await process.WaitForExitAsync(token);
                var text = await output;
                var failure = await error;
                if (process.ExitCode != 0)
                    throw new IOException($"Finder selection could not be read: {failure.Trim()}");
                return ParsePaths(text);
            }
            finally
            {
                if (!process.HasExited)
                {
                    try
                    {
                        process.Kill(true);
                    }
                    catch (InvalidOperationException) when (process.HasExited)
                    {
                    }

                    await process.WaitForExitAsync(CancellationToken.None);
                }
            }
        }

        private static List<string> ParsePaths(string text)
        {
            try
            {
                using var json = JsonDocument.Parse(text);
                if (json.RootElement.ValueKind != JsonValueKind.Array)
                    throw new InvalidDataException("Finder selection reader returned an invalid path list.");
                var paths = new List<string>(json.RootElement.GetArrayLength());
                foreach (var path in json.RootElement.EnumerateArray())
                {
                    if (path.ValueKind != JsonValueKind.String)
                        throw new InvalidDataException("Finder selection reader returned an invalid path.");
                    paths.Add(path.GetString()!);
                }

                return paths;
            }
            catch (JsonException error)
            {
                throw new InvalidDataException("Finder selection reader returned invalid JSON.", error);
            }
        }
    }
}
