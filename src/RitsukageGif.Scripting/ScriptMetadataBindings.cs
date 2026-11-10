using System.Text.Json;

namespace RitsukageGif.Scripting
{
    public static class ScriptMetadataBindings
    {
        public static ScriptBindingRegistry CreateBindings(Stream catalog, string? baseDirectory = null)
        {
            var bindings = CaptureScriptLibrary.CreateBindings(baseDirectory: baseDirectory);
            var metadata = JsonSerializer.Deserialize(catalog, ScriptMetadataJson.Default.ScriptBindingMetadata)
                           ?? throw new InvalidDataException("The script metadata catalog is empty.");
            bindings.ImportMetadata(metadata);
            return bindings;
        }

        public static void ExportDelta(ScriptBindingRegistry bindings, ScriptBindingRegistry baseline, Stream output)
        {
            JsonSerializer.Serialize(output, bindings.ExportMetadataDelta(baseline),
                ScriptMetadataJson.Default.ScriptBindingMetadata);
        }
    }
}
