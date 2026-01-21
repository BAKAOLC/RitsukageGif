using RitsukageGif.Class;

namespace RitsukageGif.CaptureProvider.RecordFrame
{
    public class RecordInfo : NotifyPropertyChanged
    {
        public string Path
        {
            get;
            set => Set(ref field, value);
        }

        public int Frames
        {
            get;
            set => Set(ref field, value);
        }

        public int ProcessedFrames
        {
            get;
            set => Set(ref field, value);
        }

        public bool Completed
        {
            get;
            set => Set(ref field, value);
        }
    }
}