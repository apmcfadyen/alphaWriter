namespace alphaWriter.Services.Nlp
{
    public class LocationHeuristicService : ILocationHeuristicService
    {
        private static readonly HashSet<string> SpatialPrepositions = new(StringComparer.OrdinalIgnoreCase)
        {
            "in", "at", "to", "from", "near", "toward", "towards",
            "across", "through", "within", "upon", "outside", "beyond",
            "around", "into", "onto"
        };

        private static readonly HashSet<string> DescriptorNouns = new(StringComparer.OrdinalIgnoreCase)
        {
            "city", "town", "village", "kingdom", "land", "realm",
            "forest", "mountain", "river", "castle", "palace", "tower",
            "temple", "valley", "island", "sea", "ocean", "desert",
            "fortress", "cavern", "cave", "swamp", "marsh", "harbor",
            "port", "province", "empire", "dungeon", "continent",
            "bay", "lake", "peak", "ridge", "plateau", "canyon"
        };

        private static readonly HashSet<string> PersonalPronouns = new(StringComparer.OrdinalIgnoreCase)
        {
            "he", "she", "they", "him", "her", "them",
            "his", "hers", "their", "theirs",
            "himself", "herself", "themselves"
        };

        public List<(string Name, int Count)> FindLocationCandidates(
            IReadOnlyList<string> sentences,
            IReadOnlyList<(string Value, string Pos)>[] taggedSentences,
            IReadOnlySet<string> knownCharacterNames)
        {
            // Phase A+B: extract candidates from each sentence
            // Key = lowercase name, Value = (display name, count, sentences containing it)
            var candidates = new Dictionary<string, (string DisplayName, int Count, List<int> SentenceIndices)>(
                StringComparer.OrdinalIgnoreCase);

            for (int si = 0; si < taggedSentences.Length; si++)
            {
                var tokens = taggedSentences[si];
                var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                // Phase A: spatial preposition patterns
                for (int i = 0; i < tokens.Count; i++)
                {
                    if (tokens[i].Pos == "ADP" && SpatialPrepositions.Contains(tokens[i].Value))
                    {
                        var name = ExtractLocationName(tokens, i + 1);
                        if (name != null)
                            found.Add(name);
                    }
                }

                // Phase B: descriptor-of patterns ("kingdom of Alderia")
                for (int i = 0; i < tokens.Count - 2; i++)
                {
                    if (DescriptorNouns.Contains(tokens[i].Value)
                        && tokens[i + 1].Value.Equals("of", StringComparison.OrdinalIgnoreCase))
                    {
                        var propnName = ExtractProperNounSequence(tokens, i + 2);
                        if (propnName != null)
                        {
                            var fullName = tokens[i].Value + " of " + propnName;
                            found.Add(fullName);
                        }
                    }
                }

                foreach (var name in found)
                {
                    var key = name.ToLowerInvariant();
                    if (candidates.TryGetValue(key, out var existing))
                    {
                        candidates[key] = (existing.DisplayName, existing.Count + 1, existing.SentenceIndices);
                        existing.SentenceIndices.Add(si);
                    }
                    else
                    {
                        candidates[key] = (name, 1, new List<int> { si });
                    }
                }
            }

            // Phase C: filtering

            // C1: remove known character names
            foreach (var charName in knownCharacterNames)
            {
                candidates.Remove(charName);
            }

            // C2: pronoun adjacency filter
            // Scan ALL sentences for each candidate name. If the name appears near
            // personal pronouns (within 2 tokens) in any sentence, it's likely a
            // character, not a location. This catches names like "Marcus" that appear
            // after spatial prepositions ("went to Marcus") but elsewhere are used as
            // characters ("He told Marcus", "Marcus, he said").
            var toRemove = new List<string>();
            foreach (var (key, (displayName, _, _)) in candidates)
            {
                int totalAppearances = 0;
                int pronounAdjacentCount = 0;

                for (int si = 0; si < taggedSentences.Length; si++)
                {
                    if (ContainsName(taggedSentences[si], displayName))
                    {
                        totalAppearances++;
                        if (HasPronounAdjacentToName(taggedSentences[si], displayName))
                            pronounAdjacentCount++;
                    }
                }

                if (totalAppearances > 0)
                {
                    double ratio = (double)pronounAdjacentCount / totalAppearances;
                    if (ratio >= 0.5)
                        toRemove.Add(key);
                }
            }

            foreach (var key in toRemove)
                candidates.Remove(key);

            return candidates
                .Select(kv => (kv.Value.DisplayName, kv.Value.Count))
                .OrderByDescending(x => x.Count)
                .ToList();
        }

        /// <summary>
        /// Starting at <paramref name="startIndex"/>, skips an optional DET token,
        /// then collects consecutive PROPN or capitalized NOUN tokens as a location name.
        /// Returns null if no proper noun is found.
        /// </summary>
        private static string? ExtractLocationName(
            IReadOnlyList<(string Value, string Pos)> tokens, int startIndex)
        {
            int i = startIndex;
            if (i >= tokens.Count) return null;

            // Skip optional determiner
            if (tokens[i].Pos == "DET")
                i++;

            return ExtractProperNounSequence(tokens, i);
        }

        /// <summary>
        /// Collects consecutive PROPN tokens or capitalized NOUN tokens starting at index.
        /// Returns null if no qualifying token is found.
        /// </summary>
        private static string? ExtractProperNounSequence(
            IReadOnlyList<(string Value, string Pos)> tokens, int startIndex)
        {
            var parts = new List<string>();
            for (int i = startIndex; i < tokens.Count; i++)
            {
                var (value, pos) = tokens[i];
                if (pos == "PROPN")
                {
                    parts.Add(value);
                }
                else if (pos == "NOUN" && value.Length > 0 && char.IsUpper(value[0]))
                {
                    parts.Add(value);
                }
                else
                {
                    break;
                }
            }

            return parts.Count > 0 ? string.Join(" ", parts) : null;
        }

        /// <summary>
        /// Checks if the candidate name appears in the token sequence.
        /// </summary>
        private static bool ContainsName(
            IReadOnlyList<(string Value, string Pos)> tokens, string candidateName)
        {
            var nameTokens = candidateName.Split(' ');
            for (int i = 0; i <= tokens.Count - nameTokens.Length; i++)
            {
                bool match = true;
                for (int j = 0; j < nameTokens.Length; j++)
                {
                    if (!tokens[i + j].Value.Equals(nameTokens[j], StringComparison.OrdinalIgnoreCase))
                    {
                        match = false;
                        break;
                    }
                }
                if (match) return true;
            }
            return false;
        }

        /// <summary>
        /// Checks if a personal pronoun appears within 2 tokens of the candidate name
        /// in the tagged sentence. This catches patterns like "He told Marcus" or
        /// "Marcus, he said" where the name is used as a character, not a location.
        /// </summary>
        private static bool HasPronounAdjacentToName(
            IReadOnlyList<(string Value, string Pos)> tokens, string candidateName)
        {
            // Find all positions where the candidate name starts
            var nameTokens = candidateName.Split(' ');
            for (int i = 0; i <= tokens.Count - nameTokens.Length; i++)
            {
                bool match = true;
                for (int j = 0; j < nameTokens.Length; j++)
                {
                    if (!tokens[i + j].Value.Equals(nameTokens[j], StringComparison.OrdinalIgnoreCase))
                    {
                        match = false;
                        break;
                    }
                }

                if (!match) continue;

                // Check 2 tokens before and after the name span for pronouns
                int nameStart = i;
                int nameEnd = i + nameTokens.Length - 1;

                for (int k = Math.Max(0, nameStart - 2); k <= Math.Min(tokens.Count - 1, nameEnd + 2); k++)
                {
                    if (k >= nameStart && k <= nameEnd) continue; // skip the name itself
                    if (tokens[k].Pos == "PRON" && PersonalPronouns.Contains(tokens[k].Value))
                        return true;
                }
            }

            return false;
        }
    }
}
