using System.Collections.Generic;

namespace alphaWriter.Models.Analysis
{
    public class SentenceAnalysis
    {
        public int Index { get; set; }
        public string Text { get; set; } = string.Empty;
        public int WordCount { get; set; }

        /// <summary>Offset of this sentence in the scene's normalized plain text.</summary>
        public int Start { get; set; }

        /// <summary>Index of the paragraph that contains this sentence.</summary>
        public int ParagraphIndex { get; set; }

        // Populated by EmotionService (Phase 3)
        public List<(EmotionLabel Label, float Confidence)> Emotions { get; set; } = [];

        // Populated by EmbeddingService (Phase 2)
        public float EmbeddingDistance { get; set; }

        public List<string> Flags { get; set; } = [];
    }
}
