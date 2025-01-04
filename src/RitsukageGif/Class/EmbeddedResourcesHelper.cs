using System;
using System.IO;
using System.Reflection;

namespace RitsukageGif.Class
{
    internal static class EmbeddedResourcesHelper
    {
        public static Stream GetStream(Uri uri)
        {
            if (uri.Scheme != "embedded")
                throw new NotSupportedException("Scheme not supported");

            var assembly = Assembly.GetExecutingAssembly();
            var defaultNamespace = assembly.GetName().Name;
            var resourcePath = $"{defaultNamespace}{uri.AbsolutePath.Replace('/', '.').Replace('\\', '.')}";
            var stream = assembly.GetManifestResourceStream(resourcePath);
            var memoryStream = new MemoryStream();
            stream?.CopyTo(memoryStream);
            memoryStream.Seek(0, SeekOrigin.Begin);
            return memoryStream;
        }
    }
}