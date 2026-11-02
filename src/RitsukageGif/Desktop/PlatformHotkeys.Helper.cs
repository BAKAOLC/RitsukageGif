using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace RitsukageGif.Desktop
{
    internal static partial class PlatformHotkeys
    {
        private static async Task RunPortableHotkeysAsync((uint Modifiers, uint Key)[] keys, Action<int> pressed,
            Action<string> error, Action registered, Action<int, string?>? registrationChanged,
            Action<int, string>? triggerChanged, string helperName, bool portal,
            IReadOnlyList<string>? descriptions, CancellationToken token)
        {
            if (portal) DesktopApplicationRegistration.EnsureRegistered();
            var executable = Path.Combine(AppContext.BaseDirectory, helperName);
            if (!File.Exists(executable))
                throw new FileNotFoundException("The global shortcuts helper is missing.", executable);
            var start = new ProcessStartInfo(executable)
            {
                UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true,
                RedirectStandardError = true, CreateNoWindow = true,
            };
            var ids = new Dictionary<string, int>(StringComparer.Ordinal);
            for (var index = 0; index < keys.Length; index++)
            {
                var key = keys[index];
                if (key.Key == 0) continue;
                var id = string.Create(CultureInfo.InvariantCulture, $"key_{key.Modifiers:x}_{key.Key:x}");
                ids.Add(id, index);
                var trigger = PortalTrigger(key);
                start.ArgumentList.Add(id);
                start.ArgumentList.Add(trigger);
                start.ArgumentList.Add(descriptions is null ? "RitsukageGif: " + trigger : descriptions[index]);
            }

            using var process = Process.Start(start) ??
                                throw new IOException("The global shortcuts helper could not start.");
            var diagnostic = process.StandardError.ReadToEndAsync(token);
            await using var stop = token.Register(() =>
            {
                try
                {
                    // ReSharper disable once AccessToDisposedClosure
                    process.StandardInput.Close();
                }
                catch (IOException)
                {
                }
                catch (InvalidOperationException)
                {
                }
            });
            var states = new bool[keys.Length];
            var held = new HashSet<int>();
            try
            {
                while (await process.StandardOutput.ReadLineAsync(token).ConfigureAwait(false) is { } line)
                {
                    if (token.IsCancellationRequested) break;
                    var parts = line.Split('\t', 3);
                    if (parts[0] == "R")
                    {
                        if (ids.Values.All(keyIndex => states[keyIndex])) registered();
                        continue;
                    }

                    if (parts.Length < 2 || !ids.TryGetValue(parts[1], out var index))
                        throw new IOException("The global shortcuts helper returned an invalid shortcut identifier.");
                    switch (parts[0])
                    {
                        case "B" when parts.Length == 3:
                            var trigger = Encoding.UTF8.GetString(Convert.FromBase64String(parts[2]));
                            if (string.IsNullOrWhiteSpace(trigger))
                                throw new IOException(
                                    "The global shortcuts helper returned an empty shortcut trigger.");
                            states[index] = true;
                            triggerChanged?.Invoke(index, trigger);
                            registrationChanged?.Invoke(index, null);
                            break;
                        case "F":
                            states[index] = false;
                            held.Remove(index);
                            var failure = parts.Length == 3
                                ? Encoding.UTF8.GetString(Convert.FromBase64String(parts[2]))
                                : portal
                                    ? "The desktop portal did not bind this shortcut."
                                    : "The global shortcut is unavailable or already registered.";
                            registrationChanged?.Invoke(index, failure);
                            error(failure);
                            break;
                        case "A":
                            if (states[index] && held.Add(index)) pressed(index);
                            break;
                        case "D":
                            held.Remove(index);
                            break;
                        default:
                            throw new IOException("The global shortcuts helper returned an invalid response.");
                    }
                }

                if (token.IsCancellationRequested) return;
                await process.WaitForExitAsync(token).ConfigureAwait(false);
                var details = (await diagnostic.ConfigureAwait(false)).Trim();
                var message = details.Length == 0 ? "The global shortcut session closed." : details;
                foreach (var index in ids.Values) registrationChanged?.Invoke(index, message);
                error(message);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
            }
            finally
            {
                process.StandardInput.Close();
                await StopPortalHelperAsync(process).ConfigureAwait(false);
            }
        }

        private static async Task StopPortalHelperAsync(Process process)
        {
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));
                try
                {
                    await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    if (!process.HasExited) process.Kill();
                    await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
                }
            }
            catch (InvalidOperationException)
            {
            }
            catch (Win32Exception)
            {
            }
        }
    }
}
