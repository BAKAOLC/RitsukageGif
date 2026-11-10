using System.Runtime.CompilerServices;
using Avalonia.Media;

namespace RitsukageGif.Desktop.Icons
{
    internal static partial class IconCatalog
    {
        private static readonly ConditionalWeakTable<IconDefinition, CachedSvgIcon> CachedIcons = new();

        internal static IconDefinition GetDefinition(string key)
        {
            return FindDefinition(key) ?? throw new KeyNotFoundException($"Unknown icon key '{key}'.");
        }

        internal static IconDefinition? FindDefinition(string key)
        {
            return TryGetDefinition(key, out var definition) ? definition : null;
        }

        internal static Geometry GetGeometry(string key)
        {
            var paths = GetCached(GetDefinition(key)).Paths;
            return paths.Length == 1
                ? paths[0].Geometry
                : throw new NotSupportedException(
                    $"Icon '{key}' contains multiple paths; render its complete definition with SvgIcon.");
        }

        internal static CachedSvgIcon GetCached(IconDefinition definition)
        {
            return CachedIcons.GetValue(definition, static icon => new(icon));
        }
    }
}
