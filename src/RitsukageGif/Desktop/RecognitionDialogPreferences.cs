namespace RitsukageGif.Desktop
{
    public sealed record RecognitionDialogPreferences
    {
        public FormulaDialogPreferences Formula { get; set; } = new();
        public TableOutputStyle Table { get; set; } = TableOutputStyle.Default;
        public RecognitionTextLayout? Layout { get; set; }
        public RecognitionPunctuation? Punctuation { get; set; }
        public bool RemoveSpaces { get; set; }
        public bool CopyTextOnClose { get; set; }
        public bool HighlightSelectedText { get; set; }
        public bool VerticalRightToLeft { get; set; } = true;
        public bool CloseAfterCopyAll { get; set; } = true;
        public bool CloseAfterCopySelected { get; set; }
        public bool Topmost { get; set; } = true;
        public int Width { get; init; }
        public int Height { get; init; }

        internal static bool ValidSize(int width, int height)
        {
            return (width == 0 && height == 0) || (width is > 0 and <= 32768 && height is > 0 and <= 32768);
        }

        internal RecognitionDialogPreferences Normalize()
        {
            return this with
            {
                // ReSharper disable once NullCoalescingConditionIsAlwaysNotNullAccordingToAPIContract
                Formula = Formula ?? new(),
                // ReSharper disable once NullCoalescingConditionIsAlwaysNotNullAccordingToAPIContract
                Table = (Table ?? TableOutputStyle.Default).Normalize(),
                Layout = Layout is { } layout && !Enum.IsDefined(layout) ? null : Layout,
                Punctuation = Punctuation is { } punctuation && !Enum.IsDefined(punctuation) ? null : Punctuation,
                Width = ValidSize(Width, Height) ? Width : 0,
                Height = ValidSize(Width, Height) ? Height : 0,
            };
        }

        internal RecognitionDialogPreferences MergeChanges(RecognitionDialogPreferences baseline,
            RecognitionDialogPreferences edited)
        {
            return new()
            {
                Formula = Formula.MergeChanges(baseline.Formula, edited.Formula),
                Table = Table.MergeChanges(baseline.Table, edited.Table),
                Layout = baseline.Layout == edited.Layout ? Layout : edited.Layout,
                Punctuation = baseline.Punctuation == edited.Punctuation ? Punctuation : edited.Punctuation,
                RemoveSpaces = baseline.RemoveSpaces == edited.RemoveSpaces ? RemoveSpaces : edited.RemoveSpaces,
                CopyTextOnClose = baseline.CopyTextOnClose == edited.CopyTextOnClose
                    ? CopyTextOnClose
                    : edited.CopyTextOnClose,
                HighlightSelectedText = baseline.HighlightSelectedText == edited.HighlightSelectedText
                    ? HighlightSelectedText
                    : edited.HighlightSelectedText,
                VerticalRightToLeft = baseline.VerticalRightToLeft == edited.VerticalRightToLeft
                    ? VerticalRightToLeft
                    : edited.VerticalRightToLeft,
                CloseAfterCopyAll = baseline.CloseAfterCopyAll == edited.CloseAfterCopyAll
                    ? CloseAfterCopyAll
                    : edited.CloseAfterCopyAll,
                CloseAfterCopySelected = baseline.CloseAfterCopySelected == edited.CloseAfterCopySelected
                    ? CloseAfterCopySelected
                    : edited.CloseAfterCopySelected,
                Topmost = baseline.Topmost == edited.Topmost ? Topmost : edited.Topmost,
                Width = baseline.Width == edited.Width && baseline.Height == edited.Height ? Width : edited.Width,
                Height = baseline.Width == edited.Width && baseline.Height == edited.Height ? Height : edited.Height,
            };
        }
    }
}
