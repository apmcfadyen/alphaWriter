namespace alphaWriter.Models.Analysis
{
    public class StyleProfile
    {
        public double AverageSentenceLength { get; set; }
        public double SentenceLengthStdDev { get; set; }
        public double VocabularyRichness { get; set; }   // type-token ratio
        public double AverageWordLength { get; set; }
        public double ContractionRate { get; set; }       // fraction of sentences containing contractions
        public int TotalSentences { get; set; }
        public int TotalWords { get; set; }
        public double DialogueRatio { get; set; }         // fraction of sentences that are dialogue
        public double ReadabilityScore { get; set; }      // Flesch-Kincaid Reading Ease (0–100, higher = easier)
        public double OpenerVariety { get; set; }         // Shannon entropy of sentence-opener POS types (0–1)
        public double ClauseComplexity { get; set; }      // avg subordinate clauses per sentence
    }
}
