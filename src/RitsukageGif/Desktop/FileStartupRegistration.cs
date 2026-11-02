using System.Text;

namespace RitsukageGif.Desktop
{
    internal abstract class FileStartupRegistration(string path) : IStartupRegistration
    {
        private string EntryPath { get; } = path;

        public virtual string? Read()
        {
            return File.Exists(EntryPath) ? File.ReadAllText(EntryPath) : null;
        }

        public virtual void Write(string? command)
        {
            if (command is null)
            {
                File.Delete(EntryPath);
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(EntryPath)!);
            var temporary = EntryPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporary, command, new UTF8Encoding(false));
                if (!OperatingSystem.IsWindows())
                    File.SetUnixFileMode(temporary, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                File.Move(temporary, EntryPath, true);
            }
            finally
            {
                File.Delete(temporary);
            }
        }

        public abstract string CreateCommand(string executable);
        public abstract bool IsEnabled(string executable);
    }
}
