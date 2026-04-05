using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using alphaWriter.Models;

namespace alphaWriter.Services.Nlp
{
    public static class NlpTextExtractor
    {
        // Common abbreviations that should not trigger sentence breaks.
        private static readonly HashSet<string> _abbreviations = new(StringComparer.OrdinalIgnoreCase)
        {
            "mr", "mrs", "ms", "dr", "prof", "sr", "jr", "st", "ave", "blvd",
            "dept", "est", "vol", "vs", "etc", "inc", "ltd", "corp", "co",
            "gen", "gov", "sgt", "cpl", "pvt", "capt", "lt", "col", "cmdr",
            "adm", "rev", "hon", "pres", "sec", "treas", "amb"
        };

        private static readonly Regex _sentenceBoundary = new(
            @"(?<=[.!?])\s+(?=[A-Z""\u201C])",
            RegexOptions.Compiled);

        /// <summary>
        /// Converts scene HTML content to plain text, reusing Scene's existing pipeline.
        /// </summary>
        public static string ExtractPlainText(string htmlContent)
        {
            if (string.IsNullOrWhiteSpace(htmlContent)) return string.Empty;
            return Scene.DecodeEntities(
                Scene.StripComments(
                    Scene.StripHtml(
                        Scene.DecodeUnicodeEscapes(htmlContent))));
        }

        /// <summary>
        /// Splits plain text into sentences. Handles abbreviations, quoted dialogue,
        /// ellipsis, and other common patterns in creative writing.
        /// </summary>
        public static List<string> SplitSentences(string plainText)
        {
            if (string.IsNullOrWhiteSpace(plainText)) return [];

            // Normalize whitespace
            var text = Regex.Replace(plainText.Trim(), @"\s+", " ");

            var sentences = new List<string>();
            var candidates = _sentenceBoundary.Split(text);

            // Merge fragments that ended on an abbreviation
            var merged = new List<string>();
            foreach (var candidate in candidates)
            {
                var trimmed = candidate.Trim();
                if (trimmed.Length == 0) continue;

                if (merged.Count > 0 && EndsWithAbbreviation(merged[^1]))
                {
                    merged[^1] = merged[^1] + " " + trimmed;
                }
                else
                {
                    merged.Add(trimmed);
                }
            }

            foreach (var s in merged)
            {
                var trimmed = s.Trim();
                if (trimmed.Length > 0)
                    sentences.Add(trimmed);
            }

            return sentences;
        }

        /// <summary>
        /// Splits plain text into paragraphs (by newline boundaries).
        /// </summary>
        public static List<string> SplitParagraphs(string plainText)
        {
            if (string.IsNullOrWhiteSpace(plainText)) return [];

            var paragraphs = new List<string>();
            foreach (var line in plainText.Split('\n'))
            {
                var trimmed = line.Trim();
                if (trimmed.Length > 0)
                    paragraphs.Add(trimmed);
            }
            return paragraphs;
        }

        /// <summary>
        /// Checks whether text ends with a known abbreviation followed by a period.
        /// </summary>
        private static bool EndsWithAbbreviation(string text)
        {
            if (!text.EndsWith('.')) return false;

            // Walk backward to find the last word before the period
            int end = text.Length - 2; // skip the '.'
            if (end < 0) return false;

            int start = end;
            while (start > 0 && char.IsLetter(text[start - 1]))
                start--;

            var lastWord = text[start..(end + 1)];
            return _abbreviations.Contains(lastWord);
        }

        private static readonly Regex _dialogueAttribution = new(
            "[\"\u201D]\\s*(?:said|asked|whispered|shouted|replied|exclaimed|muttered|called|" +
            "cried|yelled|demanded|answered|added|continued|began|insisted|suggested|remarked|" +
            "observed|noted|explained|declared|announced|stated|murmured|growled|hissed|snapped|" +
            "snarled|barked|roared|screamed|sobbed|sighed|laughed|chuckled|giggled)\\b",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary>
        /// Detects whether a sentence is dialogue (starts/ends with quotes or
        /// contains a dialogue attribution pattern).
        /// </summary>
        public static bool IsDialogue(string sentence)
        {
            var s = sentence.Trim();
            if (s.Length < 2) return false;

            char first = s[0];
            // Starts with opening quote
            if (first == '"' || first == '\u201C') return true;

            // Contains a speech verb after a closing quote
            if (s.Contains('\u201D') || s.Contains('"'))
            {
                if (_dialogueAttribution.IsMatch(s))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Detects whether a sentence contains a contraction (e.g., don't, I'm, we've).
        /// </summary>
        public static bool HasContraction(string sentence)
        {
            return Regex.IsMatch(sentence, @"\b\w+['\u2019]\w+\b");
        }
    }
}
