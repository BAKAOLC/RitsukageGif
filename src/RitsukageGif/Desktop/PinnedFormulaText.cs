using System.Text;

namespace RitsukageGif.Desktop
{
    internal static class PinnedFormulaText
    {
        public static string Serialize(IReadOnlyList<string> sources)
        {
            if (sources.Count == 1) return sources[0];
            var text = new StringBuilder();
            foreach (var source in sources)
            {
                if (text.Length > 0) text.AppendLine();
                text.AppendLine("\\[");
                text.AppendLine(source);
                text.AppendLine("\\]");
            }

            return text.ToString();
        }
    }
}
