using System.Globalization;
using System.Net;
using System.Text;
using alphaWriter.Models;
using alphaWriter.Models.Analysis;
using alphaWriter.Services.Nlp.Rules;

namespace alphaWriter.Services.Nlp
{
    // ── Genre guidelines ─────────────────────────────────────────────────────

    /// <summary>A rule-of-thumb range for one book measurement in one genre.</summary>
    public sealed record Benchmark(string Id, string Label, string Unit, double Min, double Max, string Advice);

    public enum BenchmarkVerdict { Below, Within, Above }

    public sealed record BenchmarkRow(Benchmark Benchmark, double Value, BenchmarkVerdict Verdict)
    {
        public string ValueText => GenreBenchmarks.Format(Benchmark, Value);
        public string RangeText => $"{GenreBenchmarks.Format(Benchmark, Benchmark.Min)}–{GenreBenchmarks.Format(Benchmark, Benchmark.Max)}";
    }

    /// <summary>
    /// Typical ranges for common genres. These are craft rules of thumb (the kind of
    /// figures agents and writing guides quote), not measurements of published books:
    /// a book outside a range isn't wrong, but it's worth knowing why.
    /// </summary>
    public static class GenreBenchmarks
    {
        private static readonly Dictionary<string, (double[] BookK, double[] Chapter, double[] Sentence, double[] Dialogue, double[] Ease)> Ranges = new()
        {
            //                   book (k words)  chapter words    sentence words  dialogue share   reading ease
            ["General"]      = ([70, 100],  [2000, 5000], [12, 20], [0.25, 0.50], [60, 80]),
            ["Literary"]     = ([70, 110],  [3000, 6000], [14, 24], [0.15, 0.40], [50, 70]),
            ["Thriller"]     = ([70, 100],  [1500, 3500], [9, 15],  [0.30, 0.55], [65, 85]),
            ["Romance"]      = ([50, 90],   [2000, 4000], [11, 17], [0.35, 0.60], [65, 85]),
            ["Fantasy / SF"] = ([90, 130],  [3000, 6000], [12, 20], [0.25, 0.45], [55, 75]),
            ["Young Adult"]  = ([60, 90],   [2000, 4000], [10, 16], [0.35, 0.55], [70, 90]),
        };

        public static IReadOnlyList<Benchmark> For(string? genre)
        {
            var r = Ranges.TryGetValue(genre ?? string.Empty, out var found) ? found : Ranges["General"];
            return
            [
                new("book-words", "Book length", "k words", r.BookK[0], r.BookK[1],
                    "Length expectations are firmest for debut novels."),
                new("chapter-words", "Average chapter", "words", r.Chapter[0], r.Chapter[1],
                    "Shorter chapters read faster; longer ones give room to breathe."),
                new("sentence-words", "Average sentence", "words", r.Sentence[0], r.Sentence[1],
                    "Variety matters more than the average."),
                new("dialogue-share", "Dialogue", "%", r.Dialogue[0], r.Dialogue[1],
                    "Share of sentences that are spoken."),
                new("reading-ease", "Reading ease", "Flesch", r.Ease[0], r.Ease[1],
                    "Higher is easier: short words, short sentences."),
            ];
        }

        public static string Format(Benchmark b, double v) => b.Unit switch
        {
            "%" => $"{v * 100:0}%",
            "k words" => $"{v:0.#}k",
            "words" when v >= 100 => v.ToString("#,0", CultureInfo.InvariantCulture),
            _ => v.ToString("0.#", CultureInfo.InvariantCulture)
        };

        /// <summary>The book's measurements against its genre's ranges. Unmeasurable rows are left out.</summary>
        public static List<BenchmarkRow> Compare(string? genre, Book book, IReadOnlyList<SceneAnalysisResult> results)
        {
            var values = BookMetrics.Compute(book, results);
            return For(genre)
                .Where(b => values.ContainsKey(b.Id))
                .Select(b =>
                {
                    var v = values[b.Id];
                    var verdict = v < b.Min ? BenchmarkVerdict.Below : v > b.Max ? BenchmarkVerdict.Above : BenchmarkVerdict.Within;
                    return new BenchmarkRow(b, v, verdict);
                })
                .ToList();
        }
    }

    /// <summary>Whole-book measurements, weighted so long scenes count for more.</summary>
    public static class BookMetrics
    {
        public static Dictionary<string, double> Compute(Book book, IReadOnlyList<SceneAnalysisResult> results)
        {
            var values = new Dictionary<string, double>();
            var drafted = results.Where(r => r.Style.TotalWords > 0).ToList();
            int words = drafted.Sum(r => r.Style.TotalWords);
            if (words == 0) return values;

            values["book-words"] = words / 1000.0;
            var chapterWords = drafted.GroupBy(r => r.ChapterTitle).Select(g => g.Sum(r => r.Style.TotalWords)).ToList();
            values["chapter-words"] = chapterWords.Average();

            int sentences = drafted.Sum(r => r.Style.TotalSentences);
            if (sentences > 0)
            {
                values["sentence-words"] = (double)words / sentences;
                values["dialogue-share"] = drafted.Sum(r => r.Style.DialogueRatio * r.Style.TotalSentences) / sentences;
            }
            values["reading-ease"] = drafted.Sum(r => r.Style.ReadabilityScore * r.Style.TotalWords) / words;
            return values;
        }
    }

    // ── Scene report card ────────────────────────────────────────────────────

    /// <summary>One coloured segment of a strip chart (beats or senses).</summary>
    public sealed record StripSegment(string Label, string Color, double Weight)
    {
        /// <summary>Weight as FlexLayout.Grow expects it.</summary>
        public float Grow => (float)Weight;
        public string Tip => Weight == 1 ? Label : $"{Label} ({Weight:0})";
    }

    /// <summary>A scene at a glance: its shape, senses, tension and notes.</summary>
    public sealed class SceneReportCard
    {
        public string SceneId { get; init; } = string.Empty;
        public string Stats { get; init; } = string.Empty;
        public List<StripSegment> Beats { get; init; } = [];
        public string BeatSummary { get; init; } = string.Empty;
        public List<StripSegment> Senses { get; init; } = [];
        public string SenseSummary { get; init; } = string.Empty;
        public string TensionSummary { get; init; } = string.Empty;
        public string NoteSummary { get; init; } = string.Empty;

        public bool HasBeats => Beats.Count > 0;
        public bool HasSenses => Senses.Count > 0;
        public bool HasTension => TensionSummary.Length > 0;

        public static readonly IReadOnlyDictionary<string, string> BeatColors = new Dictionary<string, string>
        {
            ["Dialogue"] = "#61AFEF", ["Action"] = "#E06C75", ["Interiority"] = "#C678DD",
            ["Description"] = "#98C379", ["Exposition"] = "#D19A66"
        };

        public static readonly string[] SenseNames = ["Sound", "Taste", "Touch", "Body", "Smell", "Sight"];
        public static readonly string[] SenseColors = ["#E5C07B", "#C678DD", "#D19A66", "#E06C75", "#98C379", "#61AFEF"];

        public static SceneReportCard Build(SceneAnalysisResult result, IReadOnlyList<TensionArc.Point> arc,
            IEnumerable<NlpNote> visibleNotes)
        {
            var style = result.Style;
            var stats = $"{style.TotalWords:#,0} words · {style.DialogueRatio:P0} dialogue · {style.AverageSentenceLength:0.#}-word sentences";

            // Beats: consecutive paragraphs of the same kind merge into one segment
            var beats = new List<StripSegment>();
            foreach (var beat in result.ParagraphBeats)
            {
                if (beats.Count > 0 && beats[^1].Label == beat)
                    beats[^1] = beats[^1] with { Weight = beats[^1].Weight + 1 };
                else
                    beats.Add(new StripSegment(beat, BeatColors.GetValueOrDefault(beat, "#7A7A8A"), 1));
            }
            var beatSummary = string.Join(", ", result.ParagraphBeats
                .GroupBy(b => b).OrderByDescending(g => g.Count())
                .Select(g => $"{g.Count()} {g.Key.ToLowerInvariant()}"));

            var counts = result.Senses.Counts;
            int total = counts.Sum();
            var senses = total < 5 ? [] : counts
                .Select((n, i) => new StripSegment(SenseNames[i], SenseColors[i], n))
                .Where(s => s.Weight > 0)
                .ToList();
            var senseSummary = senses.Count == 0 ? string.Empty : DescribeSenses(counts, total) +
                (result.Senses.Concreteness > 0 ? $" · concreteness {result.Senses.Concreteness:0.0}/5" : string.Empty);

            string tension = string.Empty;
            var point = arc.FirstOrDefault(p => p.Scene.SceneId == result.SceneId);
            if (point is not null && arc.Count >= 3)
            {
                int rank = arc.Count(p => p.Tension > point.Tension);   // 0 = most tense
                var where = rank < Math.Max(1, arc.Count / 5) ? "among the book’s most tense scenes"
                    : rank >= arc.Count - Math.Max(1, arc.Count / 5) ? "among the book’s quietest scenes"
                    : "mid-range for this book";
                tension = $"Tension {point.Tension:0}/100, {where}";
            }

            var byCategory = visibleNotes.Where(n => n.SceneId == result.SceneId)
                .GroupBy(n => n.Category).OrderBy(g => g.Key)
                .Select(g => $"{g.Count()} {CategoryWord(g.Key)}").ToList();
            var noteSummary = byCategory.Count == 0 ? "No notes" : string.Join(" · ", byCategory);

            return new SceneReportCard
            {
                SceneId = result.SceneId, Stats = stats, Beats = beats, BeatSummary = beatSummary,
                Senses = senses, SenseSummary = senseSummary, TensionSummary = tension, NoteSummary = noteSummary
            };
        }

        private static string CategoryWord(NlpNoteCategory c) => c switch
        {
            NlpNoteCategory.CopyEditor => "copy",
            NlpNoteCategory.LineEditor => "line",
            _ => "developmental"
        };

        /// <summary>"Mostly sight; some sound and touch" from per-sense counts.</summary>
        internal static string DescribeSenses(int[] counts, int total)
        {
            var ranked = counts.Select((n, i) => (Name: SenseNames[i].ToLowerInvariant(), Share: (double)n / total))
                .Where(s => s.Share > 0).OrderByDescending(s => s.Share).ToList();
            var main = ranked[0];
            var others = ranked.Skip(1).Where(s => s.Share >= 0.1).Select(s => s.Name).ToList();
            var head = main.Share >= 0.6 ? $"Mostly {main.Name}" : $"Led by {main.Name}";
            return others.Count == 0 ? head : $"{head}; some {string.Join(" and ", others)}";
        }
    }

    // ── Exportable summary ───────────────────────────────────────────────────

    /// <summary>
    /// A self-contained HTML editorial letter for the whole book: where it stands, what
    /// changed since earlier runs, the biggest issues by rule and by chapter, and the
    /// genre guidelines. Readable in any browser and printable to PDF.
    /// </summary>
    public static class EditorialSummaryWriter
    {
        private static string H(string s) => WebUtility.HtmlEncode(s);

        public static string BuildHtml(Book book, IReadOnlyList<SceneAnalysisResult> results,
            IReadOnlyList<NlpNote> visibleNotes, DateTime generated)
        {
            var genre = book.StyleSheet?.Genre ?? EditorialStyleSheet.DefaultGenre;
            var arc = TensionArc.Compute(results);
            var sb = new StringBuilder();
            sb.Append("<!DOCTYPE html><html><head><meta charset=\"utf-8\"><title>")
              .Append(H(book.Title)).Append(" — editorial summary</title><style>")
              .Append(Css).Append("</style></head><body><main>");

            sb.Append("<h1>").Append(H(book.Title)).Append("</h1>");
            sb.Append("<p class=\"meta\">Editorial summary · ").Append(H(genre)).Append(" · ")
              .Append(generated.ToString("d MMMM yyyy", CultureInfo.InvariantCulture)).Append("</p>");

            // At a glance
            int words = results.Sum(r => r.Style.TotalWords);
            int chapters = results.Select(r => r.ChapterTitle).Distinct().Count();
            sb.Append("<section class=\"glance\">");
            Stat(sb, words.ToString("#,0", CultureInfo.InvariantCulture), "words analyzed");
            Stat(sb, chapters.ToString(CultureInfo.InvariantCulture), "chapters");
            Stat(sb, results.Count(r => r.Style.TotalWords > 0).ToString(CultureInfo.InvariantCulture), "scenes");
            Stat(sb, visibleNotes.Count.ToString(CultureInfo.InvariantCulture), "open notes");
            sb.Append("</section>");

            // Progress over time
            var history = book.AnalysisHistory;
            if (history.Count >= 2)
            {
                sb.Append("<h2>Progress</h2><table><tr><th>Analysis</th><th>Words</th><th>Open notes</th></tr>");
                foreach (var snap in history.TakeLast(8))
                    sb.Append("<tr><td>").Append(snap.Date.ToString("d MMM yyyy, HH:mm", CultureInfo.InvariantCulture))
                      .Append("</td><td>").Append(snap.Words.ToString("#,0", CultureInfo.InvariantCulture))
                      .Append("</td><td>").Append(snap.Total).Append("</td></tr>");
                sb.Append("</table>");
            }

            // Biggest issues by rule
            var byRule = visibleNotes.GroupBy(n => n.RuleId).OrderByDescending(g => g.Count()).Take(12).ToList();
            if (byRule.Count > 0)
            {
                var previous = history.Count >= 2 ? history[^2] : null;
                sb.Append("<h2>What to work on</h2><table><tr><th>Note</th><th>Editor</th><th>Count</th></tr>");
                foreach (var g in byRule)
                {
                    var rule = RuleCatalog.Find(g.Key);
                    string trend = previous is not null && previous.ByRule.TryGetValue(g.Key, out var before) && before != g.Count()
                        ? $" <span class=\"muted\">(was {before})</span>" : string.Empty;
                    sb.Append("<tr><td><strong>").Append(H(rule?.Title ?? g.Key)).Append("</strong><br><span class=\"muted\">")
                      .Append(H(rule?.Description ?? string.Empty)).Append("</span></td><td>")
                      .Append(H(g.First().CategoryLabel)).Append("</td><td>").Append(g.Count()).Append(trend).Append("</td></tr>");
                }
                sb.Append("</table>");
            }

            // Big-picture notes (developmental) in full
            var developmental = visibleNotes.Where(n => n.Category == NlpNoteCategory.DevelopmentalEditor).Take(20).ToList();
            if (developmental.Count > 0)
            {
                sb.Append("<h2>Structure and story</h2><ul class=\"notes\">");
                foreach (var n in developmental)
                {
                    sb.Append("<li>").Append(H(n.Message));
                    var where = string.Join(" › ", new[] { n.ChapterTitle, n.SceneTitle }.Where(s => !string.IsNullOrEmpty(s)));
                    if (where.Length > 0) sb.Append(" <span class=\"muted\">— ").Append(H(where)).Append("</span>");
                    sb.Append("</li>");
                }
                sb.Append("</ul>");
            }

            // Tension arc as a small inline chart
            if (arc.Count >= 3)
            {
                sb.Append("<h2>Tension across the book</h2>").Append(ArcSvg(arc))
                  .Append("<p class=\"muted\">Relative to this book: danger and conflict words, short sentences, action and heated dialogue")
                  .Append(arc.Any(p => p.Mood is not null) ? ", plus tense emotions from the emotion model." : ".")
                  .Append("</p>");
            }

            // Genre guidelines
            var rows = GenreBenchmarks.Compare(genre, book, results);
            if (rows.Count > 0)
            {
                sb.Append("<h2>Against ").Append(H(genre)).Append(" guidelines</h2><table><tr><th>Measure</th><th>This book</th><th>Typical</th><th></th></tr>");
                foreach (var row in rows)
                {
                    var verdict = row.Verdict switch
                    {
                        BenchmarkVerdict.Below => "<span class=\"warn\">below</span>",
                        BenchmarkVerdict.Above => "<span class=\"warn\">above</span>",
                        _ => "<span class=\"ok\">within</span>"
                    };
                    sb.Append("<tr><td>").Append(H(row.Benchmark.Label)).Append("<br><span class=\"muted\">")
                      .Append(H(row.Benchmark.Advice)).Append("</span></td><td>").Append(H(row.ValueText))
                      .Append("</td><td>").Append(H(row.RangeText)).Append("</td><td>").Append(verdict).Append("</td></tr>");
                }
                sb.Append("</table><p class=\"muted\">Rules of thumb, not measurements of published books. A book outside a range isn’t wrong.</p>");
            }

            // Chapter by chapter
            var chapterRows = results.Where(r => r.Style.TotalWords > 0).GroupBy(r => r.ChapterTitle).ToList();
            if (chapterRows.Count > 0)
            {
                sb.Append("<h2>Chapter by chapter</h2><table><tr><th>Chapter</th><th>Words</th><th>Dialogue</th><th>Tension</th><th>Notes</th></tr>");
                foreach (var g in chapterRows)
                {
                    int w = g.Sum(r => r.Style.TotalWords);
                    int s = g.Sum(r => r.Style.TotalSentences);
                    double dialogue = s == 0 ? 0 : g.Sum(r => r.Style.DialogueRatio * r.Style.TotalSentences) / s;
                    var points = arc.Where(p => p.Scene.ChapterTitle == g.Key).ToList();
                    var ids = g.Select(r => r.SceneId).ToHashSet();
                    sb.Append("<tr><td>").Append(H(g.Key)).Append("</td><td>").Append(w.ToString("#,0", CultureInfo.InvariantCulture))
                      .Append("</td><td>").Append($"{dialogue:P0}")
                      .Append("</td><td>").Append(points.Count == 0 ? "–" : $"{points.Average(p => p.Tension):0}")
                      .Append("</td><td>").Append(visibleNotes.Count(n => ids.Contains(n.SceneId))).Append("</td></tr>");
                }
                sb.Append("</table>");
            }

            sb.Append("<footer>Generated by alphaWriter on this computer. Sense ratings: Lancaster Sensorimotor Norms (Lynott et al., 2020), CC BY 4.0.</footer>");
            sb.Append("</main></body></html>");
            return sb.ToString();
        }

        private static void Stat(StringBuilder sb, string value, string label) =>
            sb.Append("<div><b>").Append(H(value)).Append("</b><span>").Append(H(label)).Append("</span></div>");

        private static string ArcSvg(IReadOnlyList<TensionArc.Point> arc)
        {
            const double width = 640, height = 140;
            double step = width / Math.Max(1, arc.Count - 1);
            var line = string.Join(" ", arc.Select((p, i) =>
                $"{(i * step).ToString("0.#", CultureInfo.InvariantCulture)},{(height - p.Smoothed / 100 * height).ToString("0.#", CultureInfo.InvariantCulture)}"));
            return $"<svg viewBox=\"0 0 {width} {height}\" class=\"arc\" role=\"img\" aria-label=\"Tension across the book\">" +
                   $"<polyline points=\"0,{height} {line} {width},{height}\" fill=\"#f3d5d8\" stroke=\"none\"/>" +
                   $"<polyline points=\"{line}\" fill=\"none\" stroke=\"#c0485a\" stroke-width=\"2\"/></svg>";
        }

        private const string Css =
            "body{margin:0;background:#faf8f4;color:#2b2a28;font:15px/1.55 Georgia,'Times New Roman',serif}" +
            "main{max-width:760px;margin:0 auto;padding:40px 24px 60px}" +
            "h1{font-size:30px;margin:0 0 4px}h2{font-size:19px;margin:34px 0 10px;border-bottom:1px solid #ddd6c8;padding-bottom:4px}" +
            ".meta,.muted{color:#7a756c}.muted{font-size:13px}" +
            ".glance{display:flex;flex-wrap:wrap;gap:12px;margin:20px 0}" +
            ".glance div{background:#fff;border:1px solid #e6e0d4;border-radius:6px;padding:10px 14px;min-width:120px}" +
            ".glance b{display:block;font-size:22px}.glance span{font-size:12px;color:#7a756c}" +
            "table{width:100%;border-collapse:collapse;font-size:14px}th,td{text-align:left;padding:6px 8px;border-bottom:1px solid #ebe5da;vertical-align:top}" +
            "th{font-size:12px;color:#7a756c;font-weight:normal;text-transform:uppercase;letter-spacing:.04em}" +
            ".notes li{margin-bottom:8px}.ok{color:#3c7a4a}.warn{color:#b5642a}" +
            ".arc{width:100%;height:auto;background:#fff;border:1px solid #e6e0d4;border-radius:6px}" +
            "footer{margin-top:40px;font-size:12px;color:#9a948a}" +
            "@media print{body{background:#fff}main{padding:0}}";
    }
}
