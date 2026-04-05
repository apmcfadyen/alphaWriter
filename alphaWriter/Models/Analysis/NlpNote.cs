namespace alphaWriter.Models.Analysis
{
    public class NlpNote
    {
        public NlpNoteSeverity Severity { get; set; }
        public NlpNoteCategory Category { get; set; }
        public string SceneId { get; set; } = string.Empty;
        public string SceneTitle { get; set; } = string.Empty;
        public string ChapterTitle { get; set; } = string.Empty;
        public int? SentenceIndex { get; set; }
        public string Message { get; set; } = string.Empty;

        public string SeverityColor => Severity switch
        {
            NlpNoteSeverity.Issue => "#E06C75",
            NlpNoteSeverity.Warning => "#E5C07B",
            _ => "#61AFEF"
        };

        public string CategoryLabel => Category switch
        {
            NlpNoteCategory.CopyEditor => "Copy Editor",
            NlpNoteCategory.LineEditor => "Line Editor",
            NlpNoteCategory.DevelopmentalEditor => "Developmental Editor",
            _ => Category.ToString()
        };
    }
}
