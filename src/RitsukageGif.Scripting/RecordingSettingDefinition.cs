namespace RitsukageGif.Scripting
{
    internal sealed record RecordingSettingDefinition(
        ScriptPropertyDescriptor Property,
        Func<PresetRecordingSettings, object?, PresetRecordingSettings> Apply);
}
