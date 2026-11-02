using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using Avalonia.Controls.Platform;
using Avalonia.Threading;

namespace Avalonia.Native
{
    internal sealed class MacOsMountedVolumeInfoListener : IDisposable
    {
        private readonly IDisposable _disposable;
        private bool _beenDisposed;
        private readonly ObservableCollection<MountedVolumeInfo> _mountedDrives;

        public MacOsMountedVolumeInfoListener(ObservableCollection<MountedVolumeInfo> mountedDrives)
        {
            _mountedDrives = mountedDrives;

            _disposable = DispatcherTimer.Run(Poll, TimeSpan.FromSeconds(1));

            Poll();
        }

        public void Dispose()
        {
            Dispose(true);
        }

        private bool Poll()
        {
            var mountVolInfos = Directory.GetDirectories("/Volumes/")
                .Select(p => new MountedVolumeInfo
                {
                    VolumeLabel = Path.GetFileName(p),
                    VolumePath = p,
                    VolumeSizeBytes = 0,
                })
                .ToArray();

            if (_mountedDrives.SequenceEqual(mountVolInfos))
                return true;
            _mountedDrives.Clear();

            foreach (var i in mountVolInfos)
                _mountedDrives.Add(i);
            return true;
        }

        private void Dispose(bool disposing)
        {
            if (!_beenDisposed)
            {
                if (disposing) _disposable.Dispose();

                _beenDisposed = true;
            }
        }
    }

    internal class MacOsMountedVolumeInfoProvider : IMountedVolumeInfoProvider
    {
        public IDisposable Listen(ObservableCollection<MountedVolumeInfo> mountedDrives)
        {
            return new MacOsMountedVolumeInfoListener(mountedDrives);
        }
    }
}
