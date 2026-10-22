using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using alphaWriter.Models;
using alphaWriter.Models.Analysis;
using alphaWriter.Services.Nlp.Parsing;
using static alphaWriter.Services.Nlp.Rules.RuleHelpers;

namespace alphaWriter.Services.Nlp.Rules
{
    // ── Beat map ─────────────────────────────────────────────────────────────

    /// <summary>What a paragraph is mostly doing.</summary>
    public enum Beat { Dialogue, Action, Interiority, Description, Exposition }

    /// <summary>
    /// Classifies paragraphs into beats from word signals: quoted speech, physical action
    /// verbs, thinking verbs, backstory markers (past perfect, "years ago") and description.
    /// A heuristic: it's meant to show the shape of a scene, not to grade a paragraph.
    /// </summary>
    public static class BeatClassifier
    {
        private static HashSet<string> Stems(params string[] words) =>
            words.Select(WordStemmer.Stem).ToHashSet(StringComparer.Ordinal);

        private static readonly HashSet<string> ActionStems = Stems(
            "ran", "grabbed", "pulled", "pushed", "turned", "stepped", "walked", "threw", "caught",
            "opened", "slammed", "climbed", "jumped", "reached", "kicked", "struck", "hit", "dragged",
            "lifted", "dropped", "crossed", "raced", "fell", "rose", "stood", "shoved", "swung",
            "ducked", "dove", "spun", "lunged", "fired", "shot", "crawled", "rushed", "burst",
            "tore", "broke", "seized", "snatched", "hurled", "stumbled", "sprinted", "charged",
            "knelt", "leaned", "pressed", "shut", "locked", "fled", "chased", "carried", "landed");

        private static readonly HashSet<string> ThinkingStems = Stems(
            "thought", "knew", "wondered", "remembered", "realized", "realised", "wanted", "hoped",
            "feared", "believed", "wished", "understood", "imagined", "decided", "supposed",
            "doubted", "regretted", "worried", "felt", "guessed", "suspected");

        private static readonly HashSet<string> InteriorMarkers = new(StringComparer.OrdinalIgnoreCase)
            { "perhaps", "maybe", "surely", "why", "if" };

        private static readonly HashSet<string> DescriptionWords = new(StringComparer.OrdinalIgnoreCase)
        {
            "light", "shadow", "shadows", "dark", "darkness", "smell", "scent", "sound", "air", "wind",
            "sky", "rain", "sun", "moon", "stone", "stones", "wall", "walls", "floor", "window", "windows",
            "red", "blue", "green", "grey", "gray", "gold", "golden", "silver", "black", "white", "brown",
            "pale", "bright", "dim", "cold", "warm", "damp", "dust", "fog", "mist", "water", "trees",
            "glass", "wood", "iron", "smoke", "silence", "quiet"
        };

        private static readonly Regex BackstoryPhrases = new(
            @"\b(years ago|months ago|long ago|used to|back then|as a (child|boy|girl)|when (she|he|they|i) (was|were)|in those days|had always|had never|years before|once, long)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>One beat per paragraph, in order.</summary>
        public static List<Beat> Classify(SceneDocument doc) =>
            doc.Paragraphs.Select(p => Classify(doc, p)).ToList();

        public static Beat Classify(SceneDocument doc, ParagraphInfo paragraph)
        {
            if (paragraph.Kind == ParagraphKind.Dialogue) return Beat.Dialogue;

            var sentences = doc.Sentences.Skip(paragraph.FirstSentenceIndex).Take(paragraph.SentenceCount).ToList();
            var words = sentences.SelectMany(s => s.Tokens).Where(t => t.IsWord).ToList();
            if (words.Count == 0) return Beat.Description;
            var narration = words.Where(t => !t.InQuote).ToList();
            if (narration.Count < words.Count / 2) return Beat.Dialogue;

            double per100 = 100.0 / Math.Max(narration.Count, 1);
            double action = narration.Count(t => ActionStems.Contains(t.Stem)) * per100;
            double thinking = (narration.Count(t => ThinkingStems.Contains(t.Stem))
                               + narration.Count(t => InteriorMarkers.Contains(t.Text))) * per100;
            double description = narration.Count(t => DescriptionWords.Contains(t.Text)) * per100;

            // Past perfect ("had gone", "had been", "had waited") is the classic backstory signal
            int pastPerfect = 0;
            for (int i = 0; i + 1 < narration.Count; i++)
                if (narration[i].Lower == "had" && IsParticiple(narration[i + 1].Lower)) pastPerfect++;
            var text = doc.Text.Substring(paragraph.Start, paragraph.Length);
            double backstory = (pastPerfect * 2 + BackstoryPhrases.Matches(text).Count * 3) * per100;

            // Past perfect inside a character's thoughts ("perhaps he had guessed") is recent
            // thinking, not backstory, so exposition must also outweigh the thinking signal.
            if (backstory >= 3 && backstory >= action && backstory > thinking) return Beat.Exposition;
            if (action >= thinking && action >= description && action >= 2) return Beat.Action;
            if (thinking >= description && thinking >= 2) return Beat.Interiority;
            return Beat.Description;
        }

        private static bool IsParticiple(string w) =>
            (w.Length > 3 && w.EndsWith("ed")) || Lexicons.IrregularParticiples.Contains(w) || w is "been";
    }

    /// <summary>Runs of backstory or explanation that stop the scene's present action.</summary>
    public sealed class InfoDumpRule : ISceneRule
    {
        public RuleDescriptor Descriptor => RuleCatalog.InfoDump;

        public IEnumerable<NlpNote> Evaluate(SceneRuleContext ctx)
        {
            var doc = ctx.Document;
            var beats = BeatClassifier.Classify(doc);
            int minParagraphs = (int)ctx.Threshold(Descriptor.Id, "minParagraphs", 3);

            int start = -1;
            for (int p = 0; p <= beats.Count; p++)
            {
                if (p < beats.Count && beats[p] == Beat.Exposition)
                {
                    if (start < 0) start = p;
                    continue;
                }
                if (start >= 0)
                {
                    int count = p - start;
                    int words = doc.Paragraphs.Skip(start).Take(count)
                        .Sum(par => doc.Sentences.Skip(par.FirstSentenceIndex).Take(par.SentenceCount).Sum(s => s.WordCount));
                    if (count >= minParagraphs || (count >= 2 && words >= 250))
                    {
                        var first = doc.Sentences[doc.Paragraphs[start].FirstSentenceIndex];
                        var note = Note(Descriptor, NlpNoteSeverity.Warning,
                            $"{count} paragraphs of backstory in a row ({words} words). The scene’s present moment stops " +
                            "while the past is explained. Keep what this scene needs, and let the rest surface later, " +
                            "a detail at a time, when it matters.");
                        if (first.Words.FirstOrDefault() is { } w) note.At(w);
                        note.SentenceIndex = first.Index;
                        yield return note;
                    }
                    start = -1;
                }
            }
        }
    }

    // ── Grounding ────────────────────────────────────────────────────────────

    internal static class Grounding
    {
        public static readonly HashSet<string> PlaceNouns = new(StringComparer.OrdinalIgnoreCase)
        {
            "room", "kitchen", "hall", "hallway", "corridor", "street", "road", "lane", "alley", "square",
            "market", "harbour", "harbor", "quay", "dock", "docks", "beach", "shore", "sea", "river",
            "bridge", "forest", "woods", "field", "garden", "yard", "house", "cottage", "castle", "tower",
            "church", "chapel", "inn", "tavern", "pub", "bar", "cafe", "shop", "office", "station",
            "platform", "train", "car", "ship", "boat", "deck", "cabin", "camp", "tent", "cave",
            "mountain", "hill", "valley", "village", "town", "city", "palace", "library", "school",
            "hospital", "prison", "cell", "cellar", "attic", "stairs", "staircase", "roof", "balcony",
            "bedroom", "parlour", "parlor", "study", "courtyard", "gate", "porch", "landing", "barn"
        };

        private static readonly Regex TimeCueRx = new(
            @"\b(morning|afternoon|evening|night|midnight|noon|dawn|dusk|sunrise|sunset|twilight|today|tonight|" +
            @"yesterday|tomorrow|hours?|minutes?|o'clock|o’clock|weeks?|weekend|months?|years?|season|spring|summer|" +
            @"autumn|winter|monday|tuesday|wednesday|thursday|friday|saturday|sunday|january|february|march|" +
            @"april|june|july|august|september|october|november|december|christmas|later|earlier)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static bool HasTimeCue(string text) => TimeCueRx.IsMatch(text);

        /// <summary>Location names and aliases from the Story Bible, as lowercase words.</summary>
        public static HashSet<string> LocationWords(Book? book)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (book is null) return set;
            foreach (var l in book.Locations)
                foreach (var n in new[] { l.Name, l.Aka })
                    foreach (var w in (n ?? string.Empty).Split([' ', ',', ';'], StringSplitOptions.RemoveEmptyEntries))
                        if (w.Length > 2 && !Lexicons.Stopwords.Contains(w)) set.Add(w.Trim('"', '\''));
            return set;
        }
    }

    /// <summary>
    /// "White room" openings: a scene's first ~150 words don't say where we are, and often
    /// not when or with whom either, so readers start the scene without footing.
    /// </summary>
    public sealed class SceneGroundingRule : ISceneRule
    {
        public RuleDescriptor Descriptor => RuleCatalog.SceneGrounding;

        public IEnumerable<NlpNote> Evaluate(SceneRuleContext ctx)
        {
            var doc = ctx.Document;
            if (doc.WordCount < 300) yield break;

            int window = (int)ctx.Threshold(Descriptor.Id, "openingWords", 150);
            var opening = doc.Tokens.Where(t => t.IsWord).Take(window).ToList();
            if (opening.Count == 0) yield break;

            var places = Grounding.LocationWords(ctx.Book);
            bool where = opening.Any(t => !t.InQuote && (Grounding.PlaceNouns.Contains(t.Text) || places.Contains(t.Text)));
            bool who = opening.Any(t => LineRuleHelpers.CharacterFor(t, ctx) is not null)
                       || opening.Any(t => !t.InQuote && Lexicons.SubjectPronouns.Contains(t.Text));
            var openingText = doc.Text.Substring(0, Math.Min(doc.Text.Length, opening[^1].End));
            bool when = Grounding.HasTimeCue(openingText);

            if (where) yield break;

            var missing = new List<string> { "where we are" };
            if (!when) missing.Add("when this is happening");
            if (!who) missing.Add("whose eyes we’re in");
            var note = Note(Descriptor, NlpNoteSeverity.Info,
                $"The opening {opening.Count} words don’t establish {string.Join(" or ", missing)}. " +
                "A line of setting early on (a room, a street, the weather, the hour) gives readers their footing.");
            note.At(opening[0]);
            yield return note;
        }
    }

    // ── Character presence ───────────────────────────────────────────────────

    /// <summary>
    /// The scene's cast tags against its text. Characters are auto-tagged when named, so this
    /// looks for tagged characters who are only talked about in dialogue ("we should ask Tomas")
    /// or never named at all, and offers a one-click untag that auto-detection respects.
    /// </summary>
    public sealed class CharacterPresenceRule : ISceneRule
    {
        public RuleDescriptor Descriptor => RuleCatalog.CharacterPresence;

        public IEnumerable<NlpNote> Evaluate(SceneRuleContext ctx)
        {
            if (ctx.Book is null || ctx.Book.Characters.Count == 0) yield break;
            var doc = ctx.Document;

            // Characters are auto-tagged when their name appears, so the useful question is
            // the reverse one: is everyone tagged actually on stage?
            var narrationMentions = new HashSet<Character>();
            var dialogueMentions = new Dictionary<Character, TokenInfo>();
            foreach (var t in doc.Tokens)
            {
                if (LineRuleHelpers.CharacterFor(t, ctx) is not { } c) continue;
                if (t.InQuote) dialogueMentions.TryAdd(c, t);
                else narrationMentions.Add(c);
            }

            foreach (var id in ctx.TaggedCharacterIds.Except(ctx.ViewpointCharacterIds).Distinct())
            {
                var character = ctx.Book.Characters.FirstOrDefault(c => c.Id == id);
                if (character is null || narrationMentions.Contains(character)) continue;

                NlpNote note;
                if (dialogueMentions.TryGetValue(character, out var spoken))
                {
                    note = Note(Descriptor, NlpNoteSeverity.Info,
                            $"{character.Name} is tagged in this scene but only talked about in dialogue. " +
                            "If they aren’t actually present, untag them so the story map and cast lists stay accurate.")
                        .At(spoken);
                }
                else
                {
                    if (doc.WordCount < 150) continue;
                    note = Note(Descriptor, NlpNoteSeverity.Info,
                        $"{character.Name} is tagged in this scene but never named in it. " +
                        "If they’re present, a mention helps readers track them; if not, untag them.");
                    note.Evidence = character.Name;
                }
                note.FixAction = $"untag-character:{character.Id}";
                note.FixActionLabel = $"Untag {character.Name}";
                yield return note;
            }
        }
    }
}
