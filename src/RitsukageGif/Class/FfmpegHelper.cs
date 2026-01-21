using System;
using System.IO;

namespace RitsukageGif.Class
{
    public static class FfmpegHelper
    {
        private static bool? _isAvailable;

        public static bool IsAvailable
        {
            get
            {
                if (_isAvailable.HasValue)
                    return _isAvailable.Value;

                _isAvailable = CheckFfmpegAvailability();
                return _isAvailable.Value;
            }
        }

        public static string? FfmpegPath { get; private set; }

        private static bool CheckFfmpegAvailability()
        {
            try
            {
                var appDir = AppDomain.CurrentDomain.BaseDirectory;
                var localFfmpeg = Path.Combine(appDir, "ffmpeg.exe");

                if (File.Exists(localFfmpeg))
                {
                    FfmpegPath = localFfmpeg;
                    return true;
                }

                var pathFfmpeg = FindFfmpegInPath();
                if (string.IsNullOrEmpty(pathFfmpeg)) return false;
                FfmpegPath = pathFfmpeg;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static string? FindFfmpegInPath()
        {
            var pathEnv = Environment.GetEnvironmentVariable("PATH");
            if (string.IsNullOrEmpty(pathEnv))
                return null;

            var paths = pathEnv.Split(';');
            foreach (var path in paths)
                try
                {
                    var ffmpegPath = Path.Combine(path, "ffmpeg.exe");
                    if (File.Exists(ffmpegPath))
                        return ffmpegPath;
                }
                catch
                {
                    // Ignore invalid paths
                }

            return null;
        }

        public static void Reset()
        {
            _isAvailable = null;
            FfmpegPath = null;
        }
    }
}