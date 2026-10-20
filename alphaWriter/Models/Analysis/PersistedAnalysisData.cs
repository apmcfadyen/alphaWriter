using System;
using System.Collections.Generic;

namespace alphaWriter.Models.Analysis
{
    public class PersistedAnalysisData
    {
        /// <summary>
        /// Format version. Data written by an older version (sentence indices, no spans)
        /// is discarded on load rather than shown with misaligned highlights.
        /// </summary>
        public int FormatVersion { get; set; }

        public DateTime AnalyzedAtUtc { get; set; }
        public Dictionary<string, string> SceneContentHashes { get; set; } = [];
        public List<SceneAnalysisResult> Results { get; set; } = [];
        public List<NlpNote> Notes { get; set; } = [];
    }
}
