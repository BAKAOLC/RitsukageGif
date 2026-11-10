using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    internal static partial class AnnotationScriptLibrary
    {
        private static void RegisterProperties(ScriptBindingRegistry bindings)
        {
            bindings.RegisterEnum("AnnotationArrowShape", Enum.GetValues<AnnotationArrowShape>()
                .ToDictionary(value => value.ToString(), value => value));
            bindings.RegisterEnum("AnnotationEndpointStyle", Enum.GetValues<AnnotationEndpointStyle>()
                .ToDictionary(value => value.ToString(), value => value));
            bindings.RegisterEnum("AnnotationGeometryShape", Enum.GetValues<AnnotationGeometryShape>()
                .ToDictionary(value => value.ToString(), value => value));
            bindings.RegisterEnum("AnnotationMagnifierLink", Enum.GetValues<AnnotationMagnifierLink>()
                .ToDictionary(value => value.ToString(), value => value));
            bindings.RegisterEnum("AnnotationMarkerBlend", Enum.GetValues<AnnotationMarkerBlend>()
                .ToDictionary(value => value.ToString(), value => value));
            bindings.RegisterEnum("AnnotationMarkerPath", Enum.GetValues<AnnotationMarkerPath>()
                .ToDictionary(value => value.ToString(), value => value));
            bindings.RegisterEnum("AnnotationMosaicMode", Enum.GetValues<AnnotationMosaicMode>()
                .ToDictionary(value => value.ToString(), value => value));
            bindings.RegisterEnum("AnnotationNumberFormat", Enum.GetValues<AnnotationNumberFormat>()
                .ToDictionary(value => value.ToString(), value => value));
            bindings.RegisterEnum("AnnotationNumberStyle", Enum.GetValues<AnnotationNumberStyle>()
                .ToDictionary(value => value.ToString(), value => value));
            bindings.RegisterEnum("AnnotationPixelPath", Enum.GetValues<AnnotationPixelPath>()
                .ToDictionary(value => value.ToString(), value => value));
            bindings.RegisterEnum("AnnotationStrokeStyle", Enum.GetValues<AnnotationStrokeStyle>()
                .ToDictionary(value => value.ToString(), value => value));
            bindings.RegisterObjectSchema("AnnotationProperties",
            [
                new("filled", "boolean", true),
                new("geometryShape", "AnnotationGeometryShape", true),
                new("strokeStyle", "AnnotationStrokeStyle", true),
                new("cornerRadius", "number", true),
                new("curved", "boolean", true),
                new("arrowShape", "AnnotationArrowShape", true),
                new("startEndpoint", "AnnotationEndpointStyle", true),
                new("endEndpoint", "AnnotationEndpointStyle", true),
                new("spotlightEllipse", "boolean", true),
                new("spotlightStroke", "boolean", true),
                new("bold", "boolean", true),
                new("italic", "boolean", true),
                new("strikeThrough", "boolean", true),
                new("textBackground", "string", true),
                new("textOutline", "string", true),
                new("textOutlineWidth", "number", true),
                new("wrapWidth", "number", true),
                new("textPadding", "number", true),
                new("opacity", "number", true),
                new("number", "number", true),
                new("tiled", "boolean", true),
                new("nineGrid", "boolean", true),
                new("anchor", "number", true),
                new("imageWidth", "number", true),
                new("blurRadius", "number", true),
                new("fontFamily", "string", true),
                new("magnification", "number", true),
                new("magnifierEllipse", "boolean", true),
                new("magnifierAntialias", "boolean", true),
                new("magnifierShadow", "boolean", true),
                new("magnifierShowAnnotations", "boolean", true),
                new("magnifierLink", "AnnotationMagnifierLink", true),
                new("markerPath", "AnnotationMarkerPath", true),
                new("markerBlend", "AnnotationMarkerBlend", true),
                new("numberStyle", "AnnotationNumberStyle", true),
                new("numberFormat", "AnnotationNumberFormat", true),
                new("numberCallout", "boolean", true),
                new("numberCaptionGuide", "boolean", true),
                new("numberCaptionScale", "number", true),
                new("numberCaptionOutline", "boolean", true),
                new("pixelPath", "AnnotationPixelPath", true),
                new("mosaicMode", "AnnotationMosaicMode", true),
                new("autoMosaicSynchronous", "boolean", true),
                new("inpaintingRadius", "number", true),
                new("textOutlineEnabled", "boolean", true),
                new("textBackgroundEnabled", "boolean", true),
                new("textBackgroundOpacity", "number", true),
                new("textBackgroundRadius", "number", true),
                new("watermarkText", "string", true),
                new("watermarkImagePath", "string?", true),
                new("watermarkSpacing", "number", true),
                new("watermarkAngle", "number", true),
                new("watermarkTimeFormat", "string", true),
            ]);
        }

        private static ScreenshotAnnotationProperties ReadProperties(object? value,
            ScreenshotAnnotationProperties defaults)
        {
            var properties = defaults with { };
            foreach (var (name, item) in Fields(value))
                switch (name)
                {
                    case "filled": properties.Filled = (bool)item!; break;
                    case "geometryShape": properties.GeometryShape = (AnnotationGeometryShape)item!; break;
                    case "strokeStyle": properties.StrokeStyle = (AnnotationStrokeStyle)item!; break;
                    case "cornerRadius": properties.CornerRadius = Number(item); break;
                    case "curved": properties.Curved = (bool)item!; break;
                    case "arrowShape": properties.ArrowShape = (AnnotationArrowShape)item!; break;
                    case "startEndpoint": properties.StartEndpoint = (AnnotationEndpointStyle)item!; break;
                    case "endEndpoint": properties.EndEndpoint = (AnnotationEndpointStyle)item!; break;
                    case "spotlightEllipse": properties.SpotlightEllipse = (bool)item!; break;
                    case "spotlightStroke": properties.SpotlightStroke = (bool)item!; break;
                    case "bold": properties.Bold = (bool)item!; break;
                    case "italic": properties.Italic = (bool)item!; break;
                    case "strikeThrough": properties.StrikeThrough = (bool)item!; break;
                    case "textBackground": properties.TextBackground = Color.Parse((string)item!); break;
                    case "textOutline": properties.TextOutline = Color.Parse((string)item!); break;
                    case "textOutlineWidth": properties.TextOutlineWidth = Number(item); break;
                    case "wrapWidth": properties.WrapWidth = Number(item); break;
                    case "textPadding": properties.TextPadding = Number(item); break;
                    case "opacity": properties.Opacity = Number(item); break;
                    case "number": properties.Number = Integer(item); break;
                    case "tiled": properties.Tiled = (bool)item!; break;
                    case "nineGrid": properties.NineGrid = (bool)item!; break;
                    case "anchor": properties.Anchor = Integer(item); break;
                    case "imageWidth": properties.ImageWidth = Number(item); break;
                    case "blurRadius": properties.BlurRadius = Integer(item); break;
                    case "fontFamily": properties.FontFamily = (string)item!; break;
                    case "magnification": properties.Magnification = Number(item); break;
                    case "magnifierEllipse": properties.MagnifierEllipse = (bool)item!; break;
                    case "magnifierAntialias": properties.MagnifierAntialias = (bool)item!; break;
                    case "magnifierShadow": properties.MagnifierShadow = (bool)item!; break;
                    case "magnifierShowAnnotations": properties.MagnifierShowAnnotations = (bool)item!; break;
                    case "magnifierLink": properties.MagnifierLink = (AnnotationMagnifierLink)item!; break;
                    case "markerPath": properties.MarkerPath = (AnnotationMarkerPath)item!; break;
                    case "markerBlend": properties.MarkerBlend = (AnnotationMarkerBlend)item!; break;
                    case "numberStyle": properties.NumberStyle = (AnnotationNumberStyle)item!; break;
                    case "numberFormat": properties.NumberFormat = (AnnotationNumberFormat)item!; break;
                    case "numberCallout": properties.NumberCallout = (bool)item!; break;
                    case "numberCaptionGuide": properties.NumberCaptionGuide = (bool)item!; break;
                    case "numberCaptionScale": properties.NumberCaptionScale = Number(item); break;
                    case "numberCaptionOutline": properties.NumberCaptionOutline = (bool)item!; break;
                    case "pixelPath": properties.PixelPath = (AnnotationPixelPath)item!; break;
                    case "mosaicMode": properties.MosaicMode = (AnnotationMosaicMode)item!; break;
                    case "autoMosaicSynchronous": properties.AutoMosaicSynchronous = (bool)item!; break;
                    case "inpaintingRadius": properties.InpaintingRadius = Integer(item); break;
                    case "textOutlineEnabled": properties.TextOutlineEnabled = (bool)item!; break;
                    case "textBackgroundEnabled": properties.TextBackgroundEnabled = (bool)item!; break;
                    case "textBackgroundOpacity": properties.TextBackgroundOpacity = Number(item); break;
                    case "textBackgroundRadius": properties.TextBackgroundRadius = Number(item); break;
                    case "watermarkText": properties.WatermarkText = (string)item!; break;
                    case "watermarkImagePath": properties.WatermarkImagePath = (string?)item; break;
                    case "watermarkSpacing": properties.WatermarkSpacing = Number(item); break;
                    case "watermarkAngle": properties.WatermarkAngle = Number(item); break;
                    case "watermarkTimeFormat": properties.WatermarkTimeFormat = (string)item!; break;
                    default: throw new ArgumentException("Unknown annotation property: " + name);
                }

            return properties;
        }
    }
}
