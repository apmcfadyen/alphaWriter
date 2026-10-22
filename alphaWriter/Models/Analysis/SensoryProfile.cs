using System.Collections.Generic;

namespace alphaWriter.Models.Analysis
{
    /// <summary>
    /// Which senses a scene's narration draws on, from the Lancaster Sensorimotor Norms.
    /// Indexed by sense: sound, taste, touch, body, smell, sight.
    /// </summary>
    public class SensoryProfile
    {
        /// <summary>Sensory words per sense.</summary>
        public int[] Counts { get; set; } = new int[6];

        /// <summary>Up to five example words per sense, in order of appearance.</summary>
        public List<string>[] Examples { get; set; } = [[], [], [], [], [], []];

        /// <summary>Content words found in the norms.</summary>
        public int RatedWords { get; set; }

        /// <summary>Mean strongest-sense rating of content words (0–5); higher is more concrete.</summary>
        public double Concreteness { get; set; }
    }
}
