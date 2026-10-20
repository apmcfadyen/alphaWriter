using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using alphaWriter.Models;

namespace alphaWriter.Services.Nlp
{
    public static class NlpTextExtractor
    {
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
        /// Splits plain text into sentences. Sentences never cross paragraph (newline)
        /// boundaries. Handles abbreviations, quoted dialogue (including a closing quote
        /// after the terminal punctuation), and ellipses.
        /// </summary>
        public static List<string> SplitSentences(string plainText)
        {
            if (string.IsNullOrWhiteSpace(plainText)) return [];

            var sentences = new List<string>();
            foreach (var line in plainText.Split('\n'))
            {
                var paragraph = Regex.Replace(line, @"[\s\u00A0]+", " ").Trim();
                if (paragraph.Length == 0) continue;
                foreach (var (start, length) in Parsing.SentenceSplitter.Split(paragraph))
                    sentences.Add(paragraph.Substring(start, length));
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
