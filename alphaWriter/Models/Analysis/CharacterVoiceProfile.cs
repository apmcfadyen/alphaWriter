using System.Collections.Generic;

namespace alphaWriter.Models.Analysis
{
    public class CharacterVoiceProfile
    {
        public string CharacterId { get; set; } = string.Empty;
        public string CharacterName { get; set; } = string.Empty;
        public StyleProfile Style { get; set; } = new();
        public List<(string Word, double TfIdf)> DistinctiveWords { get; set; } = [];
        public Dictionary<EmotionLabel, double> EmotionDistribution { get; set; } = [];
        public int SceneCount { get; set; }
        public int TotalWords { get; set; }

        // Extended voice metrics (populated when POS tagging is available)
        public double AdverbDensity { get; set; }       // -ly adverbs / total words
        public double PassiveVoiceRate { get; set; }    // passive sentences / total sentences
        public double OpenerVariety { get; set; }       // Shannon entropy of sentence-opener types (0–1)
        public double ClauseComplexity { get; set; }    // avg subordinate clauses per sentence
        public double ReadabilityScore { get; set; }    // Flesch-Kincaid Reading Ease (0–100)

        // Per-scene tracking for the consistency timeline chart
        // Each entry is one scene appearance in chapter order: [sceneTitle, avgSentLen, vocabRichness, contractionRate, dialogueRatio, adverbDensity, passiveRate, readability]
        public List<SceneVoiceSnapshot> SceneSnapshots { get; set; } = [];
    }

    public class SceneVoiceSnapshot
    {
        public string SceneTitle { get; set; } = string.Empty;
        public string ChapterTitle { get; set; } = string.Empty;
        public double AvgSentenceLength { get; set; }
        public double VocabularyRichness { get; set; }
        public double ContractionRate { get; set; }
        public double DialogueRatio { get; set; }
        public double AdverbDensity { get; set; }
        public double PassiveVoiceRate { get; set; }
        public double ReadabilityScore { get; set; }
    }
}
