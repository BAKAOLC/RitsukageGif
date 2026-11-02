using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    internal static class SettingsField
    {
        private static readonly AppSettings Defaults = new()
        {
            Annotation = new AnnotationPreferences().Normalize(),
            Appearance = new AppearancePreferences().Normalize(),
            CapturePreferences = new CapturePreferences().Normalize(),
            PinnedImages = new PinnedImagePreferences().Normalize(),
            ScreenshotSave = new ScreenshotSavePreferences().Normalize(),
            ScreenshotClipboard = new ScreenshotClipboardPreferences().Normalize(),
            System = new SystemPreferences().Normalize(),
            Translation = new TranslationPreferences().Normalize(),
            Recognition = new RecognitionPreferences().Normalize(),
            Network = new NetworkPreferences().Normalize(),
        };

        private static readonly AttachedProperty<ISettingsDefaultDescriptor?> DescriptorProperty =
            AvaloniaProperty.RegisterAttached<Control, ISettingsDefaultDescriptor?>("SettingsDefaultDescriptor",
                typeof(SettingsField));

        internal static TControl Track<TControl, TValue>(TControl control, Control label,
            Func<AppSettings, TValue> getter, Func<TValue, TValue, bool>? equals = null) where TControl : Control
        {
            control.SetValue(DescriptorProperty,
                new SettingsDefaultDescriptor<TValue>(label, getter, getter(Defaults), equals));
            return control;
        }

        internal static void Refresh(Control control, AppSettings settings)
        {
            if (control.GetValue(DescriptorProperty) is not { } descriptor) return;
            var changed = !settings.NonDefaultConfigItemsFontNotBold && descriptor.IsChanged(settings);
            switch (descriptor.Label)
            {
                case TextBlock label:
                    if (changed) label.FontWeight = FontWeight.Bold;
                    else label.ClearValue(TextBlock.FontWeightProperty);
                    break;
                case TemplatedControl label:
                    if (changed) label.FontWeight = FontWeight.Bold;
                    else label.ClearValue(TemplatedControl.FontWeightProperty);
                    break;
            }
        }

        internal static bool SameColors(IReadOnlyList<string> left, IReadOnlyList<string> right)
        {
            if (left.Count != right.Count) return false;
            for (var index = 0; index < left.Count; index++)
                if (!Color.TryParse(left[index], out var leftColor) ||
                    !Color.TryParse(right[index], out var rightColor) || leftColor != rightColor)
                    return false;
            return true;
        }

        internal static bool SameColumns(List<List<ScreenshotTool>> left, List<List<ScreenshotTool>> right)
        {
            if (left.Count != right.Count) return false;
            for (var index = 0; index < left.Count; index++)
                if (!left[index].SequenceEqual(right[index]))
                    return false;
            return true;
        }
    }
}
