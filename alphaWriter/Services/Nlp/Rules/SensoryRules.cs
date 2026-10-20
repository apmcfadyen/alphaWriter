using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using alphaWriter.Models.Analysis;
using alphaWriter.Services.Nlp.Parsing;
using static alphaWriter.Services.Nlp.Rules.RuleHelpers;

namespace alphaWriter.Services.Nlp.Rules
{
    /// <summary>The six perceptual dimensions of the Lancaster norms, in table order.</summary>
    public enum Sense { Sound, Taste, Touch, Body, Smell, Sight }

    /// <summary>
    /// How strongly each word is experienced through each sense (0–5), from the Lancaster
    /// Sensorimotor Norms (Lynott et al., 2020; CC BY 4.0). "Body" is interoception:
    /// hunger, pain, a racing heart.
    /// </summary>
    public sealed class SensoryLexicon
    {
        public const string PackagePath = "lexicons/sensorimotor.tsv";

        /// <summary>A word must rate at least this in a sense to count as evoking it.</summary>
        public const float SenseThreshold = 3.5f;

        /// <summary>Words below this in every sense are abstract ("situation", "aspect").</summary>
        public const float AbstractThreshold = 2.5f;

        private readonly Dictionary<string, float[]> _ratings;

        private SensoryLexicon(Dictionary<string, float[]> ratings) => _ratings = ratings;

        public int Count => _ratings.Count;

        /// <summary>Parses "word TAB auditory TAB gustatory TAB haptic TAB interoceptive TAB olfactory TAB visual".</summary>
        public static SensoryLexicon Parse(TextReader reader)
        {
            var ratings = new Dictionary<string, float[]>(StringComparer.Ordinal);
            string? line;
            while ((line = reader.ReadLine()) is not null)
            {
                if (line.Length == 0 || line[0] == '#') continue;
                var parts = line.Split('\t');
                if (parts.Length != 7) continue;
                var values = new float[6];
                bool ok = true;
                for (int i = 0; i < 6 && ok; i++)
                    ok = float.TryParse(parts[i + 1], System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out values[i]);
                if (ok) ratings[parts[0]] = values;
            }
            return new SensoryLexicon(ratings);
        }

        /// <summary>The bundled table, once loaded.</summary>
        public static SensoryLexicon? Default { get; private set; }

        /// <summary>Loads the bundled table from the app package (once). Null if it is unavailable.</summary>
        public static async Task<SensoryLexicon?> LoadDefaultAsync()
        {
            if (Default is not null) return Default;
            try
            {
                await using var stream = await FileSystem.OpenAppPackageFileAsync(PackagePath);
                using var reader = new StreamReader(stream);
                Default = Parse(reader);
            }
            catch
            {
                // Not packaged (unit tests) or unreadable: the sensory rules are simply skipped
            }
            return Default;
        }

        private static readonly (string Suffix, string Replacement)[] Inflections =
        [
            ("ies", "y"), ("ied", "y"), ("es", ""), ("s", ""), ("ed", ""), ("ed", "e"),
            ("ing", ""), ("ing", "e"), ("ly", ""), ("er", ""), ("est", "")
        ];

        /// <summary>
        /// Ratings for a word, trying simple inflections when the form itself isn't listed
        /// ("hammered" → "hammer", "stopped" → "stop"). Null when the word is unknown.
        /// </summary>
        public float[]? Lookup(string word)
        {
            word = word.ToLowerInvariant();
            if (_ratings.TryGetValue(word, out var r)) return r;
            foreach (var (suffix, replacement) in Inflections)
            {
                if (word.Length <= suffix.Length + 2 || !word.EndsWith(suffix, StringComparison.Ordinal)) continue;
                var stem = word[..^suffix.Length] + replacement;
                if (_ratings.TryGetValue(stem, out r)) return r;
                // Doubled consonant: "stopped" → "stopp" → "stop"
                if (replacement.Length == 0 && stem.Length > 2 && stem[^1] == stem[^2]
                    && _ratings.TryGetValue(stem[..^1], out r))
                    return r;
            }
            return null;
        }

        /// <summary>
        /// The senses a word clearly evokes: rated at least <see cref="SenseThreshold"/> and
        /// close to its strongest sense, so "key" counts as sight, not touch.
        /// </summary>
        public static IEnumerable<Sense> SensesOf(float[] ratings)
        {
            var max = ratings.Max();
            for (int i = 0; i < ratings.Length; i++)
                if (ratings[i] >= SenseThreshold && ratings[i] >= max - 0.5f)
                    yield return (Sense)i;
        }
    }

    /// <summary>Sensory profile of a scene's narration.</summary>
    public static class SensoryAnalyzer
    {
        /// <summary>
        /// Words that carry no sensory detail of their own however they rate: speech tags
        /// and the perception verbs that only report a sense ("she heard", "he saw").
        /// </summary>
        private static readonly HashSet<string> Ignored = new(StringComparer.OrdinalIgnoreCase)
        {
            "said", "say", "says", "saying", "asked", "ask", "asks", "told", "tell", "tells",
            "replied", "spoke", "speak", "talk", "talked", "called", "answered",
            "see", "saw", "seen", "sees", "look", "looked", "looks", "looking", "watch", "watched",
            "hear", "heard", "hears", "feel", "felt", "feels", "notice", "noticed", "seem", "seemed",
            "someone", "something", "somebody", "anyone", "everyone", "nothing"
        };

        /// <summary>Function words, skipped when measuring how concrete a passage is.</summary>
        private static readonly HashSet<string> FunctionWords = new(StringComparer.OrdinalIgnoreCase)
        {
            "the", "a", "an", "and", "or", "but", "nor", "of", "to", "in", "on", "at", "by", "for",
            "with", "from", "as", "into", "onto", "about", "over", "under", "up", "down", "out", "off",
            "is", "was", "were", "be", "been", "being", "am", "are", "had", "has", "have", "do", "did",
            "does", "he", "she", "it", "they", "we", "i", "you", "his", "her", "its", "their", "our",
            "my", "your", "him", "them", "us", "me", "this", "that", "these", "those", "there", "here",
            "then", "than", "so", "not", "no", "if", "when", "while", "which", "who", "whom", "what",
            "would", "could", "should", "might", "must", "can", "will", "shall", "just", "very", "too",
            "also", "again", "once", "only", "some", "any", "each", "other", "more", "most", "all",
            "both", "such", "own", "same", "one", "itself", "himself", "herself", "themselves"
        };

        public static bool IsNarrationWord(TokenInfo t) => t.IsWord && !t.InQuote;

        /// <summary>Counts sensory words per sense across the scene's narration.</summary>
        public static SensoryProfile Profile(SceneDocument doc, SensoryLexicon lexicon)
        {
            var profile = new SensoryProfile();
            double strengthSum = 0;
            int rated = 0;
            foreach (var t in doc.Tokens)
            {
                if (!IsNarrationWord(t) || FunctionWords.Contains(t.Text)) continue;
                if (lexicon.Lookup(t.Text) is not { } r) continue;
                strengthSum += r.Max();
                rated++;
                if (Ignored.Contains(t.Text)) continue;
                foreach (var sense in SensoryLexicon.SensesOf(r))
                {
                    profile.Counts[(int)sense]++;
                    var list = profile.Examples[(int)sense];
                    var word = t.Lower;
                    if (list.Count < 5 && !list.Contains(word)) list.Add(word);
                }
            }
            profile.RatedWords = rated;
            profile.Concreteness = rated > 0 ? Math.Round(strengthSum / rated, 2) : 0;
            return profile;
        }

        /// <summary>
        /// How concrete one paragraph's narration is: mean strongest-sense rating of its
        /// content words, the share of abstract ones, and those abstract words.
        /// </summary>
        public static (double Mean, double AbstractShare, int Rated, List<TokenInfo> AbstractWords)
            Concreteness(SceneDocument doc, ParagraphInfo paragraph, SensoryLexicon lexicon)
        {
            double sum = 0;
            int rated = 0;
            var abstractWords = new List<TokenInfo>();
            foreach (var s in doc.Sentences.Where(s => s.ParagraphIndex == paragraph.Index))
            foreach (var t in s.Tokens)
            {
                if (!IsNarrationWord(t) || FunctionWords.Contains(t.Text)) continue;
                if (lexicon.Lookup(t.Text) is not { } r) continue;
                var max = r.Max();
                sum += max;
                rated++;
                if (max < SensoryLexicon.AbstractThreshold) abstractWords.Add(t);
            }
            return rated == 0
                ? (0, 0, 0, abstractWords)
                : (sum / rated, (double)abstractWords.Count / rated, rated, abstractWords);
        }

        public static string Name(Sense sense) => sense switch
        {
            Sense.Sound => "sound",
            Sense.Taste => "taste",
            Sense.Touch => "touch",
            Sense.Body  => "the body",
            Sense.Smell => "smell",
            _           => "sight"
        };
    }

    /// <summary>
    /// Scenes told almost entirely through the eyes. Sound, touch, smell and taste put the
    /// reader in the room; a scene with none of them can feel like watching through glass.
    /// </summary>
    public sealed class SensoryBalanceRule : ISceneRule
    {
        public RuleDescriptor Descriptor => RuleCatalog.SensoryBalance;

        private static readonly Sense[] OtherSenses = [Sense.Sound, Sense.Touch, Sense.Smell, Sense.Taste];

        public IEnumerable<NlpNote> Evaluate(SceneRuleContext ctx)
        {
            if (ctx.Senses is null) yield break;
            var narrationWords = ctx.Document.Tokens.Count(SensoryAnalyzer.IsNarrationWord);
            if (narrationWords < 300) yield break;

            var profile = SensoryAnalyzer.Profile(ctx.Document, ctx.Senses);
            var sight = profile.Counts[(int)Sense.Sight];
            var other = OtherSenses.Sum(s => profile.Counts[(int)s]);
            if (sight + other < 10 || other > (sight + other) * 0.15) yield break;

            var missing = OtherSenses.Where(s => profile.Counts[(int)s] == 0).Select(SensoryAnalyzer.Name).ToList();
            var detail = missing.Count == OtherSenses.Length
                ? "nothing heard, touched, smelled or tasted"
                : $"no {JoinOr(missing)}";
            var note = Note(Descriptor, NlpNoteSeverity.Info,
                $"This scene is almost all sight: {sight} visual words ({string.Join(", ", profile.Examples[(int)Sense.Sight].Take(3))}…) " +
                $"and {detail}. A sound, a texture or a smell can put the reader in the room.");
            note.Evidence = string.Join(", ", profile.Examples[(int)Sense.Sight]);
            yield return note;
        }

        private static string JoinOr(List<string> items) =>
            items.Count <= 1 ? string.Concat(items) : string.Join(", ", items.Take(items.Count - 1)) + " or " + items[^1];
    }

    /// <summary>
    /// Narration paragraphs built from abstract words ("situation", "decided", "important")
    /// with little a reader can see, hear or touch: often telling where showing would land.
    /// Backstory paragraphs are left to the info-dump rule.
    /// </summary>
    public sealed class AbstractPassageRule : ISceneRule
    {
        public RuleDescriptor Descriptor => RuleCatalog.AbstractPassages;

        public IEnumerable<NlpNote> Evaluate(SceneRuleContext ctx)
        {
            if (ctx.Senses is null) yield break;
            var doc = ctx.Document;
            int flagged = 0;
            foreach (var p in doc.Paragraphs)
            {
                if (p.Kind == ParagraphKind.Dialogue || p.SentenceCount == 0) continue;
                if (BeatClassifier.Classify(doc, p) == Beat.Exposition) continue;
                var (mean, abstractShare, rated, words) = SensoryAnalyzer.Concreteness(doc, p, ctx.Senses);
                if (rated < 15 || mean >= 2.9 || abstractShare < 0.3) continue;

                var sample = words.Select(w => w.Lower).Distinct().Take(4).ToList();
                var first = doc.Sentences[p.FirstSentenceIndex];
                yield return Note(Descriptor, NlpNoteSeverity.Info,
                        $"An abstract paragraph: words like {string.Join(", ", sample.Select(w => $"“{w}”"))} " +
                        "give the reader little to see, hear or touch. If this is a moment to feel rather than " +
                        "summarize, try anchoring it in a concrete image or action.")
                    .At(first)
                    .Also(words.Take(8));
                if (++flagged >= 3) yield break;
            }
        }
    }
}
