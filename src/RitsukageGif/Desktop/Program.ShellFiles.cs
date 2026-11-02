using System.Text.Json;

namespace RitsukageGif.Desktop
{
    internal static partial class Program
    {
        private static string[] PreparePinFilesCommand(string[] args)
        {
            if (args.Sum(value => (long)value.Length * 6 + 3) < 65536 ||
                JsonSerializer.SerializeToUtf8Bytes(args, CommandJson.Default.StringArray).Length <= 65536) return args;
            var directory = Path.Combine(Path.GetTempPath(), "RitsukageGif", "shell");
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "RitsukageGif-shell-" + Guid.NewGuid().ToString("N") + ".temp");
            var created = false;
            try
            {
                using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                created = true;
                JsonSerializer.Serialize(output, args[1..], CommandJson.Default.StringArray);
                return ["pin-files-list", path];
            }
            catch
            {
                if (created) File.Delete(path);
                throw;
            }
        }

        internal static string[] ReadShellFileList(string path)
        {
            var fullPath = ValidateShellFileListPath(path);
            try
            {
                using var input = File.OpenRead(fullPath);
                var paths = JsonSerializer.Deserialize(input, CommandJson.Default.StringArray);
                if (paths is null || paths.Length == 0 || paths.Any(value =>
                        string.IsNullOrWhiteSpace(value) || !Path.IsPathFullyQualified(value)))
                    throw new InvalidDataException("Shell handoff contains no valid absolute file paths.");
                return ["pin-files", .. paths];
            }
            finally
            {
                File.Delete(fullPath);
            }
        }

        private static string ValidateShellFileListPath(string path)
        {
            var fullPath = Path.GetFullPath(path);
            var directory = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "RitsukageGif", "shell"));
            var comparison = OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            if (!string.Equals(Path.GetDirectoryName(fullPath), directory, comparison) ||
                !Path.GetFileName(fullPath).StartsWith("RitsukageGif-shell-", StringComparison.Ordinal) ||
                !Path.GetExtension(fullPath).Equals(".temp", StringComparison.Ordinal))
                throw new ArgumentException("Invalid shell handoff file path.", nameof(path));
            return fullPath;
        }
    }
}
