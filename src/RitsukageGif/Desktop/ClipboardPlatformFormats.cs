using System.Text;
using Avalonia.Input;

namespace RitsukageGif.Desktop
{
    internal static partial class ClipboardPlatformFormats
    {
        internal static readonly string[] ImageNames =
        [
            "image/apng", "image/gif", "GIF", "com.compuserve.gif", "image/webp", "org.webmproject.webp",
            "image/png", "PNG", "public.png", "image/jpeg", "public.jpeg", "image/tiff", "public.tiff",
        ];

        internal static readonly string[] HtmlNames = ["HTML Format", "public.html", "text/html"];

        internal static DataFormat<byte[]> CreateBytesFormat(string name)
        {
            return DataFormat.CreateBytesPlatformFormat(OperatingSystem.IsMacOS() ? MacNativeFormat(name) : name);
        }

        internal static void SetHtml(DataTransferItem item, string html)
        {
            if (OperatingSystem.IsMacOS())
                item.Set(DataFormat.CreateStringPlatformFormat("public.html"), html);
            else
                item.Set(DataFormat.CreateBytesPlatformFormat("text/html"), Encoding.UTF8.GetBytes(html));
        }

        internal static void SetImage(DataTransferItem item, byte[] encoded, string mimeType, byte[] previewPng)
        {
            var name = OperatingSystem.IsMacOS()
                ? mimeType switch
                {
                    "image/png" or "image/apng" => "public.png",
                    "image/gif" => "com.compuserve.gif",
                    "image/webp" => "org.webmproject.webp",
                    _ => mimeType,
                }
                : mimeType;
            item.Set(DataFormat.CreateBytesPlatformFormat(name), encoded);
            if (OperatingSystem.IsWindows())
            {
                switch (mimeType)
                {
                    case "image/png":
                        item.Set(DataFormat.CreateBytesPlatformFormat("PNG"), encoded);
                        break;
                    case "image/gif":
                        item.Set(DataFormat.CreateBytesPlatformFormat("GIF"), encoded);
                        break;
                }

                item.Set(DataFormat.Bitmap, () =>
                {
                    using var stream = new MemoryStream(previewPng, false);
                    return new(stream);
                });
            }
            else if (mimeType is not ("image/png" or "image/apng"))
            {
                item.Set(DataFormat.CreateBytesPlatformFormat(OperatingSystem.IsMacOS() ? "public.png" : "image/png"),
                    previewPng);
            }
        }
    }
}
