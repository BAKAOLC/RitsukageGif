using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace RitsukageGif.Desktop
{
    internal static class SettingsAutomationJson
    {
        internal static string Read(AppSettings settings, string pointer)
        {
            var root = Serialize(settings);
            return Find(root, Segments(pointer))?.ToJsonString() ?? "null";
        }

        internal static AppSettings Write(AppSettings settings, string pointer, string valueJson,
            CancellationToken token)
        {
            var path = Segments(pointer);
            if (path.Length == 0) return SettingsTransfer.ImportJson(valueJson, token);
            SettingsTransfer.ValidateJsonSize(valueJson);
            var root = Serialize(settings);
            var value = JsonNode.Parse(valueJson, documentOptions: new() { MaxDepth = 64 });
            Put(root, path, value, false);
            return SettingsTransfer.ImportJson(root.ToJsonString(), token);
        }

        internal static AppSettings Reset(AppSettings settings, string pointer, CancellationToken token)
        {
            var defaults = new AppSettings();
            var path = Segments(pointer);
            if (path.Length == 0) return Validate(defaults, token);
            var root = Serialize(settings);
            var defaultRoot = Serialize(defaults);
            if (TryFind(defaultRoot, path, out var value))
                Put(root, path, value?.DeepClone(), false);
            else
                Put(root, path, null, true);
            return SettingsTransfer.ImportJson(root.ToJsonString(), token);
        }

        internal static AppSettings Validate(AppSettings settings, CancellationToken token)
        {
            return SettingsTransfer.ImportJson(
                JsonSerializer.Serialize(settings, SettingsJsonContext.Default.AppSettings), token);
        }

        private static JsonNode Serialize(AppSettings settings)
        {
            return JsonSerializer.SerializeToNode(settings, SettingsJsonContext.Default.AppSettings)!;
        }

        private static string[] Segments(string pointer)
        {
            if (pointer.Length == 0) return [];
            if (pointer[0] != '/')
                throw new ArgumentException("A settings JSON pointer must be empty or begin with '/'.");
            var path = pointer[1..].Split('/');
            for (var index = 0; index < path.Length; index++)
            {
                for (var position = 0; position < path[index].Length; position++)
                    if (path[index][position] == '~' &&
                        (++position >= path[index].Length || path[index][position] is not ('0' or '1')))
                        throw new ArgumentException("The settings JSON pointer contains an invalid escape.");
                path[index] = path[index].Replace("~1", "/", StringComparison.Ordinal)
                    .Replace("~0", "~", StringComparison.Ordinal);
            }

            return path;
        }

        private static JsonNode? Find(JsonNode? node, ReadOnlySpan<string> path)
        {
            return TryFind(node, path, out var found)
                ? found
                : throw new KeyNotFoundException("The settings JSON pointer does not identify an existing value.");
        }

        private static bool TryFind(JsonNode? node, ReadOnlySpan<string> path, out JsonNode? found)
        {
            foreach (var key in path)
                switch (node)
                {
                    case JsonObject map when map.TryGetPropertyValue(key, out var child): node = child; break;
                    case JsonArray array when Index(key, array.Count, out var index): node = array[index]; break;
                    default:
                        found = null;
                        return false;
                }

            found = node;
            return true;
        }

        private static void Put(JsonNode root, string[] path, JsonNode? value, bool remove)
        {
            var parent = Find(root, path.AsSpan(0, path.Length - 1));
            var key = path[^1];
            switch (parent)
            {
                case JsonObject map when remove:
                    if (!map.Remove(key))
                        throw new KeyNotFoundException(
                            "The settings JSON pointer does not identify an existing value.");
                    break;
                case JsonObject map:
                    map[key] = value;
                    break;
                case JsonArray array when !remove && key == "-": array.Add(value); break;
                case JsonArray array when Index(key, array.Count, out var index):
                    if (remove) array.RemoveAt(index);
                    else array[index] = value;
                    break;
                default:
                    throw new KeyNotFoundException("The settings JSON pointer does not identify an editable value.");
            }
        }

        private static bool Index(string key, int count, out int index)
        {
            return int.TryParse(key, NumberStyles.None, CultureInfo.InvariantCulture, out index) &&
                   (key.Length == 1 || key[0] != '0') && index >= 0 && index < count;
        }
    }
}
