using System;
using System.Collections.Generic;
using System.Linq;
using alphaWriter.Models;
using alphaWriter.Models.Analysis;
using alphaWriter.Services.Nlp.Parsing;
using static alphaWriter.Services.Nlp.Rules.RuleHelpers;

namespace alphaWriter.Services.Nlp.Rules
{
    // ── Pronouns ─────────────────────────────────────────────────────────────

    /// <summary>
    /// A he/she whose antecedent could be either of two characters named just before it:
    /// "Tomas handed Erik the key. He turned it over." Who turned it?
    /// </summary>
    public sealed class AmbiguousPronounRule : ISceneRule
    {
        public RuleDescriptor Descriptor => RuleCatalog.AmbiguousPronoun;

        public IEnumerable<NlpNote> Evaluate(SceneRuleContext ctx)
        {
            var doc = ctx.Document;
            foreach (var paragraph in doc.Paragraphs)
            {
                var sentences = doc.Sentences.Skip(paragraph.FirstSentenceIndex).Take(paragraph.SentenceCount).ToList();
                List<Character> previous = [];

                foreach (var s in sentences)
                {
                    var current = new List<Character>();
                    foreach (var t in s.Tokens)
                    {
                        if (!t.IsWord || t.InQuote) continue;

                        if (LineRuleHelpers.CharacterFor(t, ctx) is { } named)
                        {
                            if (!current.Contains(named)) current.Add(named);
                            continue;
                        }

                        var set = PronounSets.Of(t.Text);
                        if (set == PronounSet.Unknown) continue;

                        var candidates = previous.Concat(current).Distinct()
                            .Where(c => PronounInference.For(c, ctx.Book) == set)
                            .ToList();
                        if (candidates.Count < 2) continue;

                        var names = string.Join(" or ", candidates.Take(3).Select(c => c.Name));
                        yield return Note(Descriptor, NlpNoteSeverity.Info,
                                $"“{t.Text}” could mean {names}: both are named just before it. " +
                                "Readers usually assume the most recent subject, which may not be who you mean. " +
                                "Repeating the name, or reordering the sentence, removes the doubt.")
                            .At(t);
                        break; // one note per sentence is enough
                    }
                    previous = current.Count > 0 ? current : previous;
                }
            }
        }
    }

    // ── Tense ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Narration slipping out of the scene's tense ("She opened the door. The room is dark.").
    /// Looks at subject + verb pairs in narration only, so dialogue and general statements
    /// in quotes are never counted.
    /// </summary>
    public sealed class TenseDriftRule : ISceneRule
    {
        public RuleDescriptor Descriptor => RuleCatalog.TenseDrift;

        private static readonly HashSet<string> Modals = new(StringComparer.OrdinalIgnoreCase)
            { "would", "could", "will", "can", "might", "should", "must", "may", "shall" };

        private static readonly Dictionary<string, string> PresentToPast = new(StringComparer.OrdinalIgnoreCase)
        {
            ["is"] = "was", ["are"] = "were", ["am"] = "was", ["has"] = "had", ["have"] = "had",
            ["does"] = "did", ["do"] = "did", ["says"] = "said", ["goes"] = "went", ["sees"] = "saw",
            ["comes"] = "came", ["takes"] = "took", ["makes"] = "made", ["knows"] = "knew",
            ["thinks"] = "thought", ["feels"] = "felt", ["tells"] = "told", ["finds"] = "found",
            ["gives"] = "gave", ["gets"] = "got", ["leaves"] = "left", ["stands"] = "stood",
            ["sits"] = "sat", ["runs"] = "ran", ["holds"] = "held", ["keeps"] = "kept",
            ["hears"] = "heard", ["brings"] = "brought", ["begins"] = "began", ["means"] = "meant",
            ["falls"] = "fell", ["speaks"] = "spoke", ["writes"] = "wrote", ["reads"] = "read",
        };

        private static readonly HashSet<string> IrregularPast =
            new(PresentToPast.Values.Concat(["were", "had", "did", "went"]), StringComparer.OrdinalIgnoreCase);

        private static readonly HashSet<string> PresentOnly = new(StringComparer.OrdinalIgnoreCase)
            { "is", "are", "am", "has", "does" };

        private enum Tense { None, Past, Present }

        public IEnumerable<NlpNote> Evaluate(SceneRuleContext ctx)
        {
            var samples = new List<(TokenInfo Verb, TokenInfo Subject, Tense Tense)>();
            foreach (var s in ctx.Document.Sentences)
            {
                var words = s.Tokens.Where(t => t.IsWord).ToList();
                for (int i = 0; i + 1 < words.Count; i++)
                {
                    var subject = words[i];
                    if (subject.InQuote || !IsSubject(subject, ctx)) continue;

                    var verb = words[i + 1];
                    if (verb.Text.EndsWith("ly", StringComparison.OrdinalIgnoreCase) && i + 2 < words.Count)
                        verb = words[i + 2];   // "she slowly turns"
                    if (verb.InQuote || Modals.Contains(verb.Text)) continue;

                    var tense = Classify(verb, subject);
                    if (tense != Tense.None) samples.Add((verb, subject, tense));
                }
            }

            int past = samples.Count(x => x.Tense == Tense.Past);
            int present = samples.Count(x => x.Tense == Tense.Present);
            if (past + present < (int)ctx.Threshold(Descriptor.Id, "minSamples", 8)) yield break;

            var dominant = past >= present ? Tense.Past : Tense.Present;
            double share = (double)Math.Max(past, present) / (past + present);
            if (share < 0.8 || share >= 1) yield break;   // mixed on purpose, or perfectly consistent

            string sceneTense = dominant == Tense.Past ? "past" : "present";
            foreach (var (verb, subject, tense) in samples.Where(x => x.Tense != dominant))
            {
                var note = Note(Descriptor, NlpNoteSeverity.Warning,
                        $"This scene is told in the {sceneTense} tense, but “{subject.Text} {verb.Text}” slips into the " +
                        $"{(tense == Tense.Past ? "past" : "present")}. If it isn’t a deliberate shift, match the scene.")
                    .At(ctx.Document, subject, verb);
                if (dominant == Tense.Past && ToPast(verb.Text) is { } fix)
                    note.Suggestions.Add($"{subject.Text} {fix}");
                yield return note;
            }
        }

        private static bool IsSubject(TokenInfo t, SceneRuleContext ctx) =>
            t.Text is "he" or "she" or "He" or "She" or "it" or "It" || LineRuleHelpers.CharacterFor(t, ctx) is not null
            && !t.Text.Contains('\'') && !t.Text.Contains('’');

        private static Tense Classify(TokenInfo verb, TokenInfo subject)
        {
            var v = verb.Lower;
            if (IrregularPast.Contains(v) || (v.Length > 3 && v.EndsWith("ed"))) return Tense.Past;
            if (PresentOnly.Contains(v) || PresentToPast.ContainsKey(v)) return Tense.Present;
            // Regular third-person present: "she walks", "Tomas waits"
            if (v.Length > 3 && v.EndsWith('s') && !v.EndsWith("ss") && !v.EndsWith("us") && !v.EndsWith("is")
                && char.IsLetter(v[^2]) && !Lexicons.Stopwords.Contains(v))
                return Tense.Present;
            return Tense.None;
        }

        /// <summary>Simple present → past ("walks" → "walked", "closes" → "closed"); null when unsure.</summary>
        internal static string? ToPast(string verb)
        {
            if (PresentToPast.TryGetValue(verb, out var irregular)) return irregular;
            var v = verb.ToLowerInvariant();
            if (!v.EndsWith('s')) return null;
            string result;
            if (v.EndsWith("ies")) result = v[..^3] + "ied";
            else if (v.EndsWith("ches") || v.EndsWith("shes") || v.EndsWith("sses") || v.EndsWith("xes") || v.EndsWith("zes"))
                result = v[..^2] + "ed";
            else if (v[..^1].EndsWith('e')) result = v[..^1] + "d";
            else
            {
                var stem = v[..^1];
                // stop → stopped needs doubling; don't guess for short consonant-vowel-consonant stems
                if (stem.Length <= 4 && stem.Length >= 3 && !"aeiou".Contains(stem[^1]) && "aeiou".Contains(stem[^2]) && !"aeiou".Contains(stem[^3]))
                    return null;
                result = stem + "ed";
            }
            return char.IsUpper(verb[0]) ? LineRuleHelpers.Capitalize(result) : result;
        }
    }

    // ── Gesture tics ─────────────────────────────────────────────────────────

    internal static class GestureLexicon
    {
        public static readonly IReadOnlyList<(PhrasePattern Pattern, string Label, string Verb)> Gestures =
        [
            (new("~roll", "POSS", "eyes"), "eye-rolls", "roll their eyes"),
            (new("~raise", "DET", "eyebrow"), "raised eyebrows", "raise an eyebrow"),
            (new("~raise", "POSS", "eyebrows"), "raised eyebrows", "raise an eyebrow"),
            (new("~raise", "POSS", "eyebrow"), "raised eyebrows", "raise an eyebrow"),
            (new("~run", "DET", "hand", "through", "POSS", "hair"), "hands through hair", "run a hand through their hair"),
            (new("~run", "POSS", "hand", "through", "POSS", "hair"), "hands through hair", "run a hand through their hair"),
            (new("~bite", "POSS", "lip"), "lip-biting", "bite their lip"),
            (new("~clench", "POSS", "jaw"), "clenched jaws", "clench their jaw"),
            (new("~cross", "POSS", "arms"), "crossed arms", "cross their arms"),
            (new("~purse", "POSS", "lips"), "pursed lips", "purse their lips"),
            (new("~clear", "POSS", "throat"), "throat-clearing", "clear their throat"),
            (new("~tilt", "POSS", "head"), "head tilts", "tilt their head"),
            (new("~narrow", "POSS", "eyes"), "narrowed eyes", "narrow their eyes"),
            (new("~take", "DET", "deep", "breath"), "deep breaths", "take a deep breath"),
            (new("~nod"), "nods", "nod"),
            (new("~shrug"), "shrugs", "shrug"),
            (new("~sigh"), "sighs", "sigh"),
            (new("~smile"), "smiles", "smile"),
            (new("~grin"), "grins", "grin"),
            (new("~frown"), "frowns", "frown"),
            (new("~wince"), "winces", "wince"),
            (new("~smirk"), "smirks", "smirk"),
            (new("~chuckle"), "chuckles", "chuckle"),
            (new("~swallow", "hard"), "hard swallows", "swallow hard"),
        ];

        public static readonly IReadOnlyList<PhrasePattern> Patterns = Gestures.Select(g => g.Pattern).ToList();

        /// <summary>Every gesture in narration, as (label index, matched words).</summary>
        public static IEnumerable<(int Index, List<TokenInfo> Words, SentenceInfo Sentence)> Find(SceneDocument doc)
        {
            foreach (var s in doc.Sentences)
                foreach (var (p, words) in PhraseScanner.Find(s, Patterns, narrationOnly: true))
                    yield return (p, words, s);
        }
    }

    /// <summary>The same gesture three or more times in one scene ("she nodded… he nodded… they nodded").</summary>
    public sealed class GestureRepeatRule : ISceneRule
    {
        public RuleDescriptor Descriptor => RuleCatalog.GestureTics;

        public IEnumerable<NlpNote> Evaluate(SceneRuleContext ctx)
        {
            int min = (int)ctx.Threshold(Descriptor.Id, "perScene", 3);
            var byLabel = GestureLexicon.Find(ctx.Document)
                .GroupBy(g => GestureLexicon.Gestures[g.Index].Label)
                .Where(g => g.Count() >= min);

            foreach (var group in byLabel.OrderByDescending(g => g.Count()))
            {
                var hits = group.ToList();
                var note = Note(Descriptor, NlpNoteSeverity.Info,
                        $"{LineRuleHelpers.Capitalize(group.Key)} appear {hits.Count} times in this scene. " +
                        "Repeated beats blur together; vary them, or let some lines of dialogue stand without a gesture.")
                    .At(ctx.Document, hits[0].Words[0], hits[0].Words[^1]);
                note.Evidence = group.Key;
                foreach (var h in hits.Skip(1))
                    note.ExtraSpans.Add(new TextSpan(h.Words[0].Start, h.Words[^1].End - h.Words[0].Start));
                yield return note;
            }
        }
    }

    /// <summary>
    /// Book-wide gesture habits, attributed to characters: "Characters shrug 23 times
    /// (Tomas 14, Maya 6), 9 of them in 'The Docks'."
    /// </summary>
    public sealed class GestureTicsBookRule : IBookRule
    {
        public RuleDescriptor Descriptor => RuleCatalog.GestureTics;

        public IEnumerable<NlpNote> Evaluate(BookRuleContext ctx)
        {
            if (ctx.Results.Count == 0) yield break;
            var names = SceneRuleContext.BuildCharacterWords(ctx.Book);
            int totalWords = Math.Max(1, ctx.Results.Sum(r => r.Style.TotalWords));

            var hits = new List<(string Label, string Verb, Character? Who, string Chapter)>();
            foreach (var r in ctx.Results)
            {
                var doc = SceneDocumentBuilder.FromSentences(r.Sentences.Select(s => s.Text).ToList());
                foreach (var (index, words, sentence) in GestureLexicon.Find(doc))
                {
                    var g = GestureLexicon.Gestures[index];
                    hits.Add((g.Label, g.Verb, Attribute(words[0], sentence, doc, names, ctx.Book), r.ChapterTitle));
                }
            }

            int minTotal = 10;
            double minPer10k = 3.0;
            foreach (var group in hits.GroupBy(h => h.Label).OrderByDescending(g => g.Count()))
            {
                int count = group.Count();
                if (count < minTotal || count * 10000.0 / totalWords < minPer10k) continue;

                var who = group.Where(h => h.Who is not null)
                    .GroupBy(h => h.Who!.Name)
                    .OrderByDescending(g => g.Count())
                    .Take(3)
                    .Select(g => $"{g.Key} {g.Count()}")
                    .ToList();
                var busiest = group.GroupBy(h => h.Chapter).OrderByDescending(g => g.Count()).First();

                var breakdown = who.Count > 0 ? $" ({string.Join(", ", who)})" : "";
                var hotspot = busiest.Count() >= 3 && busiest.Count() > count / 3
                    ? $", {busiest.Count()} of them in “{busiest.Key}”"
                    : "";
                var note = Note(Descriptor, NlpNoteSeverity.Info,
                    $"Characters {group.First().Verb} {count} times across the book{breakdown}{hotspot}. " +
                    "A recurring beat starts to read as the writer’s habit rather than the character’s. " +
                    "Keep the ones that mean something, and swap the rest for a gesture only this character would make.");
                note.Evidence = group.Key;
                yield return note;
            }
        }

        /// <summary>The character doing the gesture: a name right before it, or the pronoun's nearest match.</summary>
        private static Character? Attribute(TokenInfo first, SentenceInfo sentence, SceneDocument doc,
            Dictionary<string, Character> names, Book? book)
        {
            var before = sentence.Tokens.TakeWhile(t => t != first).Where(t => t.IsWord && !t.InQuote).ToList();
            if (before.Count == 0) return null;
            var subject = before[^1];
            if (names.TryGetValue(LineRuleHelpers.WithoutPossessive(subject.Text), out var named)) return named;

            var set = PronounSets.Of(subject.Text);
            if (set == PronounSet.Unknown) return null;
            // Nearest earlier name in this or the previous sentence with matching pronouns
            var window = doc.Sentences.Where(s => s.Index == sentence.Index - 1).SelectMany(s => s.Tokens)
                .Concat(before).Where(t => t.IsWord).Reverse();
            foreach (var t in window)
                if (names.TryGetValue(LineRuleHelpers.WithoutPossessive(t.Text), out var c) && PronounInference.For(c, book) == set)
                    return c;
            return null;
        }
    }
}
