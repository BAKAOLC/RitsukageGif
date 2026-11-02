namespace RitsukageGif.Desktop
{
    public sealed partial record AppSettings
    {
        internal static AnnotationPreferences MergeAnnotationChanges(AnnotationPreferences current,
            AnnotationPreferences baseline, AnnotationPreferences edited)
        {
            return Merge(current, baseline, edited);
        }
    }
}
