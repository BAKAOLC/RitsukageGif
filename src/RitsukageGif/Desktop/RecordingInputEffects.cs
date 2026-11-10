using System.Text.Json.Serialization;
using RitsukageGif.SettingsGeneration;

namespace RitsukageGif.Desktop
{
    [GenerateSettingsMerge(typeof(AppSettings))]
    public sealed record RecordingInputEffects
    {
        [JsonIgnore] public bool ShowCursor { get; init; } = true;

        public int CursorSize { get; init; } = 100;
        public int MouseButtons { get; init; } = 7;
        public RecordingMouseStyle MouseStyle { get; init; } = RecordingMouseStyle.Ray;
        public RecordingWheelStyle WheelStyle { get; init; } = RecordingWheelStyle.Arrow;
        public bool ShowWheel { get; init; } = true;
        public uint MouseColor { get; init; } = 0xff3388ff;
        public bool SmoothTrack { get; init; } = true;
        public double AnimationSpeed { get; init; } = 1;

        public RecordingKeyboardCategories KeyboardCategories { get; init; } =
            RecordingKeyboardCategories.Other | RecordingKeyboardCategories.Characters |
            RecordingKeyboardCategories.Modifiers;

        public int KeyboardFontSize { get; init; } = 22;
        public uint KeyboardTextColor { get; init; } = 0xffffffff;
        public uint KeyboardBackground { get; init; } = 0xc8000000;
        public string? KeyboardFont { get; init; }
        public int KeyboardKeepSeconds { get; init; } = 2;
        public int KeyboardMaxHistory { get; init; } = 5;
        public int KeyboardMaxCharacters { get; init; } = 5;
        public int KeyboardPosition { get; init; } = 2;
        public bool SimplifyMouseAnimation { get; init; } = true;

        public RecordingInputEffects Normalize()
        {
            return this with
            {
                ShowCursor = true,
                CursorSize = Math.Clamp(CursorSize, 50, 200),
                MouseButtons = MouseButtons & 7,
                MouseStyle = Enum.IsDefined(MouseStyle) ? MouseStyle : RecordingMouseStyle.Ray,
                WheelStyle = Enum.IsDefined(WheelStyle) ? WheelStyle : RecordingWheelStyle.Arrow,
                AnimationSpeed = double.IsFinite(AnimationSpeed) ? Math.Clamp(AnimationSpeed, 0.2, 1.8) : 1,
                KeyboardCategories = KeyboardCategories & (RecordingKeyboardCategories.Combinations |
                                                           RecordingKeyboardCategories.Other |
                                                           RecordingKeyboardCategories.Characters |
                                                           RecordingKeyboardCategories.Modifiers),
                KeyboardFontSize = Math.Clamp(KeyboardFontSize, 12, 48),
                KeyboardFont = KeyboardFont?.Trim() is { Length: > 0 and <= 256 } font ? font : null,
                KeyboardKeepSeconds = Math.Clamp(KeyboardKeepSeconds, 1, 10),
                KeyboardMaxHistory = Math.Clamp(KeyboardMaxHistory, 1, 10),
                KeyboardMaxCharacters = Math.Clamp(KeyboardMaxCharacters, 1, 50),
                KeyboardPosition = KeyboardPosition is 1 or 2 or 3 ? KeyboardPosition : 2,
            };
        }
    }
}
