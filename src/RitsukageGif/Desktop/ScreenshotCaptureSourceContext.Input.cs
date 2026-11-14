namespace RitsukageGif.Desktop
{
    // ReSharper disable once ClassCannotBeInstantiated
    internal sealed partial class ScreenshotCaptureSourceContext
    {
        private RecordingInputGeometry[] _inputGeometry = [];

        internal IReadOnlyList<RecordingInputGeometry> GetInputGeometry(RecordingFrameObservation observation,
            double cursorX, double cursorY)
        {
            if (RawSource is SceneCapture scene) return scene.GetInputGeometry(cursorX, cursorY);
            var region = RawSource is FollowingCapture following ? following.CurrentRegion : Selection.Region;
            var geometry = new RecordingInputGeometry(region, new(0, 0, observation.Width, observation.Height));
            if (_inputGeometry.Length != 1 || _inputGeometry[0] != geometry) _inputGeometry = [geometry];
            return _inputGeometry;
        }
    }
}
