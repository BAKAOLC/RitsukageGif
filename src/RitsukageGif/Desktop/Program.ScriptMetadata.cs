namespace RitsukageGif.Desktop
{
    internal static partial class Program
    {
        private static int ExportScriptMetadata(string output)
        {
            try
            {
                var path = Path.GetFullPath(output);
                var baseDirectory = Path.GetDirectoryName(path)!;
                var bindings = AutomationScriptLibrary.CreateBindings(baseDirectory);
                var baseline = CaptureScriptLibrary.CreateBindings(baseDirectory: baseDirectory);
                using var stream = File.Create(path);
                ScriptMetadataBindings.ExportDelta(bindings, baseline, stream);
                return 0;
            }
            catch (Exception error)
            {
                Console.Error.WriteLine(error.Message);
                return 1;
            }
        }
    }
}
