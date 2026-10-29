using System.Buffers;
using System.Text;

namespace RitsukageGif.Scripting
{
    public sealed class ScriptSourceResolver(Func<string, string?>? openDocument = null)
    {
        private readonly Dictionary<string, Entry> _cache =
            new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

        private readonly Lock _sync = new();

        public string Read(string path)
        {
            path = Path.GetFullPath(path);
            var open = openDocument?.Invoke(path);
            if (open is not null)
            {
                if (open.Length > 262144) throw new IOException(UiText.Get("DslTooComplex"));
                return open;
            }

            var info = new FileInfo(path);
            if (info.Length > 262144) throw new IOException(UiText.Get("DslTooComplex"));
            lock (_sync)
            {
                if (_cache.TryGetValue(path, out var entry) && entry.Length == info.Length &&
                    entry.Modified == info.LastWriteTimeUtc.Ticks)
                    return entry.Text;
                using var reader = File.OpenText(path);
                var buffer = ArrayPool<char>.Shared.Rent(4096);
                string text;
                try
                {
                    var builder = new StringBuilder((int)Math.Min(info.Length, 4096));
                    int count;
                    while ((count = reader.Read(buffer, 0, Math.Min(buffer.Length, 262145 - builder.Length))) > 0)
                    {
                        builder.Append(buffer, 0, count);
                        if (builder.Length > 262144) throw new IOException(UiText.Get("DslTooComplex"));
                    }

                    text = builder.ToString();
                }
                finally
                {
                    ArrayPool<char>.Shared.Return(buffer);
                }

                if (_cache.Count >= 128) _cache.Clear();
                _cache[path] = new(info.Length, info.LastWriteTimeUtc.Ticks, text);
                return text;
            }
        }

        internal string Stamp(string path)
        {
            try
            {
                var text = openDocument?.Invoke(path);
                if (text is not null) return "open:" + text.Length + ":" + text.GetHashCode(StringComparison.Ordinal);
                var info = new FileInfo(path);
                return info.Exists ? info.Length + ":" + info.LastWriteTimeUtc.Ticks : "missing";
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
            {
                return "unavailable";
            }
        }

        private sealed record Entry(long Length, long Modified, string Text);
    }
}
