using System;
using System.Collections.Generic;
using System.Linq;
using alphaWriter.Services.Nlp.Parsing;

namespace alphaWriter.Services.Nlp.Rules
{
    /// <summary>
    /// Matches multi-word phrases against a sentence's words, tolerant of inflection and
    /// pronouns, so "nodded his head", "nods her head" and "nodding their head" are all one
    /// pattern. Phrases never span punctuation (a comma or quote ends the run).
    /// </summary>
    /// <remarks>
    /// Pattern elements:
    /// <list type="bullet">
    /// <item><c>~walk</c> — any form of the word family (compared by stem)</item>
    /// <item><c>POSS</c> — his/her/their/my/your/its/our</item>
    /// <item><c>SUBJ</c> — he/she/they/I/we/you</item>
    /// <item><c>REFL</c> — himself/herself/themselves/myself/yourself/ourselves</item>
    /// <item><c>DET</c> — a/an/the</item>
    /// <item>anything else — that exact word (case-insensitive, either apostrophe)</item>
    /// </list>
    /// </remarks>
    public sealed class PhrasePattern
    {
        private static readonly HashSet<string> Poss = new(StringComparer.OrdinalIgnoreCase)
            { "his", "her", "their", "my", "your", "its", "our" };
        private static readonly HashSet<string> Subj = new(StringComparer.OrdinalIgnoreCase)
            { "he", "she", "they", "i", "we", "you" };
        private static readonly HashSet<string> Refl = new(StringComparer.OrdinalIgnoreCase)
            { "himself", "herself", "themselves", "myself", "yourself", "ourselves", "itself" };
        private static readonly HashSet<string> Det = new(StringComparer.OrdinalIgnoreCase)
            { "a", "an", "the" };

        private readonly Func<TokenInfo, bool>[] _elements;

        public PhrasePattern(params string[] elements)
        {
            Elements = elements;
            _elements = elements.Select(Compile).ToArray();
        }

        public IReadOnlyList<string> Elements { get; }
        public int Length => _elements.Length;

        private static Func<TokenInfo, bool> Compile(string element)
        {
            if (element.StartsWith('~'))
            {
                var stem = WordStemmer.Stem(element[1..]);
                return t => t.Stem == stem;
            }
            return element switch
            {
                "POSS" => t => Poss.Contains(t.Text),
                "SUBJ" => t => Subj.Contains(t.Text),
                "REFL" => t => Refl.Contains(t.Text),
                "DET" => t => Det.Contains(t.Text),
                _ => t => string.Equals(Normalize(t.Text), element, StringComparison.OrdinalIgnoreCase)
            };
        }

        private static string Normalize(string word) => word.Replace('’', '\'');

        /// <summary>True when the pattern matches the words starting at <paramref name="start"/>.</summary>
        public bool MatchesAt(IReadOnlyList<TokenInfo> words, int start)
        {
            if (start + _elements.Length > words.Count) return false;
            for (int i = 0; i < _elements.Length; i++)
                if (!_elements[i](words[start + i])) return false;
            return true;
        }
    }

    internal static class PhraseScanner
    {
        /// <summary>
        /// Splits a sentence into runs of consecutive words, breaking at punctuation so a
        /// phrase cannot jump across a comma, dash or quote mark.
        /// </summary>
        public static IEnumerable<List<TokenInfo>> WordRuns(SentenceInfo sentence, bool narrationOnly)
        {
            var run = new List<TokenInfo>();
            foreach (var t in sentence.Tokens)
            {
                if (t.IsWord && !(narrationOnly && t.InQuote))
                {
                    run.Add(t);
                    continue;
                }
                if (run.Count > 0) yield return run;
                run = [];
            }
            if (run.Count > 0) yield return run;
        }

        /// <summary>All non-overlapping matches of any pattern, earliest first.</summary>
        public static IEnumerable<(int PatternIndex, List<TokenInfo> Words)> Find(
            SentenceInfo sentence, IReadOnlyList<PhrasePattern> patterns, bool narrationOnly)
        {
            foreach (var run in WordRuns(sentence, narrationOnly))
            {
                for (int i = 0; i < run.Count; i++)
                {
                    for (int p = 0; p < patterns.Count; p++)
                    {
                        if (!patterns[p].MatchesAt(run, i)) continue;
                        yield return (p, run.GetRange(i, patterns[p].Length));
                        i += patterns[p].Length - 1;
                        break;
                    }
                }
            }
        }
    }

    /// <summary>The four forms needed to swap one verb phrase for another in the same tense.</summary>
    public sealed record VerbForms(string Base, string Past, string ThirdPerson, string Ing)
    {
        private static readonly HashSet<string> IrregularPasts = new(StringComparer.OrdinalIgnoreCase)
            { "made", "gave", "took", "came", "had", "let", "got", "went", "did" };

        /// <summary>Picks the form matching the tense of <paramref name="verb"/> ("gave" → Past).</summary>
        public string Matching(string verb)
        {
            var v = verb.ToLowerInvariant();
            if (v.EndsWith("ing")) return Ing;
            if (IrregularPasts.Contains(v) || v.EndsWith("ed")) return Past;
            if (v.EndsWith('s')) return ThirdPerson;
            return Base;
        }
    }
}
