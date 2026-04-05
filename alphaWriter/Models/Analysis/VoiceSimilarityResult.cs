using System.Collections.Generic;

namespace alphaWriter.Models.Analysis
{
    public class VoiceSimilarityResult
    {
        public string CharacterAName { get; set; } = string.Empty;
        public string CharacterBName { get; set; } = string.Empty;
        public double SimilarityScore { get; set; }
        public List<string> SharedDistinctiveWords { get; set; } = [];
    }
}
