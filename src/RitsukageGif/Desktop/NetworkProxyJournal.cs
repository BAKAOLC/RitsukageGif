namespace RitsukageGif.Desktop
{
    internal static class NetworkProxyJournal
    {
        private static string FilePath => Path.Combine(ApplicationStorage.DataDirectory, "network-proxy.pending");

        internal static bool HasPending => File.Exists(FilePath);

        internal static void Write(string previous, string candidate)
        {
            Directory.CreateDirectory(ApplicationStorage.DataDirectory);
            var temporary = FilePath + ".tmp";
            try
            {
                File.WriteAllText(temporary, previous + "\n" + candidate);
                File.Move(temporary, FilePath, true);
            }
            finally
            {
                File.Delete(temporary);
            }
        }

        internal static (string Previous, string Candidate)? Read()
        {
            if (!File.Exists(FilePath)) return null;
            var ids = File.ReadAllText(FilePath).Split('\n');
            if (ids.Length != 2 || ids.Any(id => id.Length > 0 && !Guid.TryParseExact(id, "N", out _)))
                throw new InvalidDataException("The pending network credential manifest is invalid.");
            return (ids[0], ids[1]);
        }

        internal static void Clear()
        {
            File.Delete(FilePath);
        }
    }
}
