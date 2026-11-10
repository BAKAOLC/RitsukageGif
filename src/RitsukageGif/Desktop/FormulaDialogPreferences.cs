namespace RitsukageGif.Desktop
{
    public sealed record FormulaDialogPreferences
    {
        public bool RemoveSpaces { get; set; } = true;
        public bool SingleWindowMode { get; set; }
        public bool CloseAfterCopyCurrentFormat { get; set; }
        public bool CloseAfterCopySelectedText { get; set; }

        internal FormulaDialogPreferences MergeChanges(FormulaDialogPreferences baseline,
            FormulaDialogPreferences edited)
        {
            return new()
            {
                RemoveSpaces = baseline.RemoveSpaces == edited.RemoveSpaces ? RemoveSpaces : edited.RemoveSpaces,
                SingleWindowMode = baseline.SingleWindowMode == edited.SingleWindowMode
                    ? SingleWindowMode
                    : edited.SingleWindowMode,
                CloseAfterCopyCurrentFormat = baseline.CloseAfterCopyCurrentFormat == edited.CloseAfterCopyCurrentFormat
                    ? CloseAfterCopyCurrentFormat
                    : edited.CloseAfterCopyCurrentFormat,
                CloseAfterCopySelectedText = baseline.CloseAfterCopySelectedText == edited.CloseAfterCopySelectedText
                    ? CloseAfterCopySelectedText
                    : edited.CloseAfterCopySelectedText,
            };
        }
    }
}
