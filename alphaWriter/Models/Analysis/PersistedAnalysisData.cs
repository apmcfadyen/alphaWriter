using System;
using System.Collections.Generic;

namespace alphaWriter.Models.Analysis
{
    public class PersistedAnalysisData
    {
        public DateTime AnalyzedAtUtc { get; set; }
        public Dictionary<string, string> SceneContentHashes { get; set; } = [];
        public List<SceneAnalysisResult> Results { get; set; } = [];
        public List<NlpNote> Notes { get; set; } = [];
    }
}
