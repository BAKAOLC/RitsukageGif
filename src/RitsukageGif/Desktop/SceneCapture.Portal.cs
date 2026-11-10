namespace RitsukageGif.Desktop
{
    internal sealed partial class SceneCapture
    {
        private void ValidatePortalSources(IReadOnlyList<PresetLayer> definitions)
        {
            if (!DesktopCapture.UsesSourceSelection) return;
            var windowSource = _portalSource?.Session.IsWindowSource ?? NativeCaptureSession.SourceIsWindow;
            foreach (var definition in definitions)
            {
                if (definition.Media is not null) continue;
                if (definition.Source == CaptureSourceKind.Window != windowSource)
                    throw new NotSupportedException(UiText.Get("PortalCaptureSourceMismatch"));
                if (!definition.Absolute || definition.Source == CaptureSourceKind.Window)
                {
                    var process = definition.Process ?? _preset.Process;
                    var title = definition.Process is null ? _preset.Title : definition.Title;
                    var handle = definition.FixedHandle != 0 ? definition.FixedHandle :
                        definition.Process is null ? _preset.FixedHandle : 0;
                    if (!windowSource || !Unspecified(process) || !Unspecified(title) || handle is not (0 or -1) ||
                        (_portalSource?.Session.GetAuthorizedWindows() ?? NativeCaptureSession.AuthorizedWindows())
                        .Count != 1)
                        throw new NotSupportedException(UiText.Get("PortalWindowMatchingUnavailable"));
                }
            }
        }

        private static bool Unspecified(string value)
        {
            return string.IsNullOrEmpty(value) || value == "*";
        }
    }
}
