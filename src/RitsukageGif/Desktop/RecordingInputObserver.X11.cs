using System.Buffers.Binary;
using System.Diagnostics;
using System.Text;
using Avalonia.Input;

namespace RitsukageGif.Desktop
{
    internal sealed partial class RecordingInputObserver
    {
        private void RunX11()
        {
            var start = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "RitsukageGif.MouseObserver"))
            {
                UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true,
                RedirectStandardError = true, CreateNoWindow = true,
            };
            start.ArgumentList.Add("--recording-input");
            if (_recordKeyboard) start.ArgumentList.Add("--keyboard");
            if (_recordMouse) start.ArgumentList.Add("--mouse");
            using var process = Process.Start(start) ??
                                throw new IOException("The recording input helper could not be started.");
            var errors = ReadRecordingErrorsAsync(process.StandardError);
            var shutdown = Task.CompletedTask;
            // ReSharper disable once AccessToDisposedClosure
            using var cancellation = _stop.Token.Register(() =>
            {
                shutdown = Task.Run(() => StopRecordingHelper(process));
            });
            try
            {
                Span<byte> packet = stackalloc byte[40];
                var stream = process.StandardOutput.BaseStream;
                if (!ReadRecordingPacket(stream, packet[..24]))
                {
                    StopRecordingHelper(process);
                    var detail = errors.GetAwaiter().GetResult();
                    if (process.ExitCode == 69) throw new PlatformNotSupportedException(detail);
                    throw new IOException("The recording input helper could not initialize: " + detail);
                }

                if (!packet[..8].SequenceEqual("RGINPUT1"u8) ||
                    BinaryPrimitives.ReadUInt32LittleEndian(packet[20..]) != 40 ||
                    BinaryPrimitives.ReadUInt32LittleEndian(packet[16..]) !=
                    ((_recordKeyboard ? 1u : 0) | (_recordMouse ? 2u : 0)))
                    throw new IOException("The recording input helper returned an invalid initialization frame.");
                _stop.Token.ThrowIfCancellationRequested();
                _ready.TrySetResult();
                while (ReadRecordingPacket(stream, packet))
                {
                    if (_stop.IsCancellationRequested) continue; // Drain helper EOF after closing its input.
                    var kind = BinaryPrimitives.ReadUInt32LittleEndian(packet);
                    var code = BinaryPrimitives.ReadUInt32LittleEndian(packet[4..]);
                    var scan = BinaryPrimitives.ReadUInt32LittleEndian(packet[8..]);
                    var state = BinaryPrimitives.ReadUInt32LittleEndian(packet[12..]);
                    var x = BinaryPrimitives.ReadInt32LittleEndian(packet[16..]);
                    var y = BinaryPrimitives.ReadInt32LittleEndian(packet[20..]);
                    var value = BinaryPrimitives.ReadInt32LittleEndian(packet[24..]);
                    var time = NanosecondsToTimestamp(BinaryPrimitives.ReadUInt64LittleEndian(packet[32..]));
                    var modifiers = ((state & 1) != 0 ? KeyModifiers.Shift : 0) |
                                    ((state & 4) != 0 ? KeyModifiers.Control : 0) |
                                    ((state & 8) != 0 ? KeyModifiers.Alt : 0) |
                                    ((state & 64) != 0 ? KeyModifiers.Meta : 0);
                    switch (kind)
                    {
                        case 1 or 2 when _recordKeyboard:
                            Emit(new(time, kind == 1 ? RecordingInputEventKind.KeyDown : RecordingInputEventKind.KeyUp,
                                X11Key(scan), modifiers, NativeCode: code, NativeScanCode: scan));
                            break;
                        case 3 or 4 when _recordMouse:
                            Emit(new(time,
                                kind == 3 ? RecordingInputEventKind.PointerDown : RecordingInputEventKind.PointerUp,
                                Modifiers: modifiers, Button: value, X: x, Y: y));
                            break;
                        case 5 when _recordMouse:
                            Emit(new(time, RecordingInputEventKind.Wheel, Modifiers: modifiers,
                                X: x, Y: y, WheelDelta: value, NativeCode: code));
                            break;
                        case 6:
                            Emit(new(time, RecordingInputEventKind.Modifiers, Modifiers: modifiers));
                            break;
                        default:
                            throw new IOException("The recording input helper returned an invalid event kind.");
                    }
                }

                if (!_stop.IsCancellationRequested)
                    throw new IOException("Recording input observation stopped unexpectedly: " +
                                          errors.GetAwaiter().GetResult());
            }
            finally
            {
                // ReSharper disable once DisposeOnUsingVariable
                cancellation.Dispose();
                if (_stop.IsCancellationRequested) shutdown.GetAwaiter().GetResult();
                else StopRecordingHelper(process);
                errors.GetAwaiter().GetResult();
            }
        }

        private static bool ReadRecordingPacket(Stream input, Span<byte> packet)
        {
            var offset = 0;
            while (offset < packet.Length)
            {
                var count = input.Read(packet[offset..]);
                if (count == 0)
                {
                    if (offset != 0)
                        throw new IOException("The recording input helper returned a truncated event frame.");
                    return false;
                }

                offset += count;
            }

            return true;
        }

        private static async Task<string> ReadRecordingErrorsAsync(StreamReader reader)
        {
            var text = new StringBuilder();
            var buffer = new char[512];
            int count;
            while ((count = await reader.ReadAsync(buffer).ConfigureAwait(false)) != 0)
                if (text.Length < 8192)
                    text.Append(buffer, 0, Math.Min(count, 8192 - text.Length));
            return text.ToString().Trim();
        }

        private static void StopRecordingHelper(Process process)
        {
            try
            {
                process.StandardInput.Close();
            }
            catch (IOException)
            {
            }

            if (!process.WaitForExit(1000))
            {
                process.Kill(true);
                process.WaitForExit();
            }
        }
    }
}
