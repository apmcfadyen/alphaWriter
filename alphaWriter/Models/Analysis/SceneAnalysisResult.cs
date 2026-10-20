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

        /// <summary>
        /// What each paragraph is mostly doing ("Dialogue", "Action", "Interiority",
        /// "Description", "Exposition"), in order: the scene's shape at a glance.
        /// </summary>
        public List<string> ParagraphBeats { get; set; } = [];

        // Populated in Phase 2 (embedding-based)
        public float ChapterSimilarity { get; set; }

        // Populated in Phase 3 (voice profiling)
        public List<string> ViewpointCharacterIds { get; set; } = [];

        // ── Incremental analysis bookkeeping ─────────────────────────────────

        /// <summary>Hash of the scene HTML this result was computed from.</summary>
        public string ContentHash { get; set; } = string.Empty;

        /// <summary>Rule engine version that produced the scene-level notes.</summary>
        public int EngineVersion { get; set; }

        /// <summary>True when POS-tier rules ran for this scene.</summary>
        public bool HadPos { get; set; }

        public int IssueCount => Notes.Count;
    }
}
