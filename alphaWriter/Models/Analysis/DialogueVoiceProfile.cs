using System.Collections.Generic;

namespace alphaWriter.Models.Analysis
{
    public class DialogueVoiceProfile
    {
        public string CharacterId { get; set; } = string.Empty;
        public string CharacterName { get; set; } = string.Empty;
        public StyleProfile Style { get; set; } = new();
        public List<(string Word, double TfIdf)> DistinctiveWords { get; set; } = [];
        public Dictionary<EmotionLabel, double> EmotionDistribution { get; set; } = [];
        public int DialogueLineCount { get; set; }
        public int TotalWords { get; set; }
        public int SceneCount { get; set; }

        // Core style convenience accessors
        public double AvgSentenceLength { get; set; }
        public double VocabularyRichness { get; set; }
        public double ContractionRate { get; set; }
        public double ReadabilityScore { get; set; }

        // POS-dependent metrics (populated when POS tagging is available)
        public double AdverbDensity { get; set; }
        public double PassiveVoiceRate { get; set; }
        public double ClauseComplexity { get; set; }

        // Dialogue-specific metric (replaces "dialogue ratio" on the radar chart)
        public double ExclamationRate { get; set; }
    }
}
