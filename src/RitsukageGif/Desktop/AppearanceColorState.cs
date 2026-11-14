using System.Buffers.Binary;
using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    internal sealed class AppearanceColorState
    {
        internal const int CustomColorCount = 16;

        private static readonly ILogger Logger = LogHub.GetLogger("Desktop.ColorPicker");
        private const int StateSize = 1 + sizeof(uint) * CustomColorCount;

        private readonly string? _filePath;
        private readonly Color[] _customColors = [.. Enumerable.Repeat(Colors.White, CustomColorCount)];
        private Task _saved = Task.CompletedTask;

        private AppearanceColorState(string? filePath)
        {
            _filePath = filePath;
        }

        internal static AppearanceColorState Current { get; private set; } = new(null);
        internal int ModernFormat { get; private set; }
        internal IReadOnlyList<Color> CustomColors => _customColors;

        internal static void Initialize(bool task)
        {
            var path = Path.Combine(ApplicationStorage.DataDirectory, "color-picker.state");
            Current = Load(path, task);
        }

        private static AppearanceColorState Load(string filePath, bool readOnly)
        {
            var state = new AppearanceColorState(readOnly ? null : filePath);
            try
            {
                if (!File.Exists(filePath)) return state;
                using var stream = File.OpenRead(filePath);
                if (stream.Length != StateSize) return state;
                Span<byte> buffer = stackalloc byte[StateSize];
                stream.ReadExactly(buffer);
                if (buffer[0] <= 2) state.ModernFormat = buffer[0];
                for (var index = 0; index < CustomColorCount; index++)
                    state._customColors[index] = Color.FromUInt32(
                        BinaryPrimitives.ReadUInt32LittleEndian(buffer[(1 + index * sizeof(uint))..]));
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                Logger.ZLogWarning(error, $"Reading color picker state failed.");
            }

            return state;
        }

        internal void SetModernFormat(int format)
        {
            if (ModernFormat == format) return;
            ModernFormat = format;
            Save();
        }

        internal void SetCustomColor(int slot, Color color)
        {
            _customColors[slot] = color;
        }

        internal Task FlushAsync()
        {
            return _saved;
        }

        internal void Save()
        {
            if (_filePath is not { } filePath) return;
            var snapshot = new byte[StateSize];
            snapshot[0] = (byte)ModernFormat;
            for (var index = 0; index < CustomColorCount; index++)
                BinaryPrimitives.WriteUInt32LittleEndian(snapshot.AsSpan(1 + index * sizeof(uint)),
                    _customColors[index].ToUInt32());
            var prior = _saved;
            _saved = Task.Run(async () =>
            {
                try
                {
                    await prior.ConfigureAwait(false);
                    Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
                    await AtomicFileOutput.WriteBytesAsync(filePath, snapshot).ConfigureAwait(false);
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                {
                    Logger.ZLogWarning(error, $"Writing color picker state failed.");
                }
            });
        }
    }
}
