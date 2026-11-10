using System.Diagnostics;

namespace RitsukageGif.Desktop
{
    internal static class RecordingCameraProcess
    {
        internal static ProcessStartInfo StartInfo(string executable)
        {
            return new(executable)
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
            };
        }

        internal static void Kill(Process process)
        {
            try
            {
                if (!process.HasExited) process.Kill(true);
            }
            catch (InvalidOperationException)
            {
            }
        }

        internal static async Task<string> DrainAsync(StreamReader reader, CancellationToken token)
        {
            var tail = "";
            var buffer = new char[2048];
            int count;
            while ((count = await reader.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
            {
                tail += new string(buffer, 0, count);
                if (tail.Length > 8192) tail = tail[^8192..];
            }

            return tail;
        }
    }
}
