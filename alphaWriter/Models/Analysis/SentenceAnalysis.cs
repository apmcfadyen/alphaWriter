using System.Collections.Generic;

namespace alphaWriter.Models.Analysis
{
    public class SentenceAnalysis
    {
        public int Index { get; set; }
        public string Text { get; set; } = string.Empty;
        public int WordCount { get; set; }

        // Populated by EmotionService (Phase 3)
        public List<(EmotionLabel Label, float Confidence)> Emotions { get; set; } = [];

        // Populated by EmbeddingService (Phase 2)
        public float EmbeddingDistance { get; set; }

        public List<string> Flags { get; set; } = [];
    }
}
