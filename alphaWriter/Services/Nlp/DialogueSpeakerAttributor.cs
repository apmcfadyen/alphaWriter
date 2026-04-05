using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using alphaWriter.Models;
using alphaWriter.Models.Analysis;

namespace alphaWriter.Services.Nlp
{
    public static class DialogueSpeakerAttributor
    {
        private static readonly string SpeechVerbs =
            "said|asked|whispered|shouted|replied|exclaimed|muttered|called|" +
            "cried|yelled|demanded|answered|added|continued|began|insisted|suggested|remarked|" +
            "observed|noted|explained|declared|announced|stated|murmured|growled|hissed|snapped|" +
            "snarled|barked|roared|screamed|sobbed|sighed|laughed|chuckled|giggled";

        /// <summary>
        /// Builds a case-insensitive lookup from character names/aliases to character IDs.
        /// Longer names are tried first during matching to avoid partial hits.
        /// </summary>
        public static Dictionary<string, string> BuildNameLookup(IReadOnlyList<Character> characters)
        {
            var lookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var character in characters)
            {
                if (!string.IsNullOrWhiteSpace(character.Name))
                    lookup.TryAdd(character.Name.Trim(), character.Id);

                if (!string.IsNullOrWhiteSpace(character.FullName))
                    lookup.TryAdd(character.FullName.Trim(), character.Id);

                if (!string.IsNullOrWhiteSpace(character.Aka))
                {
                    foreach (var alias in character.Aka.Split(',', StringSplitOptions.RemoveEmptyEntries))
                    {
                        var trimmed = alias.Trim();
                        if (trimmed.Length > 0)
                            lookup.TryAdd(trimmed, character.Id);
                    }
                }
            }

            return lookup;
        }

        /// <summary>
        /// Attributes dialogue sentences to characters using speech tag heuristics.
        /// Only sentences with explicit speech attribution tags are matched.
        /// </summary>
        public static List<DialogueAttribution> AttributeDialogue(
            List<SentenceAnalysis> sentences,
            string sceneId,
            IReadOnlyDictionary<string, string> nameLookup,
            IReadOnlyList<Character> characters)
        {
            var attributions = new List<DialogueAttribution>();

            if (sentences.Count == 0 || nameLookup.Count == 0)
                return attributions;

            // Sort names longest-first to avoid partial matches
            var sortedNames = nameLookup.Keys.OrderByDescending(n => n.Length).ToList();

            // Build the name alternation pattern (escaped, longest first)
            var namePattern = string.Join("|", sortedNames.Select(Regex.Escape));

            // Post-dialogue: "dialogue," Name verb  or  "dialogue" Name verb
            // Matches closing quote (straight " or smart \u201D) followed by optional comma, then name + speech verb
            var postDialogue = new Regex(
                "[\"\u201D]\\s*,?\\s*(" + namePattern + ")\\s+(?:" + SpeechVerbs + ")\\b",
                RegexOptions.Compiled | RegexOptions.IgnoreCase);

            // Pre-dialogue: Name verb, "dialogue"  or  Name verb "dialogue"
            // Matches name + speech verb followed by optional comma, then opening quote
            var preDialogue = new Regex(
                "\\b(" + namePattern + ")\\s+(?:" + SpeechVerbs + ")\\s*,?\\s*[\"\u201C]",
                RegexOptions.Compiled | RegexOptions.IgnoreCase);

            // Build reverse lookup from ID to character for name resolution
            var idToCharacter = characters.ToDictionary(c => c.Id, c => c);

            foreach (var sentence in sentences)
            {
                // Check for any quoted content (straight or smart quotes)
                var text = sentence.Text;
                if (!text.Contains('"') && !text.Contains('\u201C') && !text.Contains('\u201D'))
                    continue;

                string? matchedCharId = null;

                // Try post-dialogue pattern first (more common in English prose)
                var postMatch = postDialogue.Match(sentence.Text);
                if (postMatch.Success)
                {
                    var name = postMatch.Groups[1].Value;
                    if (nameLookup.TryGetValue(name, out var charId))
                        matchedCharId = charId;
                }

                // Try pre-dialogue pattern if no post-dialogue match
                if (matchedCharId == null)
                {
                    var preMatch = preDialogue.Match(sentence.Text);
                    if (preMatch.Success)
                    {
                        var name = preMatch.Groups[1].Value;
                        if (nameLookup.TryGetValue(name, out var charId))
                            matchedCharId = charId;
                    }
                }

                if (matchedCharId != null && idToCharacter.TryGetValue(matchedCharId, out var character))
                {
                    var dialogueText = ExtractDialogueText(sentence.Text);
                    if (!string.IsNullOrWhiteSpace(dialogueText))
                    {
                        attributions.Add(new DialogueAttribution
                        {
                            CharacterId = matchedCharId,
                            CharacterName = character.Name,
                            DialogueText = dialogueText,
                            SceneId = sceneId,
                            SentenceIndex = sentence.Index
                        });
                    }
                }
            }

            return attributions;
        }

        /// <summary>
        /// Extracts the quoted dialogue text from a sentence, stripping outer quotes.
        /// </summary>
        public static string ExtractDialogueText(string sentence)
        {
            // Match content between opening and closing quotes (straight or smart)
            var match = Regex.Match(sentence, "[\"\u201C](.+?)[\"\u201D]");
            return match.Success ? match.Groups[1].Value.Trim() : string.Empty;
        }
    }
}
