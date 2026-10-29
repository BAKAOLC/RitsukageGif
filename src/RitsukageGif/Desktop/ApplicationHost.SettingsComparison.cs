namespace RitsukageGif.Desktop
{
    public sealed partial class ApplicationHost
    {
        private static bool SameShortcuts(Dictionary<string, string[]> left, Dictionary<string, string[]> right)
        {
            return left.Count == right.Count && left.All(pair =>
                right.TryGetValue(pair.Key, out var values) &&
                pair.Value.SequenceEqual(values, StringComparer.Ordinal));
        }

        private static bool SameActions(List<GlobalActionBinding> left, List<GlobalActionBinding> right)
        {
            if (left.Count != right.Count) return false;
            for (var index = 0; index < left.Count; index++)
            {
                var a = left[index];
                var b = right[index];
                if (a.Id != b.Id || a.Name != b.Name || a.Kind != b.Kind || a.Shortcut != b.Shortcut ||
                    a.ShowInTray != b.ShowInTray || a.Enabled != b.Enabled || a.Parameter != b.Parameter ||
                    a.PresetName != b.PresetName || a.Arguments.Count != b.Arguments.Count ||
                    a.Arguments.Any(pair => !b.Arguments.TryGetValue(pair.Key, out var value) || value != pair.Value))
                    return false;
            }

            return true;
        }
    }
}
