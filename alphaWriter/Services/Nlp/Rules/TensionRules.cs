using System;
using System.Collections.Generic;
using System.Linq;
using alphaWriter.Models.Analysis;
using alphaWriter.Services.Nlp.Parsing;
using static alphaWriter.Services.Nlp.Rules.RuleHelpers;

namespace alphaWriter.Services.Nlp.Rules
{
    /// <summary>Text signals of tension in one scene.</summary>
    public static class TensionAnalyzer
    {
        private static readonly HashSet<string> DangerStems = new[]
        {
            "blood", "bleed", "scream", "shout", "yell", "gun", "knife", "blade", "sword", "threat",
            "threaten", "danger", "dangerous", "fear", "afraid", "terror", "panic", "run", "chase",
            "flee", "escape", "trap", "trapped", "fight", "attack", "strike", "kill", "dead", "death",
            "die", "dying", "murder", "corpse", "hurt", "pain", "wound", "injure", "break",
            "shatter", "crash", "burn", "fire", "smoke", "storm", "alarm", "siren", "warn", "warning",
            "enemy", "hunt", "hide", "hidden", "secret", "lie", "liar", "betray", "steal", "stolen",
            "lost", "missing", "vanish", "fail", "desperate", "urgent", "hurry", "quick",
            "fast", "trembl", "shake", "shaking", "pound", "racing", "gasp", "choke", "drown",
            "fall", "slip", "grab", "seize", "force", "shove", "punch", "slam", "struggle", "cry",
            "sob", "plead", "beg", "accuse", "angry", "rage", "furious", "dread", "worse", "wrong",
            "deadline", "explode", "explosion", "shot", "gunshot", "bullet", "poison",
            "ransom", "hostage", "police", "arrest", "guard", "weapon", "risk", "caught", "doom"
        }.Select(WordStemmer.Stem).ToHashSet(StringComparer.Ordinal);

        public static TensionSignals Signals(SceneDocument doc, IReadOnlyList<Beat> beats)
        {
            var words = doc.Tokens.Where(t => t.IsWord).ToList();
            if (words.Count == 0) return new TensionSignals();

            var narration = doc.Sentences.Where(s => !s.IsDialogue && s.WordCount > 0).ToList();
            var dialogue = doc.Sentences.Where(s => s.IsDialogue).ToList();
            int heated = dialogue.Sum(s => s.Text.Count(c => c is '!' or '?'));

            return new TensionSignals
            {
                Words = words.Count,
                DangerRate = 100.0 * words.Count(t => DangerStems.Contains(t.Stem)) / words.Count,
                ShortSentenceShare = narration.Count == 0 ? 0 : (double)narration.Count(s => s.WordCount <= 8) / narration.Count,
                ActionShare = beats.Count == 0 ? 0 : (double)beats.Count(b => b == Beat.Action) / beats.Count,
                HeatedDialogue = dialogue.Count == 0 ? 0 : Math.Min(1.0, (double)heated / dialogue.Count)
            };
        }
    }

    /// <summary>
    /// The book's tension and mood across scenes. Tension is relative: each scene is scored
    /// against the book's own range, so a quiet literary novel and a thriller both get a shape.
    /// </summary>
    public static class TensionArc
    {
        /// <summary>Scenes shorter than this (outlines, stubs) are left off the arc.</summary>
        public const int MinWords = 100;

        private static readonly HashSet<EmotionLabel> Tense =
        [
            EmotionLabel.Fear, EmotionLabel.Nervousness, EmotionLabel.Anger, EmotionLabel.Annoyance,
            EmotionLabel.Disgust, EmotionLabel.Surprise
        ];

        private static readonly HashSet<EmotionLabel> Positive =
        [
            EmotionLabel.Joy, EmotionLabel.Love, EmotionLabel.Amusement, EmotionLabel.Optimism,
            EmotionLabel.Relief, EmotionLabel.Gratitude, EmotionLabel.Admiration, EmotionLabel.Excitement,
            EmotionLabel.Approval, EmotionLabel.Caring, EmotionLabel.Pride
        ];

        private static readonly HashSet<EmotionLabel> Negative =
        [
            EmotionLabel.Sadness, EmotionLabel.Grief, EmotionLabel.Anger, EmotionLabel.Fear,
            EmotionLabel.Disgust, EmotionLabel.Disappointment, EmotionLabel.Remorse, EmotionLabel.Annoyance,
            EmotionLabel.Nervousness, EmotionLabel.Embarrassment, EmotionLabel.Disapproval
        ];

        /// <summary>One point on the arc.</summary>
        /// <param name="Tension">0–100 within this book.</param>
        /// <param name="Smoothed">Three-scene moving average of <paramref name="Tension"/>.</param>
        /// <param name="Mood">−1 (dark) to +1 (bright) from the emotion model; null without it.</param>
        public sealed record Point(SceneAnalysisResult Scene, double Tension, double Smoothed, double? Mood);

        public static bool HasEmotions(SceneAnalysisResult r) => r.Sentences.Any(s => s.Emotions.Count > 0);

        private static double EmotionShare(SceneAnalysisResult r, HashSet<EmotionLabel> labels)
        {
            var scored = r.Sentences.Where(s => s.Emotions.Count > 0).ToList();
            return scored.Count == 0
                ? 0
                : scored.Average(s => s.Emotions.Where(e => labels.Contains(e.Label)).Sum(e => (double)e.Confidence));
        }

        public static List<Point> Compute(IReadOnlyList<SceneAnalysisResult> results)
        {
            var scenes = results.Where(r => r.Tension.Words >= MinWords).ToList();
            if (scenes.Count == 0) return [];

            bool emotions = scenes.Any(HasEmotions);
            var components = new List<(double Weight, Func<SceneAnalysisResult, double> Value)>
            {
                (0.35, r => r.Tension.DangerRate),
                (0.20, r => r.Tension.ShortSentenceShare),
                (0.15, r => r.Tension.ActionShare),
                (0.10, r => r.Tension.HeatedDialogue)
            };
            if (emotions) components.Add((0.30, r => EmotionShare(r, Tense)));

            var raw = new double[scenes.Count];
            foreach (var (weight, value) in components)
            {
                var values = scenes.Select(value).ToList();
                var mean = values.Average();
                var sd = StdDev(values, mean);
                if (sd < 1e-9) continue;
                for (int i = 0; i < raw.Length; i++)
                    raw[i] += weight * (values[i] - mean) / sd;
            }

            double min = raw.Min(), max = raw.Max();
            var tension = raw.Select(v => max - min < 1e-9 ? 50 : 100 * (v - min) / (max - min)).ToArray();
            return scenes.Select((r, i) =>
            {
                var window = tension.Skip(Math.Max(0, i - 1)).Take(i == 0 ? 2 : 3).ToList();
                double? mood = HasEmotions(r) ? Math.Round(EmotionShare(r, Positive) - EmotionShare(r, Negative), 3) : null;
                return new Point(r, Math.Round(tension[i], 1), Math.Round(window.Average(), 1), mood);
            }).ToList();
        }
    }

    /// <summary>
    /// A long run of the book's quietest scenes in the middle: the classic sagging middle.
    /// </summary>
    public sealed class TensionSagRule : IBookRule
    {
        public RuleDescriptor Descriptor => RuleCatalog.TensionSag;

        public IEnumerable<NlpNote> Evaluate(BookRuleContext ctx)
        {
            var arc = TensionArc.Compute(ctx.Results);
            if (arc.Count < 8) yield break;

            var cutoff = arc.Select(p => p.Tension).OrderBy(t => t).ElementAt(arc.Count / 3);
            int i = 0;
            while (i < arc.Count)
            {
                if (arc[i].Tension > cutoff) { i++; continue; }
                int start = i;
                while (i < arc.Count && arc[i].Tension <= cutoff) i++;
                int length = i - start;
                // A quiet opening is the book warming up, not a sag; a quiet run at the very end is the climax rule's business
                if (length < 4 || start < arc.Count * 0.2 || i - 1 > arc.Count * 0.9) continue;

                var first = arc[start].Scene;
                var last = arc[i - 1].Scene;
                yield return Note(Descriptor, length >= 6 ? NlpNoteSeverity.Warning : NlpNoteSeverity.Info,
                        $"{length} scenes in a row, from “{first.SceneTitle}” to “{last.SceneTitle}”, are among the " +
                        "book’s quietest: little danger, conflict or urgency. A complication, a deadline or a " +
                        "question raised here can carry readers through the middle.")
                    .ForScene(first);
            }
        }
    }

    /// <summary>
    /// Where the book's most intense stretch falls. Readers expect the peak late: if it comes
    /// early, or the ending is quieter than the middle, the climax may not land.
    /// </summary>
    public sealed class ClimaxPlacementRule : IBookRule
    {
        public RuleDescriptor Descriptor => RuleCatalog.ClimaxPlacement;

        public IEnumerable<NlpNote> Evaluate(BookRuleContext ctx)
        {
            var arc = TensionArc.Compute(ctx.Results);
            if (arc.Count < 8) yield break;

            int peak = 0;
            for (int i = 1; i < arc.Count; i++)
                if (arc[i].Smoothed > arc[peak].Smoothed) peak = i;
            double position = (double)peak / (arc.Count - 1);

            var lastQuarter = arc.Skip(arc.Count * 3 / 4).ToList();
            var median = arc.Select(p => p.Smoothed).OrderBy(t => t).ElementAt(arc.Count / 2);

            if (position < 0.5)
            {
                yield return Note(Descriptor, NlpNoteSeverity.Info,
                        $"The book’s most intense stretch is around “{arc[peak].Scene.SceneTitle}”, " +
                        $"{position:P0} of the way through. Readers usually expect the peak in the final third; " +
                        "if this is the climax, what keeps the rest of the book rising?")
                    .ForScene(arc[peak].Scene);
            }
            else if (lastQuarter.Max(p => p.Smoothed) < median)
            {
                var quietest = lastQuarter.OrderBy(p => p.Smoothed).First();
                yield return Note(Descriptor, NlpNoteSeverity.Info,
                        "The last quarter of the book is quieter than its middle. If the climax is here, " +
                        "it may need more at stake on the page: danger, conflict, a ticking clock.")
                    .ForScene(quietest.Scene);
            }
        }
    }
}
