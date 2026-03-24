using System.Collections.Generic;

namespace alphaWriter.Models.Analysis
{
    public class CharacterVoiceProfile
    {
        public string CharacterId { get; set; } = string.Empty;
        public string CharacterName { get; set; } = string.Empty;
        public StyleProfile Style { get; set; } = new();
        public List<(string Word, double TfIdf)> DistinctiveWords { get; set; } = [];
        public Dictionary<EmotionCluster, double> EmotionDistribution { get; set; } = [];
        public int SceneCount { get; set; }
        public int TotalWords { get; set; }
    }
}
