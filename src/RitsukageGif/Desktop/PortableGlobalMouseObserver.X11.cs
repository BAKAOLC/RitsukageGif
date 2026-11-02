using System.Buffers.Binary;
using System.ComponentModel;
using System.Diagnostics;
using Avalonia.Input;

namespace RitsukageGif.Desktop
{
    internal static partial class PortableGlobalMouseObserver
    {
        private static void RunX11(Action<uint, GlobalMouseHookData, KeyModifiers> input, Action ready,
            CancellationToken cancellationToken)
        {
            var start = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "RitsukageGif.MouseObserver"))
            {
                UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true,
                RedirectStandardError = true, CreateNoWindow = true,
            };
            using var process = Process.Start(start) ??
                                throw new IOException("The X11 mouse observation helper could not be started.");
            var errors = process.StandardError.ReadToEndAsync(CancellationToken.None);
            using var cancellation = cancellationToken.Register(() =>
            {
                // ReSharper disable once AccessToDisposedClosure
                StopMouseHelper(process);
                // ReSharper disable once AccessToDisposedClosure
                _ = Task.Delay(1000, CancellationToken.None).ContinueWith(
                    static (_, state) => KillMouseHelper((Process)state!), process,
                    CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            });
            try
            {
                Span<byte> packet = stackalloc byte[24];
                var stream = process.StandardOutput.BaseStream;
                if (!ReadMousePacket(stream, packet[..8], cancellationToken) ||
                    !packet[..8].SequenceEqual("RGMOUSE1"u8))
                    throw MouseHelperFailure(process, errors);
                cancellationToken.ThrowIfCancellationRequested();
                ready();
                while (ReadMousePacket(stream, packet, cancellationToken))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var message = BinaryPrimitives.ReadUInt32LittleEndian(packet);
                    input(message, new()
                    {
                        X = BinaryPrimitives.ReadInt32LittleEndian(packet[4..]),
                        Y = BinaryPrimitives.ReadInt32LittleEndian(packet[8..]),
                        MouseData = BinaryPrimitives.ReadUInt32LittleEndian(packet[12..]),
                        Time = BinaryPrimitives.ReadUInt32LittleEndian(packet[20..]),
                    }, X11Modifiers(BinaryPrimitives.ReadUInt32LittleEndian(packet[16..])));
                }

                cancellationToken.ThrowIfCancellationRequested();
                throw MouseHelperFailure(process, errors);
            }
            finally
            {
                StopMouseHelper(process);
                if (!process.WaitForExit(1000))
                {
                    process.Kill(true);
                    process.WaitForExit();
                }

                input(0x001F, default, KeyModifiers.None);
            }
        }

        private static bool ReadMousePacket(Stream stream, Span<byte> packet, CancellationToken cancellationToken)
        {
            var offset = 0;
            while (offset < packet.Length)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var count = stream.Read(packet[offset..]);
                if (count == 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (offset != 0) throw new IOException("The X11 mouse helper returned an incomplete event.");
                    return false;
                }

                offset += count;
            }

            return true;
        }

        private static IOException MouseHelperFailure(Process process, Task<string> errors)
        {
            StopMouseHelper(process);
            if (!process.WaitForExit(1000))
            {
                process.Kill(true);
                process.WaitForExit();
            }

            var detail = errors.GetAwaiter().GetResult().Trim();
            return new(string.IsNullOrEmpty(detail)
                ? $"X11 global mouse observation stopped unexpectedly (exit {process.ExitCode})."
                : detail);
        }

        private static void StopMouseHelper(Process process)
        {
            try
            {
                process.StandardInput.Close();
            }
            catch (InvalidOperationException)
            {
            }
            catch (IOException)
            {
            }
        }

        private static void KillMouseHelper(Process process)
        {
            try
            {
                if (!process.HasExited) process.Kill(true);
            }
            catch (InvalidOperationException)
            {
            }
            catch (Win32Exception)
            {
            }
        }

        private static KeyModifiers X11Modifiers(uint state)
        {
            var result = KeyModifiers.None;
            if ((state & 1) != 0) result |= KeyModifiers.Shift;
            if ((state & 4) != 0) result |= KeyModifiers.Control;
            if ((state & 8) != 0) result |= KeyModifiers.Alt;
            if ((state & 64) != 0) result |= KeyModifiers.Meta;
            return result;
        }
    }
}
