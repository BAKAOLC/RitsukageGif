using System.Diagnostics;
using System.Globalization;
using System.Xml;
using System.Xml.Linq;

namespace RitsukageGif.Desktop
{
    internal static class DirectoryOpusActiveFileSelection
    {
        internal static async Task<IReadOnlyList<string>> ReadAsync(
            nint invocationLister, string invocationProcessPath, CancellationToken token)
        {
            if (!Path.GetFileName(invocationProcessPath).Equals("dopus.exe", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("The invocation process is not Directory Opus.",
                    nameof(invocationProcessPath));
            var executable = Path.Combine(Path.GetDirectoryName(invocationProcessPath)!, "dopusrt.exe");
            if (!File.Exists(executable))
                throw new FileNotFoundException("Directory Opus selection reader was not found.", executable);
            var directory = Directory.CreateTempSubdirectory("RitsukageGif-Opus-");
            try
            {
                const string xmlPath = "selection.xml";
                var paths = await QueryAsync(executable, directory.FullName, xmlPath, "paths", token);
                var tab = FindSourceTab(paths, invocationLister);
                var items = await QueryAsync(executable, directory.FullName, xmlPath, $"listsel,0x{tab:x}", token);
                return ReadSelectedItems(items, invocationLister, tab);
            }
            finally
            {
                directory.Delete(true);
            }
        }

        private static nuint FindSourceTab(XElement paths, nint invocationLister)
        {
            nuint found = 0;
            foreach (var path in paths.Elements("path"))
            {
                if (ReadHandle(path, "lister") != (nuint)invocationLister ||
                    (string?)path.Attribute("tab_state") != "1")
                    continue;
                if (found != 0)
                    throw new InvalidDataException(
                        "Directory Opus returned more than one source tab for the invocation window.");
                found = ReadHandle(path, "tab");
            }

            return found != 0
                ? found
                : throw new IOException("The invocation Directory Opus source tab is no longer available.");
        }

        private static List<string> ReadSelectedItems(XElement result, nint invocationLister, nuint tab)
        {
            var items = result.Element("items") ??
                        throw new InvalidDataException("Directory Opus returned no item list.");
            if (ReadHandle(items, "lister") != (nuint)invocationLister || ReadHandle(items, "tab") != tab)
                throw new IOException("Directory Opus returned a different source tab.");
            var paths = new List<string>();
            foreach (var item in items.Elements("item"))
            {
                var path = (string?)item.Attribute("path");
                if (path is not null && Path.IsPathFullyQualified(path) &&
                    (File.Exists(path) || Directory.Exists(path)))
                    paths.Add(path);
            }

            return paths;
        }

        private static nuint ReadHandle(XElement element, string name)
        {
            var text = (string?)element.Attribute(name);
            if (text is null || !text.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ||
                !nuint.TryParse(text.AsSpan(2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture,
                    out var value))
                throw new InvalidDataException("Directory Opus returned an invalid window handle.");
            return value;
        }

        private static async Task<XElement> QueryAsync(string executable, string directory, string output,
            string command, CancellationToken token)
        {
            var outputPath = Path.Combine(directory, output);
            File.Delete(outputPath);
            var start = new ProcessStartInfo(executable)
                { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = directory };
            start.ArgumentList.Add("/info");
            start.ArgumentList.Add($"{output},{command}");
            using var process = Process.Start(start) ??
                                throw new IOException("Unable to start Directory Opus selection reader.");
            try
            {
                await process.WaitForExitAsync(token);
                if (process.ExitCode != 0)
                    throw new IOException($"Directory Opus selection reader exited with code {process.ExitCode}.");
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

            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
            using var reader = XmlReader.Create(outputPath, settings);
            var result = XElement.Load(reader);
            var separator = command.IndexOf(',');
            var expectedCommand = separator >= 0 ? command[..separator] : command;
            if (result.Name != "results" || (string?)result.Attribute("result") != "1" ||
                expectedCommand != (string?)result.Attribute("command"))
                throw new InvalidDataException("Directory Opus did not return the requested information.");
            return result;
        }
    }
}
