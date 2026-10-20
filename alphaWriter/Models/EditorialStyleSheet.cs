using System;
using System.Collections.Generic;
using System.Linq;

namespace alphaWriter.Models
{
    /// <summary>
    /// The author's per-book editorial preferences: which rules run, how strict they are,
    /// words the book legitimately uses (invented terms, dialect), and notes the author
    /// has dismissed. Persisted with the Book.
    /// </summary>
    public class EditorialStyleSheet
    {
        public const string DefaultGenre = "General";

        public string Genre { get; set; } = DefaultGenre;

        /// <summary>Rules turned off for the whole book.</summary>
        public List<string> DisabledRuleIds { get; set; } = [];

        /// <summary>Rules turned off for individual scenes: sceneId → rule ids.</summary>
        public Dictionary<string, List<string>> SceneDisabledRuleIds { get; set; } = [];

        /// <summary>Words never flagged (invented terms, dialect, deliberate repetition).</summary>
        public List<string> AllowedWords { get; set; } = [];

        /// <summary>Fingerprints of individually dismissed notes.</summary>
        public List<string> DismissedFingerprints { get; set; } = [];

        /// <summary>Explicit threshold overrides keyed "ruleId:setting". Beat genre presets.</summary>
        public Dictionary<string, double> Thresholds { get; set; } = [];

        public bool IsRuleEnabled(string ruleId, string? sceneId = null)
        {
            if (string.IsNullOrEmpty(ruleId)) return true;
            if (DisabledRuleIds.Contains(ruleId)) return false;
            if (sceneId is not null
                && SceneDisabledRuleIds.TryGetValue(sceneId, out var perScene)
                && perScene.Contains(ruleId))
                return false;
            return true;
        }

        public bool IsWordAllowed(string word) =>
            !string.IsNullOrWhiteSpace(word)
            && AllowedWords.Any(w => string.Equals(w, word.Trim(), StringComparison.OrdinalIgnoreCase));

        public bool IsDismissed(string fingerprint) =>
            !string.IsNullOrEmpty(fingerprint) && DismissedFingerprints.Contains(fingerprint);

        public void DisableRuleForScene(string ruleId, string sceneId)
        {
            if (!SceneDisabledRuleIds.TryGetValue(sceneId, out var list))
                SceneDisabledRuleIds[sceneId] = list = [];
            if (!list.Contains(ruleId)) list.Add(ruleId);
        }

        /// <summary>
        /// Resolves a rule threshold: explicit override → genre preset → rule default.
        /// </summary>
        public double GetThreshold(string ruleId, string setting, double defaultValue)
        {
            var key = $"{ruleId}:{setting}";
            if (Thresholds.TryGetValue(key, out var v)) return v;
            if (GenrePresets.TryGet(Genre, key, out var preset)) return preset;
            return defaultValue;
        }
    }

    /// <summary>
    /// Genre-appropriate defaults. A thriller tolerates far less unbroken narration than
    /// literary fiction; literary fiction tolerates longer sentences and denser clauses.
    /// </summary>
    public static class GenrePresets
    {
        public static readonly IReadOnlyList<string> Genres =
            ["General", "Literary", "Thriller", "Romance", "Fantasy / SF", "Young Adult"];

        private static readonly Dictionary<string, Dictionary<string, double>> Presets = new()
        {
            ["Literary"] = new()
            {
                ["sentence.long:minWords"] = 55,
                ["pacing.narration-run:maxRun"] = 25,
                ["style.clause-density:maxMean"] = 2.0,
                ["style.adverb-density:maxDensity"] = 0.035,
            },
            ["Thriller"] = new()
            {
                ["sentence.long:minWords"] = 32,
                ["pacing.narration-run:maxRun"] = 10,
                ["style.clause-density:maxMean"] = 1.2,
                ["style.adverb-density:maxDensity"] = 0.025,
            },
            ["Romance"] = new()
            {
                ["sentence.long:minWords"] = 38,
                ["pacing.narration-run:maxRun"] = 12,
            },
            ["Fantasy / SF"] = new()
            {
                ["sentence.long:minWords"] = 45,
                ["pacing.narration-run:maxRun"] = 18,
            },
            ["Young Adult"] = new()
            {
                ["sentence.long:minWords"] = 34,
                ["pacing.narration-run:maxRun"] = 12,
                ["style.adverb-density:maxDensity"] = 0.03,
            },
        };

        public static bool TryGet(string genre, string key, out double value)
        {
            value = 0;
            return Presets.TryGetValue(genre ?? string.Empty, out var table)
                && table.TryGetValue(key, out value);
        }
    }
}
