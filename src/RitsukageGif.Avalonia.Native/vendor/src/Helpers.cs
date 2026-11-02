using System.Diagnostics.CodeAnalysis;
using Avalonia.Native.Interop;

namespace Avalonia.Native
{
    internal static class Helpers
    {
        extension(AvnPoint pt)
        {
            public Point ToAvaloniaPoint()
            {
                return new(pt.X, pt.Y);
            }

            public PixelPoint ToAvaloniaPixelPoint()
            {
                return new((int)pt.X, (int)pt.Y);
            }
        }

        extension(Point pt)
        {
            public AvnPoint ToAvnPoint()
            {
                return new() { X = pt.X, Y = pt.Y };
            }
        }

        extension(PixelPoint pt)
        {
            public AvnPoint ToAvnPoint()
            {
                return new() { X = pt.X, Y = pt.Y };
            }
        }

        extension(Rect rect)
        {
            public AvnRect ToAvnRect()
            {
                return new() { X = rect.X, Y = rect.Y, Height = rect.Height, Width = rect.Width };
            }
        }

        extension(Size size)
        {
            public AvnSize ToAvnSize()
            {
                return new() { Height = size.Height, Width = size.Width };
            }
        }

        extension(string? s)
        {
            [return: NotNullIfNotNull(nameof(s))]
            public IAvnString? ToAvnString()
            {
                return s != null ? new AvnString(s) : null;
            }
        }

        extension(AvnRect rect)
        {
            public PixelRect ToAvaloniaPixelRect()
            {
                return new((int)rect.X, (int)rect.Y, (int)rect.Width, (int)rect.Height);
            }
        }
    }
}
