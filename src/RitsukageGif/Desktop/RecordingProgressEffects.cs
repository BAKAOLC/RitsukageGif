using System.Text.Json.Serialization;
using RitsukageGif.SettingsGeneration;

namespace RitsukageGif.Desktop
{
    [GenerateSettingsMerge(typeof(AppSettings))]
    public sealed record RecordingProgressEffects
    {
        public bool Enabled { get; init; }
        public bool Bar { get; init; }
        public int FontSize { get; init; } = 16;

        [JsonIgnore] public int BarHeight => Math.Min(20, FontSize / 2);

        public uint TextForeground { get; init; } = 0xffffffff;
        public uint TextBackground { get; init; } = 0x80000000;
        public uint BarForeground { get; init; } = 0xff007aff;
        public uint BarBackground { get; init; } = 0xffc8c8c8;

        public RecordingProgressEffects Normalize()
        {
            return this with { FontSize = Math.Clamp(FontSize, 12, 48) };
        }
    }
}
