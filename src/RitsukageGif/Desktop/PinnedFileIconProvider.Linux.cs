using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia.Threading;
using SkiaSharp;

namespace RitsukageGif.Desktop
{
    internal static partial class PinnedFileIconProvider
    {
        [SupportedOSPlatform("linux")]
        private static async Task<byte[]> ReadLinuxAsync(string path, int size, CancellationToken token)
        {
            var variant = await Task.Run(() => QueryLinuxIcon(path, token), token).ConfigureAwait(false);
            try
            {
                var frame = await Dispatcher.UIThread.InvokeAsync(() => ReadLinuxThemePixels(variant, size, token),
                    DispatcherPriority.Background, token);
                return await Task.Run(() => EncodeLinuxPixels(frame, token), token).ConfigureAwait(false);
            }
            finally
            {
                GVariantUnref(variant);
            }
        }

        [SupportedOSPlatform("linux")]
        private static nint QueryLinuxIcon(string path, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var file = GFileNewForPath(path);
            if (file == 0) throw new IOException("GIO could not create a file icon query.");
            nint info = 0;
            nint ownedIcon = 0;
            try
            {
                nint error = 0;
                info = GFileQueryInfo(file, "standard::icon,standard::content-type", 0, 0, ref error);
                FreeIconError(ref error);
                var icon = info != 0 ? GFileInfoGetIcon(info) : 0;
                if (icon == 0)
                {
                    var contentType = GContentTypeGuess(path, 0, 0, out _);
                    if (contentType == 0) throw new IOException("GIO returned no file content type.");
                    try
                    {
                        icon = ownedIcon = GContentTypeGetIcon(contentType);
                    }
                    finally
                    {
                        GFree(contentType);
                    }
                }

                token.ThrowIfCancellationRequested();
                if (icon == 0) throw new IOException("GIO returned no associated icon.");
                var variant = GIconSerialize(icon);
                if (variant == 0) throw new IOException("The associated GIO icon could not be serialized.");
                return variant;
            }
            finally
            {
                if (ownedIcon != 0) GObjectUnref(ownedIcon);
                if (info != 0) GObjectUnref(info);
                GObjectUnref(file);
            }
        }

        [SupportedOSPlatform("linux")]
        private static (byte[] Pixels, int Width, int Height, int Channels) ReadLinuxThemePixels(
            nint variant, int size, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (GtkInitCheck(0, 0) == 0)
                throw new IOException("GTK could not open the current icon theme.");
            var icon = GIconDeserialize(variant);
            if (icon == 0) throw new IOException("The associated GIO icon could not be deserialized.");
            nint iconInfo = 0;
            nint pixbuf = 0;
            try
            {
                var theme = GtkIconThemeGetDefault();
                if (theme == 0) throw new IOException("GTK returned no associated icon theme.");
                iconInfo = GtkIconThemeLookupByGIcon(theme, icon, size, 0);
                if (iconInfo == 0 || GtkIconInfoGetFilename(iconInfo) == 0)
                    throw new IOException("The active icon theme contains no file for the associated GIO icon.");
                nint error = 0;
                pixbuf = GtkIconInfoLoadIcon(iconInfo, ref error);
                FreeIconError(ref error);
                if (pixbuf == 0) throw new IOException("The associated theme icon could not be loaded.");
                token.ThrowIfCancellationRequested();
                var width = GdkPixbufGetWidth(pixbuf);
                var height = GdkPixbufGetHeight(pixbuf);
                var channels = GdkPixbufGetNChannels(pixbuf);
                var stride = GdkPixbufGetRowstride(pixbuf);
                var source = GdkPixbufReadPixels(pixbuf);
                var rowBytes = width * channels;
                var pixels = new byte[rowBytes * height];
                for (var row = 0; row < height; row++)
                    Marshal.Copy(source + row * stride, pixels, row * rowBytes, rowBytes);
                return (pixels, width, height, channels);
            }
            finally
            {
                if (pixbuf != 0) GObjectUnref(pixbuf);
                if (iconInfo != 0) GObjectUnref(iconInfo);
                GObjectUnref(icon);
            }
        }

        private static byte[] EncodeLinuxPixels((byte[] Pixels, int Width, int Height, int Channels) frame,
            CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            using var bitmap = new SKBitmap(new(frame.Width, frame.Height,
                SKColorType.Rgba8888, SKAlphaType.Unpremul));
            if (frame.Channels == 4)
            {
                Marshal.Copy(frame.Pixels, 0, bitmap.GetPixels(), frame.Pixels.Length);
            }
            else
            {
                var target = bitmap.GetPixelSpan();
                for (int source = 0, destination = 0; source < frame.Pixels.Length; source += 3, destination += 4)
                {
                    target[destination] = frame.Pixels[source];
                    target[destination + 1] = frame.Pixels[source + 1];
                    target[destination + 2] = frame.Pixels[source + 2];
                    target[destination + 3] = 255;
                }
            }

            token.ThrowIfCancellationRequested();
            using var image = SKImage.FromBitmap(bitmap);
            using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
            if (encoded.Size > 4 * 1024 * 1024)
                throw new IOException("The associated theme icon exceeds the PNG cache entry budget.");
            return encoded.ToArray();
        }

        private static void FreeIconError(ref nint error)
        {
            if (error == 0) return;
            GErrorFree(error);
            error = 0;
        }

        [LibraryImport("libgtk-3.so.0", EntryPoint = "gtk_init_check")]
        private static partial int GtkInitCheck(nint argc, nint argv);

        [LibraryImport("libgtk-3.so.0", EntryPoint = "gtk_icon_theme_get_default")]
        private static partial nint GtkIconThemeGetDefault();

        [LibraryImport("libgtk-3.so.0", EntryPoint = "gtk_icon_theme_lookup_by_gicon")]
        private static partial nint GtkIconThemeLookupByGIcon(nint theme, nint icon, int size, int flags);

        [LibraryImport("libgtk-3.so.0", EntryPoint = "gtk_icon_info_get_filename")]
        private static partial nint GtkIconInfoGetFilename(nint iconInfo);

        [LibraryImport("libgtk-3.so.0", EntryPoint = "gtk_icon_info_load_icon")]
        private static partial nint GtkIconInfoLoadIcon(nint iconInfo, ref nint error);

        [LibraryImport("libgio-2.0.so.0", EntryPoint = "g_file_new_for_path",
            StringMarshalling = StringMarshalling.Utf8)]
        private static partial nint GFileNewForPath(string path);

        [LibraryImport("libgio-2.0.so.0", EntryPoint = "g_file_query_info", StringMarshalling = StringMarshalling.Utf8)]
        private static partial nint GFileQueryInfo(nint file, string attributes, int flags, nint cancellable,
            ref nint error);

        [LibraryImport("libgio-2.0.so.0", EntryPoint = "g_file_info_get_icon")]
        private static partial nint GFileInfoGetIcon(nint info);

        [LibraryImport("libgio-2.0.so.0", EntryPoint = "g_content_type_guess",
            StringMarshalling = StringMarshalling.Utf8)]
        private static partial nint GContentTypeGuess(string path, nint data, nuint length, out int uncertain);

        [LibraryImport("libgio-2.0.so.0", EntryPoint = "g_content_type_get_icon")]
        private static partial nint GContentTypeGetIcon(nint contentType);

        [LibraryImport("libgobject-2.0.so.0", EntryPoint = "g_object_unref")]
        private static partial void GObjectUnref(nint value);

        [LibraryImport("libglib-2.0.so.0", EntryPoint = "g_free")]
        private static partial void GFree(nint value);

        [LibraryImport("libglib-2.0.so.0", EntryPoint = "g_error_free")]
        private static partial void GErrorFree(nint value);

        [LibraryImport("libgio-2.0.so.0", EntryPoint = "g_icon_serialize")]
        private static partial nint GIconSerialize(nint icon);

        [LibraryImport("libgio-2.0.so.0", EntryPoint = "g_icon_deserialize")]
        private static partial nint GIconDeserialize(nint variant);

        [LibraryImport("libglib-2.0.so.0", EntryPoint = "g_variant_unref")]
        private static partial void GVariantUnref(nint variant);

        [LibraryImport("libgdk_pixbuf-2.0.so.0", EntryPoint = "gdk_pixbuf_get_width")]
        private static partial int GdkPixbufGetWidth(nint pixbuf);

        [LibraryImport("libgdk_pixbuf-2.0.so.0", EntryPoint = "gdk_pixbuf_get_height")]
        private static partial int GdkPixbufGetHeight(nint pixbuf);

        [LibraryImport("libgdk_pixbuf-2.0.so.0", EntryPoint = "gdk_pixbuf_get_n_channels")]
        private static partial int GdkPixbufGetNChannels(nint pixbuf);

        [LibraryImport("libgdk_pixbuf-2.0.so.0", EntryPoint = "gdk_pixbuf_get_rowstride")]
        private static partial int GdkPixbufGetRowstride(nint pixbuf);

        [LibraryImport("libgdk_pixbuf-2.0.so.0", EntryPoint = "gdk_pixbuf_read_pixels")]
        private static partial nint GdkPixbufReadPixels(nint pixbuf);
    }
}
