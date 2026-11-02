using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    internal static class AnnotationStroke
    {
        public static IDashStyle? Dashes(AnnotationStrokeStyle style)
        {
            return style switch
            {
                AnnotationStrokeStyle.Dash => DashStyle.Dash,
                AnnotationStrokeStyle.Dot => DashStyle.Dot,
                AnnotationStrokeStyle.DashDot => DashStyle.DashDot,
                AnnotationStrokeStyle.DashDotDot => DashStyle.DashDotDot,
                _ => null,
            };
        }
    }
}
