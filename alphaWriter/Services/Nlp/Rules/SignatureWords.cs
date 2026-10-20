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
    /// <summary>
    /// How often words appear in typical fiction, from the bundled table built by
    /// tools/reference-corpus (84 public-domain novels). Counts are grouped by word
    /// family so "glance", "glanced" and "glancing" compare as one.
    /// </summary>
    public sealed class ReferenceFrequencies
    {
        public const string PackagePath = "lexicons/fiction-frequencies.tsv";

        private readonly Dictionary<string, long> _byStem;

        private ReferenceFrequencies(Dictionary<string, long> byStem, long total)
        {
            _byStem = byStem;
            Total = total;
        }

        /// <summary>Words in the reference corpus.</summary>
        public long Total { get; }

        public long CountOf(string stem) => _byStem.GetValueOrDefault(stem);

        /// <summary>Parses the "word TAB count" table; "#total TAB n" sets the corpus size.</summary>
        public static ReferenceFrequencies Parse(TextReader reader)
        {
            var byStem = new Dictionary<string, long>(StringComparer.Ordinal);
            long total = 0;
            string? line;
            while ((line = reader.ReadLine()) is not null)
            {
                var tab = line.IndexOf('\t');
                if (tab <= 0) continue;
                if (!long.TryParse(line.AsSpan(tab + 1), out var n)) continue;
                var word = line[..tab];
                if (word == "#total") { total = n; continue; }
                if (word.StartsWith('#')) continue;
                var stem = WordStemmer.Stem(word);
                byStem[stem] = byStem.GetValueOrDefault(stem) + n;
            }
            if (total == 0) total = byStem.Values.Sum();
            return new ReferenceFrequencies(byStem, total);
        }

        /// <summary>The bundled table, once loaded.</summary>
        public static ReferenceFrequencies? Default { get; private set; }

        /// <summary>Loads the bundled table from the app package (once). Null if it is unavailable.</summary>
        public static async Task<ReferenceFrequencies?> LoadDefaultAsync()
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
                // Not packaged (unit tests) or unreadable: signature words are simply skipped
            }
            return Default;
        }
    }

    /// <summary>A word the author uses far more often than typical fiction does.</summary>
    /// <param name="Word">The form the author uses most ("glanced").</param>
    /// <param name="Count">Uses in the book (all forms).</param>
    /// <param name="Ratio">How many times more often than in typical fiction.</param>
    /// <param name="Score">Log-likelihood (G²): how sure we are it isn't chance.</param>
    public sealed record SignatureWord(string Word, int Count, double Ratio, double Score);

    public static class SignatureWords
    {
        /// <summary>G² above this is significant at p &lt; 0.0001.</summary>
        private const double MinScore = 15.13;
        private const double MinRatio = 2.5;
        private const int MinCount = 5;

        /// <summary>
        /// Ranks word families by how distinctive they are to this author (Dunning's
        /// log-likelihood against the reference corpus). Words the reference has barely
        /// seen are skipped: they're modern vocabulary or invented names, not habits.
        /// </summary>
        public static List<SignatureWord> Find(IEnumerable<string> words, ReferenceFrequencies reference,
            ISet<string>? excluded = null, int max = 20)
        {
            var byStem = new Dictionary<string, (int Count, Dictionary<string, int> Forms)>(StringComparer.Ordinal);
            long total = 0;
            foreach (var raw in words)
            {
                var w = raw.ToLowerInvariant().Replace('’', '\'');
                if (w.Length == 0) continue;
                total++;
                if (w.Length < 3 || !w.All(char.IsLetter)) continue;
                if (Lexicons.Stopwords.Contains(w) || excluded?.Contains(w) == true) continue;

                var stem = WordStemmer.Stem(w);
                if (!byStem.TryGetValue(stem, out var entry)) entry = (0, new Dictionary<string, int>());
                entry.Forms[w] = entry.Forms.GetValueOrDefault(w) + 1;
                byStem[stem] = (entry.Count + 1, entry.Forms);
            }
            if (total == 0) return [];

            var found = new List<SignatureWord>();
            foreach (var (stem, (a, forms)) in byStem)
            {
                if (a < MinCount) continue;
                long b = reference.CountOf(stem);
                if (b < 10) continue;

                double c = total, d = reference.Total;
                double ratio = (a / c) / (b / d);
                if (ratio < MinRatio) continue;

                double e1 = c * (a + b) / (c + d);
                double e2 = d * (a + b) / (c + d);
                double g2 = 2 * (a * Math.Log(a / e1) + b * Math.Log(b / e2));
                if (g2 < MinScore) continue;

                var display = forms.OrderByDescending(f => f.Value).First().Key;
                found.Add(new SignatureWord(display, a, ratio, g2));
            }
            return found.OrderByDescending(s => s.Score).Take(max).ToList();
        }
    }

    /// <summary>
    /// The author's signature words, measured against typical fiction ("glanced: 11× more
    /// than typical fiction, 34 uses"). Some are voice; others are habits worth thinning.
    /// </summary>
    public sealed class SignatureWordsRule : IBookRule
    {
        public RuleDescriptor Descriptor => RuleCatalog.SignatureWords;

        public IEnumerable<NlpNote> Evaluate(BookRuleContext ctx)
        {
            if (ctx.Reference is null || ctx.Results.Count == 0) yield break;

            var excluded = SceneRuleContext.BuildEntityWords(ctx.Book);
            foreach (var allowed in ctx.StyleSheet.AllowedWords) excluded.Add(allowed.ToLowerInvariant());

            var words = ctx.Results
                .SelectMany(r => r.Sentences)
                .SelectMany(s => s.Text.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries))
                .Select(w => w.Trim('.', ',', '!', '?', ';', ':', '"', '\'', '“', '”', '‘', '’', '(', ')', '—', '-'));

            var signature = SignatureWords.Find(words, ctx.Reference, excluded, max: 10);
            if (signature.Count == 0) yield break;

            var list = string.Join(", ", signature.Select(s => $"“{s.Word}” ({s.Ratio:0.#}×, {s.Count} uses)"));
            var note = Note(Descriptor, NlpNoteSeverity.Info,
                $"Your signature words, used far more often than in typical fiction: {list}. " +
                "Some of these are your voice; keep those. Others are habits that readers start to notice " +
                "by the third chapter. Insights › Signature words has the full list.");
            note.Evidence = string.Join(",", signature.Select(s => s.Word));
            yield return note;
        }
    }
}
