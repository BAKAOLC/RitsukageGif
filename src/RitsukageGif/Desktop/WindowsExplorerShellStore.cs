using System.Runtime.Versioning;
using Microsoft.Win32;

namespace RitsukageGif.Desktop
{
    [SupportedOSPlatform("windows")]
    internal sealed class WindowsExplorerShellStore : IExplorerShellStore
    {
        public ExplorerShellEntry? Read(string path)
        {
            using var key = Registry.CurrentUser.OpenSubKey(path);
            if (key is null) return null;
            using var command = key.OpenSubKey("command");
            return new(key.GetValue("RitsukageGifOwner") as string ?? "",
                key.GetValue(null) as string ?? "", command?.GetValue(null) as string ?? "",
                key.GetValue("Icon") as string ?? "");
        }

        public void Write(string path, ExplorerShellEntry entry)
        {
            using var key = Registry.CurrentUser.CreateSubKey(path, true);
            key.SetValue("RitsukageGifOwner", entry.Owner, RegistryValueKind.String);
            key.SetValue(null, entry.Caption, RegistryValueKind.String);
            key.SetValue("Icon", entry.Icon, RegistryValueKind.String);
            key.SetValue("MultiSelectModel", "Player", RegistryValueKind.String);
            using var command = key.CreateSubKey("command", true);
            command.SetValue(null, entry.Command, RegistryValueKind.String);
        }

        public void Remove(string path)
        {
            Registry.CurrentUser.DeleteSubKeyTree(path, false);
        }
    }
}
