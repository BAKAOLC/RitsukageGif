using System.Runtime.Versioning;
using Microsoft.Win32;

namespace RitsukageGif.Desktop
{
    [SupportedOSPlatform("windows")]
    internal sealed class WindowsExplorerModernStore : IExplorerModernStore
    {
        private const string KeyPath = @"Software\RitsukageGif\ShellIntegration";

        public ExplorerModernEntry? Read()
        {
            using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
            return key is null
                ? null
                : new((ExplorerContextMenuMode)(key.GetValue("ContextMenuMode") as int? ?? 0),
                    key.GetValue("Caption") as string ?? "");
        }

        public void Write(ExplorerModernEntry? entry)
        {
            if (entry is null)
            {
                Registry.CurrentUser.DeleteSubKeyTree(KeyPath, false);
                return;
            }

            using var key = Registry.CurrentUser.CreateSubKey(KeyPath, true);
            key.SetValue("ContextMenuMode", (int)entry.Mode, RegistryValueKind.DWord);
            key.SetValue("Caption", entry.Caption, RegistryValueKind.String);
        }
    }
}
