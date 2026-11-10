using System.Globalization;

namespace RitsukageGif.Desktop
{
    internal sealed class PinnedFileDisplayEntry(string? path, int totalCount)
    {
        private static readonly SemaphoreSlim MetadataWorkers = new(2);
        private static readonly string[] SizeUnits = ["Byte", "KB", "MB", "GB", "TB"];
        private Task<string>? _sizeLabel;

        internal string? Path { get; } = path;
        internal int TotalCount { get; } = totalCount;

        internal Task<string> ReadSizeAsync(CancellationToken token)
        {
            return _sizeLabel ??= ReadMetadataAsync(token);
        }

        private async Task<string> ReadMetadataAsync(CancellationToken token)
        {
            await MetadataWorkers.WaitAsync(token).ConfigureAwait(false);
            try
            {
                return await Task.Run(() => SizeLabel(Path!), token).ConfigureAwait(false);
            }
            finally
            {
                MetadataWorkers.Release();
            }
        }

        private static string SizeLabel(string path)
        {
            long length;
            try
            {
                length = Directory.Exists(path) ? 0 : new FileInfo(path).Length;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                length = 0;
            }

            var value = (double)length;
            var unit = 0;
            while (value >= 1024 && unit < SizeUnits.Length - 1)
            {
                value /= 1024;
                unit++;
            }

            return value.ToString("F2", CultureInfo.InvariantCulture) + " " + SizeUnits[unit];
        }
    }
}
