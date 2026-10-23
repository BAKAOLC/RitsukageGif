namespace RitsukageGif.Core
{
    public sealed class GifTiming(int fps = 50)
    {
        private readonly int _fps = Math.Clamp(fps, 1, 50);
        private long _centiseconds;
        private long _milliseconds;

        public int Next(int milliseconds)
        {
            _milliseconds += Math.Max(1, milliseconds);
            // Select the source interval containing each fixed sampling instant.
            // Quantizing absolute grid boundaries prevents drift at rates such as 30 fps.
            var samples = (_milliseconds * _fps + 999) / 1000;
            var target = samples * 100 / _fps;
            var result = target - _centiseconds;
            _centiseconds = target;
            return checked((int)result);
        }
    }
}
