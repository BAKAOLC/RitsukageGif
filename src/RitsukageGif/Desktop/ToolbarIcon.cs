using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Path = Avalonia.Controls.Shapes.Path;

namespace RitsukageGif.Desktop
{
    internal static class ToolbarIcon
    {
        public static string? TryGeometry(string key)
        {
            return FluentToolbarIcon.Geometry(key);
        }

        public static Control Create(Button button, string geometry)
        {
            if (FluentToolbarIcon.IsFilled(geometry))
                return new Path
                {
                    Data = Avalonia.Media.Geometry.Parse(geometry), Width = 16, Height = 16, Stretch = Stretch.Uniform,
                    VerticalAlignment = VerticalAlignment.Center,
                    [!Shape.FillProperty] = button[!TemplatedControl.ForegroundProperty],
                };
            return new Path
            {
                Data = Avalonia.Media.Geometry.Parse(geometry), Width = 16, Height = 16, Stretch = Stretch.Uniform,
                StrokeThickness = 1.6, VerticalAlignment = VerticalAlignment.Center,
                [!Shape.StrokeProperty] =
                    button[!TemplatedControl.ForegroundProperty],
            };
        }

        public static Control CreateInViewport(Button button, string geometry)
        {
            return new Viewbox
            {
                Width = 16, Height = 16, Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                Child = new Canvas
                {
                    Width = 24, Height = 24,
                    Children =
                    {
                        new Path
                        {
                            Width = 24, Height = 24, Data = Avalonia.Media.Geometry.Parse(geometry),
                            Stretch = Stretch.None,
                            [!Shape.FillProperty] = button[!TemplatedControl.ForegroundProperty],
                        },
                    },
                },
            };
        }

        public static string Geometry(string key)
        {
            return key switch
            {
                "EditorNew" => "M3,2 L12,2 19,9 19,22 3,22 Z M12,2 L12,9 19,9",
                "EditorOpen" => "M2,5 L8,5 10,8 22,8 18,21 2,21 Z",
                "ImageSave" or "ImageSaveAs" or "AnimationExport" =>
                    "M3,3 L18,3 21,6 21,21 3,21 Z M7,3 L7,9 17,9 17,3 M7,21 L7,14 17,14 17,21",
                "AnimationUndo" => "M8,3 L2,9 8,15 M2,9 L14,9 Q23,9 20,21",
                "AnimationRedo" => "M16,3 L22,9 16,15 M22,9 L10,9 Q1,9 4,21",
                "AnimationAddImage" => "M2,3 L22,3 22,21 2,21 Z M3,18 L8,11 13,16 18,9 22,15 M6,7 L8,7 8,9 6,9 Z",
                "AnimationAddText" => "M3,4 L21,4 M12,4 L12,21 M7,21 L17,21",
                "AnimationSelect" => "M4,2 L4,20 9,15 13,22 17,20 13,13 21,13 Z",
                "AnimationBrush" => "M3,21 L6,14 18,2 22,6 10,18 Z M6,14 L10,18",
                "AnimationEraser" => "M3,15 L14,3 22,11 12,21 8,21 Z M8,10 L17,17",
                "AnimationLine" => "M3,21 L21,3",
                "AnimationArrow" => "M3,21 L21,3 M10,3 L21,3 21,14",
                "ImageCopy" => "M2,2 L17,2 17,7 M2,2 L2,17 7,17 M7,7 L22,7 22,22 7,22 Z",
                "AnimationRectangle" => "M3,4 L21,4 21,20 3,20 Z",
                "AnimationEllipse" => "M12,3 C24,3 24,21 12,21 C0,21 0,3 12,3 Z",
                "AnimationSelection" =>
                    "M3,8 L3,3 8,3 M11,3 L14,3 M17,3 L21,3 21,8 M21,11 L21,14 M21,17 L21,21 17,21 M14,21 L11,21 M8,21 L3,21 3,17 M3,14 L3,11",
                "AnimationFit" => "M3,8 L3,3 8,3 M16,3 L21,3 21,8 M21,16 L21,21 16,21 M8,21 L3,21 3,16",
                "AnimationActualSize" => "M2,6 L5,3 5,21 M10,8 L10,9 M10,15 L10,16 M15,6 L18,3 18,21",
                "AnimationZoomOut" => "M10,2 A8,8 0 1 0 10,18 A8,8 0 1 0 10,2 M16,16 L23,23 M5,10 L15,10",
                "AnimationZoomIn" => "M10,2 A8,8 0 1 0 10,18 A8,8 0 1 0 10,2 M16,16 L23,23 M5,10 L15,10 M10,5 L10,15",
                "AnimationPan" =>
                    "M12,1 L8,5 M12,1 L16,5 M12,1 L12,23 M12,23 L8,19 M12,23 L16,19 M1,12 L23,12 M1,12 L5,8 M1,12 L5,16 M23,12 L19,8 M23,12 L19,16",
                "AnimationPropertiesToggle" =>
                    "M2,3 L22,3 22,21 2,21 Z M15,3 L15,21 M17,7 L20,7 M17,11 L20,11 M17,15 L20,15",
                "AnimationAddFrame" => "M2,3 L18,3 18,17 2,17 Z M15,12 L15,24 M9,18 L21,18",
                "AnimationImport" => "M3,12 L3,21 21,21 21,12 M12,2 L12,16 M7,11 L12,16 17,11",
                "AnimationDuplicate" => "M2,2 L17,2 17,7 M2,2 L2,17 7,17 M7,7 L22,7 22,22 7,22 Z",
                "AnimationDelete" => "M4,5 L20,5 M8,5 L8,2 16,2 16,5 M6,5 L7,22 17,22 18,5 M10,9 L10,18 M14,9 L14,18",
                "AnimationPrevious" => "M10,3 L2,12 10,21 M2,12 L22,12",
                "AnimationNext" => "M14,3 L22,12 14,21 M2,12 L22,12",
                "AnimationTiming" => "M12,2 A10,10 0 1 0 12,22 A10,10 0 1 0 12,2 M12,5 L12,12 17,15",
                "AnimationResize" =>
                    "M3,9 L3,3 9,3 M15,21 L21,21 21,15 M3,3 L10,10 M21,21 L14,14 M3,14 L3,21 10,21 M14,3 L21,3 21,10",
                "AnimationCrop" => "M6,1 L6,18 23,18 M1,6 L18,6 18,23",
                "AnimationRotate" => "M18,3 L23,8 16,9 M22,8 A10,10 0 1 0 20,19 M7,8 L15,8 15,16 7,16 Z",
                "AnimationFlipHorizontal" => "M12,1 L12,23 M2,20 L9,5 9,20 Z M22,20 L15,5 15,20 Z",
                "AnimationFlipVertical" => "M1,12 L23,12 M4,2 L19,9 4,9 Z M4,22 L19,15 4,15 Z",
                "AnimationEffects" => "M3,20 L16,7 20,11 7,24 Z M17,1 L17,5 M22,4 L20,6 M23,10 L21,10 M11,3 L13,5",
                "AnimationBlur" =>
                    "M5,4 A1,1 0 1 0 5,6 A1,1 0 1 0 5,4 M12,3 A2,2 0 1 0 12,7 A2,2 0 1 0 12,3 M19,4 A1,1 0 1 0 19,6 A1,1 0 1 0 19,4 M5,10 A2,2 0 1 0 5,14 A2,2 0 1 0 5,10 M12,9 A3,3 0 1 0 12,15 A3,3 0 1 0 12,9 M19,10 A2,2 0 1 0 19,14 A2,2 0 1 0 19,10 M5,18 A1,1 0 1 0 5,20 A1,1 0 1 0 5,18 M12,17 A2,2 0 1 0 12,21 A2,2 0 1 0 12,17 M19,18 A1,1 0 1 0 19,20 A1,1 0 1 0 19,18",
                "ScreenshotArrow" => Geometry("AnimationArrow"),
                "ScreenshotCopy" => Geometry("ImageCopy"),
                "ScreenshotSave" => Geometry("ImageSave"),
                "ScreenshotPin" => "M8,3 L19,3 M10,3 L10,9 7,14 20,14 17,9 17,3 M13,14 L13,23",
                "ScreenshotApplyText" => "M3,12 L9,18 21,5",
                "AnimationText" => Geometry("AnimationAddText"),
                "ScreenshotHighlight" => "M3,17 L13,3 21,9 11,21 Z M3,22 L21,22 M6,14 L14,20",
                "AnimationMosaic" =>
                    "M3,3 L9,3 9,9 3,9 Z M15,3 L21,3 21,9 15,9 Z M9,9 L15,9 15,15 9,15 Z M3,15 L9,15 9,21 3,21 Z M15,15 L21,15 21,21 15,21 Z",
                "ScreenshotScrollVertical" =>
                    "M12,2 L12,22 M7,7 L12,2 17,7 M7,17 L12,22 17,17 M3,10 L3,14 M21,10 L21,14",
                "ScreenshotScrollHorizontal" =>
                    "M2,12 L22,12 M7,7 L2,12 7,17 M17,7 L22,12 17,17 M10,3 L14,3 M10,21 L14,21",
                _ => throw new ArgumentOutOfRangeException(nameof(key), key, null),
            };
        }
    }
}
