using System;
using System.Collections.Generic;
using System.Linq;
using alphaWriter.Models;
using alphaWriter.Models.Analysis;
using alphaWriter.Services.Nlp.Parsing;

namespace alphaWriter.Services.Nlp.Rules
{
    /// <summary>What a rule needs in order to run. Lower tiers are cheaper.</summary>
    public enum RuleTier
    {
        /// <summary>Words and punctuation only — fast enough to run while the author types.</summary>
        Lexical,
        /// <summary>Needs part-of-speech tags (Catalyst).</summary>
        Pos,
        /// <summary>Needs a downloaded ONNX model (embeddings, emotions).</summary>
        Model,
        /// <summary>Compares scenes/chapters across the whole book.</summary>
        Book
    }

    /// <summary>The unit a rule's notes are computed over.</summary>
    public enum RuleScope { Scene, Chapter, Book }

    /// <summary>Static metadata for a rule — what the settings panel and note cards show.</summary>
    public sealed record RuleDescriptor(
        string Id,
        string Title,
        string Description,
        NlpNoteCategory Category,
        RuleTier Tier,
        RuleScope Scope);

    public interface IProseRule
    {
        RuleDescriptor Descriptor { get; }
    }

    /// <summary>A rule that inspects one scene.</summary>
    public interface ISceneRule : IProseRule
    {
        IEnumerable<NlpNote> Evaluate(SceneRuleContext context);
    }

    /// <summary>A rule that compares scenes across the book.</summary>
    public interface IBookRule : IProseRule
    {
        IEnumerable<NlpNote> Evaluate(BookRuleContext context);
    }

    public sealed class SceneRuleContext
    {
        public required SceneDocument Document { get; init; }
        public string SceneId { get; init; } = string.Empty;
        public string SceneTitle { get; init; } = string.Empty;
        public string ChapterTitle { get; init; } = string.Empty;
        public EditorialStyleSheet StyleSheet { get; init; } = new();
        public Book? Book { get; init; }
        public IReadOnlyList<string> ViewpointCharacterIds { get; init; } = [];

        /// <summary>Characters the author tagged as appearing in this scene.</summary>
        public IReadOnlyList<string> TaggedCharacterIds { get; init; } = [];

        /// <summary>Precomputed style profile for the scene (sentence length stats etc.).</summary>
        public StyleProfile Style { get; init; } = new();

        /// <summary>Precomputed pacing metrics for the scene.</summary>
        public PacingMetrics Pacing { get; init; } = new();

        private HashSet<string>? _entityWords;

        /// <summary>
        /// Lowercased words from every character, location and item name and alias.
        /// Rules skip these so a protagonist's name is never an "echo".
        /// </summary>
        public HashSet<string> EntityWords => _entityWords ??= BuildEntityWords(Book);

        private Dictionary<string, Character>? _characterWords;

        /// <summary>
        /// Lowercased name/alias word → character, for words that belong to exactly one
        /// character ("maya", "lindqvist", "tom"). Shared words are left out as ambiguous.
        /// </summary>
        public IReadOnlyDictionary<string, Character> CharacterWords => _characterWords ??= BuildCharacterWords(Book);

        /// <summary>The scene's viewpoint character when there is exactly one; otherwise null.</summary>
        public Character? SingleViewpoint =>
            ViewpointCharacterIds.Count == 1
                ? Book?.Characters.FirstOrDefault(c => c.Id == ViewpointCharacterIds[0])
                : null;

        public double Threshold(string ruleId, string setting, double defaultValue) =>
            StyleSheet.GetThreshold(ruleId, setting, defaultValue);

        internal static Dictionary<string, Character> BuildCharacterWords(Book? book)
        {
            var map = new Dictionary<string, Character>(StringComparer.OrdinalIgnoreCase);
            var shared = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (book is null) return map;

            foreach (var c in book.Characters)
            {
                foreach (var name in new[] { c.Name, c.FullName, c.Aka })
                {
                    if (string.IsNullOrWhiteSpace(name)) continue;
                    foreach (var part in name.Split([',', ';', ' ', '\t'], StringSplitOptions.RemoveEmptyEntries))
                    {
                        var word = part.Trim().Trim('"', '\'');
                        if (word.Length < 2 || !char.IsUpper(word[0])) continue; // "the" in "the harbourmaster's daughter"
                        if (map.TryGetValue(word, out var other) && other != c) shared.Add(word);
                        else map[word] = c;
                    }
                }
            }
            foreach (var word in shared) map.Remove(word);
            return map;
        }

        public static HashSet<string> BuildEntityWords(Book? book)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (book is null) return set;

            void AddNames(params string?[] names)
            {
                foreach (var n in names)
                {
                    if (string.IsNullOrWhiteSpace(n)) continue;
                    foreach (var part in n.Split([',', ';', ' ', '\t'], StringSplitOptions.RemoveEmptyEntries))
                        set.Add(part.Trim().Trim('"', '\'').ToLowerInvariant());
                }
            }

            foreach (var c in book.Characters) AddNames(c.Name, c.FullName, c.Aka);
            foreach (var l in book.Locations) AddNames(l.Name, l.Aka);
            foreach (var i in book.Items) AddNames(i.Name, i.Aka);
            return set;
        }
    }

    public sealed class BookRuleContext
    {
        public required IReadOnlyList<SceneAnalysisResult> Results { get; init; }
        public Book? Book { get; init; }
        public EditorialStyleSheet StyleSheet { get; init; } = new();

        /// <summary>Typical-fiction word frequencies, when the bundled table is available.</summary>
        public ReferenceFrequencies? Reference { get; init; }
    }

    /// <summary>Helpers shared by rule implementations.</summary>
    internal static class RuleHelpers
    {
        public static NlpNote Note(RuleDescriptor rule, NlpNoteSeverity severity, string message) => new()
        {
            RuleId = rule.Id,
            Category = rule.Category,
            Severity = severity,
            Message = message
        };

        /// <summary>Points a note at one token (primary span).</summary>
        public static NlpNote At(this NlpNote note, TokenInfo token)
        {
            note.Start = token.Start;
            note.Length = token.Length;
            note.Evidence = token.Text;
            return note;
        }

        /// <summary>Points a note at the range from <paramref name="first"/> through <paramref name="last"/>.</summary>
        public static NlpNote At(this NlpNote note, SceneDocument doc, TokenInfo first, TokenInfo last)
        {
            note.Start = first.Start;
            note.Length = last.End - first.Start;
            note.Evidence = doc.Text.Substring(first.Start, last.End - first.Start);
            return note;
        }

        /// <summary>Points a note at a whole sentence.</summary>
        public static NlpNote At(this NlpNote note, SentenceInfo sentence)
        {
            note.Start = sentence.Start;
            note.Length = sentence.Length;
            note.SentenceIndex = sentence.Index;
            return note;
        }

        /// <summary>Adds a one-click replacement for the note's primary span.</summary>
        public static NlpNote WithSuggestion(this NlpNote note, string replacement)
        {
            note.Suggestions.Add(replacement);
            return note;
        }

        public static NlpNote Also(this NlpNote note, IEnumerable<TokenInfo> tokens)
        {
            foreach (var t in tokens)
                note.ExtraSpans.Add(new TextSpan(t.Start, t.Length));
            return note;
        }

        public static string Truncate(string s, int maxLen) =>
            s.Length <= maxLen ? s : s[..maxLen] + "…";

        public static double StdDev(IReadOnlyList<double> values, double mean) =>
            values.Count > 1
                ? Math.Sqrt(values.Sum(v => Math.Pow(v - mean, 2)) / (values.Count - 1))
                : 0;
    }
}
