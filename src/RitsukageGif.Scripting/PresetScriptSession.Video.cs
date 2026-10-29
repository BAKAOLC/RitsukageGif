namespace RitsukageGif.Scripting
{
    public sealed partial class PresetScriptSession
    {
        public void PrepareVideoRecording()
        {
            InitializePlayback(PresetMotion.Now);
            _recordingEnded = false;
        }

        public void CompleteVideoRecording()
        {
            _recordingEnded = true;
        }
    }
}
