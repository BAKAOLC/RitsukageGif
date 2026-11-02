using System.Runtime.Versioning;

namespace RitsukageGif.Desktop
{
    internal sealed class ExplorerPlatformIntegration(
        IExplorerShellStore classicStore,
        IExplorerModernStore modernStore,
        Func<string, bool> useModern)
    {
        private readonly ExplorerShellIntegration _classic = new(classicStore);

        [SupportedOSPlatform("windows")]
        internal static ExplorerPlatformIntegration CreateWindows()
        {
            return new(new WindowsExplorerShellStore(),
                new WindowsExplorerModernStore(), executable => WindowsPackageIdentity.HasIdentity() &&
                                                                File.Exists(Path.Combine(
                                                                    Path.GetDirectoryName(executable) ?? "",
                                                                    "RitsukageGif.ExplorerShell.dll")));
        }

        internal void Apply(ExplorerContextMenuMode mode, string executable, string caption, Action persist)
        {
            var original = modernStore.Read();
            var modern = useModern(executable);
            var desired = modern ? mode == ExplorerContextMenuMode.Disabled && original is null
                    ? null
                    : new ExplorerModernEntry(mode, caption) :
                original is null ? null : new(ExplorerContextMenuMode.Disabled, caption);
            var changed = original != desired;
            try
            {
                if (changed) modernStore.Write(desired);
                _classic.Apply(modern ? ExplorerContextMenuMode.Disabled : mode, executable, caption, persist);
            }
            catch (Exception failure)
            {
                if (changed)
                    try
                    {
                        modernStore.Write(original);
                    }
                    catch (Exception rollback)
                    {
                        throw new AggregateException("Explorer integration rollback failed.", failure, rollback);
                    }

                throw;
            }
        }
    }
}
