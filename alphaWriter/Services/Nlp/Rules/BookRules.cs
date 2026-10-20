using System;
using System.Collections.Generic;
using System.Linq;
using alphaWriter.Models;
using alphaWriter.Models.Analysis;
using static alphaWriter.Services.Nlp.Rules.RuleHelpers;

namespace alphaWriter.Services.Nlp.Rules
{
    internal static class BookRuleHelpers
    {
        public static NlpNote ForScene(this NlpNote note, SceneAnalysisResult r)
        {
            note.SceneId = r.SceneId;
            note.SceneTitle = r.SceneTitle;
            note.ChapterTitle = r.ChapterTitle;
            return note;
        }

        /// <summary>Points a book-level note at one of a scene's sentences.</summary>
        public static NlpNote AtSentence(this NlpNote note, SceneAnalysisResult r, int index)
        {
            var s = r.Sentences[index];
            note.SentenceIndex = index;
            note.Start = s.Start;
            note.Length = s.Text.Length;
            note.AnchorText = s.Text;
            note.AnchorOffset = 0;
            note.AnchorOccurrence = r.Sentences.Take(index).Count(x => x.Text == s.Text);
            return note;
        }
    }

    public sealed class SceneCountRule : IBookRule
    {
        public RuleDescriptor Descriptor => RuleCatalog.SceneCount;

        public IEnumerable<NlpNote> Evaluate(BookRuleContext ctx)
        {
            var book = ctx.Book;
            if (book is null) yield break;

            var counts = book.Chapters
                .Select(c => (double)c.Scenes.Count(s => s.Status != SceneStatus.Outline))
                .Where(c => c > 0)
                .ToList();
            if (counts.Count <= 2) yield break;

            double avg = counts.Average();
            double stdDev = StdDev(counts, avg);
            if (stdDev <= 0) yield break;

            foreach (var chapter in book.Chapters)
            {
                int count = chapter.Scenes.Count(s => s.Status != SceneStatus.Outline);
                if (count == 0) continue;
                if (Math.Abs(count - avg) / stdDev <= 2.0) continue;

                string direction = count > avg ? "many more" : "far fewer";
                var note = Note(Descriptor, NlpNoteSeverity.Info,
                    $"'{chapter.Title}' has {count} scenes against a book average of {avg:F0} — " +
                    $"{direction} than most chapters. " +
                    "A disparity this large can affect how readers experience the chapter's pacing " +
                    "relative to the rest of the book.");
                note.ChapterTitle = chapter.Title;
                yield return note;
            }
        }
    }

    public sealed class ChapterLengthRule : IBookRule
    {
        public RuleDescriptor Descriptor => RuleCatalog.ChapterLength;

        public IEnumerable<NlpNote> Evaluate(BookRuleContext ctx)
        {
            var wordCounts = new Dictionary<string, int>();
            foreach (var r in ctx.Results)
                wordCounts[r.ChapterTitle] = wordCounts.GetValueOrDefault(r.ChapterTitle) + r.Style.TotalWords;
            if (wordCounts.Count <= 2) yield break;

            var counts = wordCounts.Values.Select(v => (double)v).ToList();
            double avg = counts.Average();
            double stdDev = StdDev(counts, avg);
            if (stdDev <= 0) yield break;

            foreach (var (title, wc) in wordCounts)
            {
                if (Math.Abs(wc - avg) / stdDev <= 2.0) continue;
                string direction = wc > avg ? "significantly longer" : "significantly shorter";
                var note = Note(Descriptor, NlpNoteSeverity.Warning,
                    $"'{title}' is {wc:N0} words — {direction} than the book average of {avg:N0}. " +
                    "A chapter this far outside the norm disrupts the implicit pacing contract with readers. " +
                    "Consider trimming, or splitting it into two shorter chapters.");
                note.ChapterTitle = title;
                yield return note;
            }
        }
    }

    public sealed class DialogueBalanceRule : IBookRule
    {
        public RuleDescriptor Descriptor => RuleCatalog.DialogueBalance;

        public IEnumerable<NlpNote> Evaluate(BookRuleContext ctx)
        {
            var scenes = ctx.Results.Where(r => r.Style.TotalSentences > 5).ToList();
            if (scenes.Count <= 2) yield break;

            var ratios = scenes.Select(r => r.Style.DialogueRatio).ToList();
            double avg = ratios.Average();
            double stdDev = StdDev(ratios, avg);
            if (stdDev <= 0) yield break;

            foreach (var r in scenes)
            {
                double z = (r.Style.DialogueRatio - avg) / stdDev;
                if (Math.Abs(z) <= 2.0) continue;
                string direction = z > 0 ? "unusually dialogue-heavy" : "unusually narration-heavy";
                yield return Note(Descriptor, NlpNoteSeverity.Info,
                        $"'{r.SceneTitle}' is {direction} — {r.Style.DialogueRatio:P0} dialogue " +
                        $"vs the book average of {avg:P0}. " +
                        "This imbalance can feel jarring if it isn't marking a structural pivot.")
                    .ForScene(r);
            }
        }
    }

    public sealed class ContractionShiftRule : IBookRule
    {
        public RuleDescriptor Descriptor => RuleCatalog.ContractionShift;

        public IEnumerable<NlpNote> Evaluate(BookRuleContext ctx)
        {
            var scenes = ctx.Results.Where(r => r.Style.TotalSentences > 5).ToList();
            if (scenes.Count <= 2) yield break;

            var rates = scenes.Select(r => r.Style.ContractionRate).ToList();
            double avg = rates.Average();
            double stdDev = StdDev(rates, avg);
            if (stdDev <= 0) yield break;

            foreach (var r in scenes)
            {
                double z = (r.Style.ContractionRate - avg) / stdDev;
                if (Math.Abs(z) <= 2.0) continue;
                string direction = z > 0 ? "more casual" : "more formal";
                yield return Note(Descriptor, NlpNoteSeverity.Info,
                        $"'{r.SceneTitle}' sounds {direction} — contraction rate {r.Style.ContractionRate:P0} " +
                        $"vs the book average of {avg:P0}. " +
                        "The register shift may be deliberate (a formal ceremony, a tense confrontation) — " +
                        "if not, it can quietly erode the book's tonal consistency.")
                    .ForScene(r);
            }
        }
    }

    public sealed class SceneOpeningRule : IBookRule
    {
        public RuleDescriptor Descriptor => RuleCatalog.SceneOpening;

        public IEnumerable<NlpNote> Evaluate(BookRuleContext ctx)
        {
            foreach (var r in ctx.Results)
            {
                if (r.Sentences.Count < 5) continue;
                var first = r.Sentences[0];
                if (first.WordCount > 35 && r.Style.AverageSentenceLength < 20)
                {
                    yield return Note(Descriptor, NlpNoteSeverity.Info,
                            $"'{r.SceneTitle}' opens with a {first.WordCount}-word sentence against a scene average of {r.Style.AverageSentenceLength:F0} — " +
                            "readers are still orienting themselves and may stumble on a complex opener. " +
                            "A shorter hook pulls them in faster.")
                        .ForScene(r)
                        .AtSentence(r, 0);
                }
            }
        }
    }

    public sealed class SceneClosingRule : IBookRule
    {
        public RuleDescriptor Descriptor => RuleCatalog.SceneClosing;

        public IEnumerable<NlpNote> Evaluate(BookRuleContext ctx)
        {
            foreach (var r in ctx.Results)
            {
                if (r.Sentences.Count < 5) continue;
                var last = r.Sentences[^1];
                if (last.WordCount > 35 && r.Style.AverageSentenceLength < 20)
                {
                    yield return Note(Descriptor, NlpNoteSeverity.Info,
                            $"'{r.SceneTitle}' closes with a {last.WordCount}-word sentence — the final beat carries extra weight, " +
                            "and a long ending can dissipate it. A tighter closing sentence tends to land harder.")
                        .ForScene(r)
                        .AtSentence(r, r.Sentences.Count - 1);
                }
            }
        }
    }

    public sealed class FormalityDriftRule : IBookRule
    {
        public RuleDescriptor Descriptor => RuleCatalog.FormalityDrift;

        public IEnumerable<NlpNote> Evaluate(BookRuleContext ctx)
        {
            var chapters = ctx.Results
                .GroupBy(r => r.ChapterTitle)
                .Select(g => g.Average(r => r.Style.ContractionRate))
                .ToList();
            if (chapters.Count < 4) yield break;

            int mid = chapters.Count / 2;
            double first = chapters.Take(mid).Average();
            double second = chapters.Skip(mid).Average();
            double shift = second - first;
            if (Math.Abs(shift) <= 0.06) yield break;

            string direction = shift < 0 ? "more formal" : "more casual";
            yield return Note(Descriptor, NlpNoteSeverity.Warning,
                $"The book's prose grows {direction} across the second half — " +
                $"contraction rate shifts from {first:P0} in early chapters " +
                $"to {second:P0} later. " +
                "A gradual register shift can signal natural character development; " +
                "an abrupt one may indicate unintentional voice drift.");
        }
    }

    public sealed class VocabularyPlateauRule : IBookRule
    {
        public RuleDescriptor Descriptor => RuleCatalog.VocabularyPlateau;

        public IEnumerable<NlpNote> Evaluate(BookRuleContext ctx)
        {
            var results = ctx.Results;
            if (results.Count < 6) yield break;

            int mid = results.Count / 2;
            static IEnumerable<string> Words(IEnumerable<SceneAnalysisResult> rs) => rs
                .SelectMany(r => r.Sentences.Select(s => s.Text))
                .SelectMany(s => s.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries))
                .Select(w => w.ToLowerInvariant().Trim('.', ',', '!', '?'))
                .Where(w => w.Length > 3);

            var firstHalf = Words(results.Take(mid)).ToHashSet();
            var secondHalf = Words(results.Skip(mid)).Distinct().ToList();
            if (secondHalf.Count == 0) yield break;

            double noveltyRate = (double)secondHalf.Count(w => !firstHalf.Contains(w)) / secondHalf.Count;
            if (noveltyRate < 0.10)
            {
                yield return Note(Descriptor, NlpNoteSeverity.Info,
                    $"The second half of the book introduces very little new vocabulary ({noveltyRate:P0} of words are new) — " +
                    "the prose may start to feel repetitive as readers move through the back half. " +
                    "Varying sentence structures and word choices can maintain freshness.");
            }
        }
    }

    public sealed class ActPacingRule : IBookRule
    {
        public RuleDescriptor Descriptor => RuleCatalog.ActPacing;

        public IEnumerable<NlpNote> Evaluate(BookRuleContext ctx)
        {
            var chapters = ctx.Results.GroupBy(r => r.ChapterTitle).ToList();
            if (chapters.Count < 6) yield break;

            int actSize = chapters.Count / 3;
            double act1 = chapters.Take(actSize).SelectMany(g => g).Average(r => r.Style.TotalWords);
            double act2 = chapters.Skip(actSize).Take(actSize).SelectMany(g => g).Average(r => r.Style.TotalWords);

            if (act2 > act1 * 1.5 && act1 > 0)
            {
                yield return Note(Descriptor, NlpNoteSeverity.Warning,
                    $"Your middle act scenes average {act2:N0} words vs {act1:N0} in Act 1 — " +
                    "the pacing slows significantly in the second third of the book. " +
                    "Consider tightening middle act scenes or subdividing longer ones.");
            }
        }
    }

    public sealed class EmotionalFlatlineRule : IBookRule
    {
        public RuleDescriptor Descriptor => RuleCatalog.EmotionalFlatline;

        public IEnumerable<NlpNote> Evaluate(BookRuleContext ctx)
        {
            var byChapter = ctx.Results
                .Where(r => r.Sentences.Any(s => s.Emotions.Count > 0))
                .GroupBy(r => r.ChapterTitle);

            foreach (var chapter in byChapter)
            {
                var dominant = chapter
                    .Select(r => r.Sentences
                        .Where(s => s.Emotions.Count > 0)
                        .SelectMany(s => s.Emotions)
                        .GroupBy(e => e.Label)
                        .OrderByDescending(g => g.Sum(e => e.Confidence))
                        .FirstOrDefault()?.Key)
                    .Where(e => e.HasValue)
                    .Select(e => e!.Value)
                    .Distinct()
                    .ToList();

                if (dominant.Count > 0 && dominant.Count < 3)
                {
                    var note = Note(Descriptor, NlpNoteSeverity.Info,
                        $"'{chapter.Key}' runs almost entirely in {string.Join(", ", dominant)} — " +
                        "fewer than 3 distinct emotional tones across all scenes. " +
                        "Even a single moment of contrasting intensity would give readers an emotional beat to anchor to.");
                    note.ChapterTitle = chapter.Key;
                    yield return note;
                }
            }
        }
    }

    public sealed class DialogueDesertRule : IBookRule
    {
        public RuleDescriptor Descriptor => RuleCatalog.DialogueDesert;

        public IEnumerable<NlpNote> Evaluate(BookRuleContext ctx)
        {
            var results = ctx.Results;
            int start = -1, count = 0;

            for (int i = 0; i <= results.Count; i++)
            {
                bool desert = i < results.Count
                    && results[i].Style.DialogueRatio < 0.05
                    && results[i].Style.TotalSentences >= 5;
                if (desert)
                {
                    if (start < 0) start = i;
                    count++;
                    continue;
                }

                if (count >= 4)
                {
                    var first = results[start];
                    bool toEnd = i == results.Count;
                    string message = toEnd
                        ? $"The book ends with {count} consecutive scenes containing almost no dialogue — " +
                          "pure narration this sustained can feel remote. " +
                          "Even a brief exchange grounds readers before the final beat."
                        : $"'{first.SceneTitle}' through '{results[i - 1].SceneTitle}' — {count} consecutive scenes " +
                          "contain almost no dialogue. Pure narration at this length can feel airless; " +
                          "even a brief exchange grounds readers and resets the rhythm.";
                    yield return Note(Descriptor, NlpNoteSeverity.Warning, message).ForScene(first);
                }
                start = -1;
                count = 0;
            }
        }
    }
}
