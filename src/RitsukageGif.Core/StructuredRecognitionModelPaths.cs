namespace RitsukageGif.Core
{
    public sealed record StructuredRecognitionModelPaths(string Directory, string RuntimeLibrary)
    {
        public static StructuredRecognitionModelPaths InDirectory(string directory)
        {
            directory = Path.GetFullPath(directory);
            return new(directory, Path.Combine(directory, OperatingSystem.IsWindows() ? "onnxruntime.dll" :
                OperatingSystem.IsMacOS() ? "libonnxruntime.dylib" : "libonnxruntime.so"));
        }

        private string Model(string name)
        {
            return Path.Combine(Directory, name);
        }

        internal RecognitionProviderCapabilities Capabilities(params string[] files)
        {
            if (!File.Exists(RuntimeLibrary)) return new(false, "The recognition runtime is not installed.");
            foreach (var file in files)
                if (!File.Exists(Model(file)))
                    return new(false, "The recognition model is not installed: " + file);
            return new(true);
        }
    }
}
