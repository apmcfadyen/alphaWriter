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
    }
}
