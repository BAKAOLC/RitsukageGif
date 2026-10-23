namespace RitsukageGif.Scripting
{
    internal sealed class PresetRuntimeClock
    {
        private double _time = double.NaN;

        public double Now
        {
            get
            {
                var time = Volatile.Read(ref _time);
                return double.IsNaN(time) ? PresetMotion.Now : time;
            }
        }

        public void SetTime(double time)
        {
            if (!double.IsFinite(time)) throw new ArgumentOutOfRangeException(nameof(time));
            Volatile.Write(ref _time, time);
        }
    }
}
