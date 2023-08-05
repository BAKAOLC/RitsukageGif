using RitsukageGif.Class;

namespace RitsukageGif.CaptureProvider.RecordFrame
{
    public class RecordInfo : NotifyPropertyChanged
    {
        private bool _completed;

        private int _frames;
        private string _path;

        private int _processedFrames;

        public string Path
        {
            get => _path;
            set => Set(ref _path, value);
        }

        public int Frames
        {
            get => _frames;
            set => Set(ref _frames, value);
        }

        public int ProcessedFrames
        {
            get => _processedFrames;
            set => Set(ref _processedFrames, value);
        }

        public bool Completed
        {
            get => _completed;
            set => Set(ref _completed, value);
        }
    }
}