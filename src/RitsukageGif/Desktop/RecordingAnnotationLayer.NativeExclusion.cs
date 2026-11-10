using Avalonia.Controls;
using Avalonia.Threading;

namespace RitsukageGif.Desktop
{
    internal sealed partial class RecordingAnnotationLayer
    {
        internal event Action<LinuxCaptureExclusionResult> NativeExclusionFailed
        {
            add => _overlay.NativeExclusionFailed += value;
            remove => _overlay.NativeExclusionFailed -= value;
        }

        internal bool OwnsNativeExclusionWindow(TopLevel? window)
        {
            return _overlay.OwnsNativeExclusionWindow(window);
        }

        internal async Task BindHostedSourceExclusionAsync(LinuxCaptureExclusionProof proof,
            CancellationToken token)
        {
            await Dispatcher.UIThread.InvokeAsync(() => _overlay.BindHostedSourceExclusionAsync(proof, token));
        }

        internal async Task<LinuxCaptureExclusionResult> PrepareNativeExclusionAsync(LinuxCaptureExclusionProof proof,
            CancellationToken token)
        {
            return await Dispatcher.UIThread.InvokeAsync(() => _overlay.PrepareNativeExclusionAsync(proof, token));
        }

        internal async ValueTask ReleaseNativeExclusionAsync()
        {
            if (!OperatingSystem.IsLinux()) return;
            await Dispatcher.UIThread.InvokeAsync(() => _overlay.ReleaseNativeExclusionAsync().AsTask());
            var logger = LogHub.GetLogger("Desktop.RecordingAnnotations");
            if (_overlay.NativePreparationFailure is { } error)
                logger.ZLogWarning(error, $"Native recording annotation preparation failed before release.");
            if (_overlay.NativeReleaseFailure != LinuxCaptureExclusionFailure.None)
                logger.ZLogWarning(
                    $"Native recording annotation exclusion release failed. Detail={_overlay.NativeReleaseFailure}");
        }

        private void PrepareNativeEditor(Window editor)
        {
            if (!_overlay.NeedsNativeWindowPreparation) return;
            var title = editor.Title!;
            var content = editor.Content;
            var marker = LinuxCaptureExclusionLease.CreateWindowMarker();
            editor.Title = marker;
            editor.Content = null;
            editor.Opacity = 0;
            editor.Opened += (_, _) => _ = ExposeNativeEditorAsync(editor, marker, title, content);
        }

        private async Task ExposeNativeEditorAsync(Window editor, string marker, string title, object? content)
        {
            try
            {
                var result = await _overlay.PrepareAuxiliaryWindowAsync(editor, marker);
                if (result.Capability == LinuxCaptureExclusionCapability.Unavailable || _disposed || !editor.IsVisible)
                {
                    editor.Close(null);
                    return;
                }

                editor.Title = title;
                editor.Content = content;
                editor.Opacity = 1;
            }
            catch (Exception error)
            {
                editor.Close(null);
                if (!_disposed) Session.SetPrivacyNotice(error.Message);
            }
        }
    }
}
