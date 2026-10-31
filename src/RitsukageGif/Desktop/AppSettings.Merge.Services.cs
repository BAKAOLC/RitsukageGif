namespace RitsukageGif.Desktop
{
    public sealed partial record AppSettings
    {
        private static TranslationPreferences Merge(TranslationPreferences current, TranslationPreferences baseline,
            TranslationPreferences edited)
        {
            return current with
            {
                Provider = MergeValue(current.Provider, baseline.Provider, edited.Provider),
                Endpoint = MergeValue(current.Endpoint, baseline.Endpoint, edited.Endpoint),
                Region = MergeValue(current.Region, baseline.Region, edited.Region),
                Model = MergeValue(current.Model, baseline.Model, edited.Model),
                SourceLanguage = MergeValue(current.SourceLanguage, baseline.SourceLanguage, edited.SourceLanguage),
                TargetLanguage = MergeValue(current.TargetLanguage, baseline.TargetLanguage, edited.TargetLanguage),
                FallbackLanguage = MergeValue(current.FallbackLanguage, baseline.FallbackLanguage,
                    edited.FallbackLanguage),
                TimeoutSeconds = MergeValue(current.TimeoutSeconds, baseline.TimeoutSeconds, edited.TimeoutSeconds),
                UseApiKey = MergeValue(current.UseApiKey, baseline.UseApiKey, edited.UseApiKey),
            };
        }


        private static T MergeValue<T>(T current, T baseline, T edited)
        {
            return EqualityComparer<T>.Default.Equals(baseline, edited) ? current : edited;
        }
    }
}
