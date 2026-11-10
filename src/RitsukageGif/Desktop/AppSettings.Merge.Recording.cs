namespace RitsukageGif.Desktop
{
    public sealed partial record AppSettings
    {
        private static ScreenshotRecordingPreferences MergeRecordingPreferences(ScreenshotRecordingPreferences current,
            ScreenshotRecordingPreferences baseline, ScreenshotRecordingPreferences edited)
        {
            return current with
            {
                Fps = KeepUneditedRecordingValue(current.Fps, baseline.Fps, edited.Fps),
                DelaySeconds =
                KeepUneditedRecordingValue(current.DelaySeconds, baseline.DelaySeconds, edited.DelaySeconds),
                Quick = KeepUneditedRecordingValue(current.Quick, baseline.Quick, edited.Quick),
                QuickQuality =
                KeepUneditedRecordingValue(current.QuickQuality, baseline.QuickQuality, edited.QuickQuality),
                SkipQuickReminder = KeepUneditedRecordingValue(current.SkipQuickReminder, baseline.SkipQuickReminder,
                    edited.SkipQuickReminder),
                QuickLastPath =
                KeepUneditedRecordingValue(current.QuickLastPath, baseline.QuickLastPath, edited.QuickLastPath),
                AutoExitAfterEncoding = KeepUneditedRecordingValue(current.AutoExitAfterEncoding,
                    baseline.AutoExitAfterEncoding, edited.AutoExitAfterEncoding),
                ReviewFormat =
                KeepUneditedRecordingValue(current.ReviewFormat, baseline.ReviewFormat, edited.ReviewFormat),
                ReviewQuality =
                KeepUneditedRecordingValue(current.ReviewQuality, baseline.ReviewQuality, edited.ReviewQuality),
                SystemAudio = KeepUneditedRecordingValue(current.SystemAudio, baseline.SystemAudio, edited.SystemAudio),
                Microphone = KeepUneditedRecordingValue(current.Microphone, baseline.Microphone, edited.Microphone),
                RecordKeyboardActions = KeepUneditedRecordingValue(current.RecordKeyboardActions,
                    baseline.RecordKeyboardActions, edited.RecordKeyboardActions),
                RecordMouseActions = KeepUneditedRecordingValue(current.RecordMouseActions,
                    baseline.RecordMouseActions, edited.RecordMouseActions),
                Camera = KeepUneditedRecordingValue(current.Camera, baseline.Camera, edited.Camera),
                InputEffects = Merge(current.InputEffects, baseline.InputEffects, edited.InputEffects),
                ProgressEffects = Merge(current.ProgressEffects, baseline.ProgressEffects, edited.ProgressEffects),
                ToolbarCaptureMode = KeepUneditedRecordingValue(current.ToolbarCaptureMode, baseline.ToolbarCaptureMode,
                    edited.ToolbarCaptureMode),
            };
        }

        private static T KeepUneditedRecordingValue<T>(T current, T baseline, T edited)
        {
            return EqualityComparer<T>.Default.Equals(baseline, edited) ? current : edited;
        }
    }
}
