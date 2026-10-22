using System;
using System.Collections.Generic;

namespace alphaWriter.Models
{
    /// <summary>
    /// Note counts after one whole-book analysis, so revision progress can be shown over
    /// time ("filter words: 31 → 22"). Only notes the author hadn't hidden are counted.
    /// </summary>
    public class AnalysisSnapshot
    {
        public DateTime Date { get; set; }
        public int Words { get; set; }
        public int Total { get; set; }

        /// <summary>Rule id → visible notes.</summary>
        public Dictionary<string, int> ByRule { get; set; } = new();
    }
}
