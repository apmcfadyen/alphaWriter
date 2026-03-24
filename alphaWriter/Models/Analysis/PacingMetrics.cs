namespace alphaWriter.Models.Analysis
{
    public class PacingMetrics
    {
        public double DialogueDensity { get; set; }            // fraction of sentences that are dialogue
        public int LongestNarrationStreak { get; set; }        // max consecutive non-dialogue sentences
        public double SceneWordCount { get; set; }
        public double ChapterAverageWordCount { get; set; }
        public double SceneToChapterRatio { get; set; }        // scene words / chapter avg
        public int ParagraphCount { get; set; }
        public double AverageParagraphLength { get; set; }     // sentences per paragraph
    }
}
