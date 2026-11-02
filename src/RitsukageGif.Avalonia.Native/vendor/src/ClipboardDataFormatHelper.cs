using System;
using System.Collections.Generic;
using Avalonia.Input;
using Avalonia.Native.Interop;

namespace Avalonia.Native
{
    internal static class ClipboardDataFormatHelper
    {
        // TODO hide native types behind IAvnClipboard abstraction, so managed side won't depend on macOS.
        private const string NsPasteboardTypeString = "public.utf8-plain-text";
        private const string NsPasteboardTypeFileUrl = "public.file-url";
        private const string NsPasteboardTypePng = "public.png";
        private const string AppPrefix = "net.avaloniaui.app.uti.";

        public static DataFormat[] ToDataFormats(IAvnStringArray? nativeFormats, Func<string, bool> isTextFormat)
        {
            if (nativeFormats is null)
                return [];

            var count = nativeFormats.Count;
            if (count == 0)
                return [];

            var results = new List<DataFormat>();

            for (var c = 0u; c < count; c++)
            {
                using var nativeFormat = nativeFormats.Get(c);
                var name = nativeFormat.String ?? string.Empty;
                results.Add(ToDataFormat(name, isTextFormat));
                if (name == NsPasteboardTypePng)
                    results.Add(DataFormat.CreateBytesPlatformFormat(NsPasteboardTypePng));
            }

            return [.. results];
        }

        private static DataFormat ToDataFormat(string nativeFormat, Func<string, bool> isTextFormat)
        {
            return nativeFormat switch
            {
                NsPasteboardTypeString => DataFormat.Text,
                NsPasteboardTypeFileUrl => DataFormat.File,
                NsPasteboardTypePng => DataFormat.Bitmap,
                _ when isTextFormat(nativeFormat) => DataFormat.FromSystemName<string>(nativeFormat, AppPrefix),
                _ => DataFormat.FromSystemName<byte[]>(nativeFormat, AppPrefix),
            };
        }

        public static string ToNativeFormat(DataFormat format)
        {
            if (DataFormat.Text.Equals(format))
                return NsPasteboardTypeString;

            if (DataFormat.File.Equals(format))
                return NsPasteboardTypeFileUrl;

            if (DataFormat.Bitmap.Equals(format))
                return NsPasteboardTypePng;

            return format.ToSystemName(AppPrefix);
        }
    }
}
