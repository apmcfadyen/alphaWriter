using System.Collections.Generic;

namespace alphaWriter.Models.Analysis
{
    public class SceneAnalysisResult
    {
        public string SceneId { get; set; } = string.Empty;
        public string SceneTitle { get; set; } = string.Empty;
        public string ChapterTitle { get; set; } = string.Empty;

        public StyleProfile Style { get; set; } = new();
        public PacingMetrics Pacing { get; set; } = new();
        public List<SentenceAnalysis> Sentences { get; set; } = [];
        public List<NlpNote> Notes { get; set; } = [];

        // Populated in Phase 2 (embedding-based)
        public float ChapterSimilarity { get; set; }

        // Populated in Phase 3 (voice profiling)
        public List<string> ViewpointCharacterIds { get; set; } = [];

        public int IssueCount => Notes.Count;
    }
}
