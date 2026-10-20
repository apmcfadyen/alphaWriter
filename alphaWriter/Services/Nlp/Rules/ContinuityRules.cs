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
    /// <summary>Book-level helpers: the book's scenes as documents, in reading order.</summary>
    internal static class BookText
    {
        /// <summary>A scene's sentences rebuilt as a document (one sentence per paragraph).</summary>
        public sealed record SceneDoc(SceneAnalysisResult Result, SceneDocument Doc, int ChapterIndex);

        public static List<SceneDoc> Scenes(BookRuleContext ctx)
        {
            var chapterOrder = ctx.Results.Select(r => r.ChapterTitle).Distinct().ToList();
            return ctx.Results
                .Select(r => new SceneDoc(r,
                    SceneDocumentBuilder.FromSentences(r.Sentences.Select(s => s.Text).ToList()),
                    chapterOrder.IndexOf(r.ChapterTitle)))
                .ToList();
        }

        public static List<string> Chapters(BookRuleContext ctx) =>
            ctx.Results.Select(r => r.ChapterTitle).Distinct().ToList();

        /// <summary>Points a note at a token of a rebuilt scene document, in the scene's own offsets.</summary>
        public static NlpNote AtToken(this NlpNote note, SceneDoc scene, TokenInfo first, TokenInfo? last = null)
        {
            last ??= first;
            var sentence = scene.Doc.SentenceAt(first.Start)!;
            int offset = first.Start - sentence.Start;
            note.ForScene(scene.Result).AtSentence(scene.Result, sentence.Index);
            note.Start += offset;
            note.Length = last.End - first.Start;
            note.AnchorOffset = offset;
            note.Evidence = scene.Doc.Text.Substring(first.Start, last.End - first.Start);
            return note;
        }
    }

    // ── Characters over the whole book ───────────────────────────────────────

    /// <summary>A recurring character who vanishes for a long stretch of chapters.</summary>
    public sealed class CharacterAbsenceRule : IBookRule
    {
        public RuleDescriptor Descriptor => RuleCatalog.CharacterAbsence;

        public IEnumerable<NlpNote> Evaluate(BookRuleContext ctx)
        {
            var chapters = BookText.Chapters(ctx);
            if (ctx.Book is null || chapters.Count < 6) yield break;
            var names = SceneRuleContext.BuildCharacterWords(ctx.Book);

            var present = new Dictionary<Character, SortedSet<int>>();
            foreach (var scene in BookText.Scenes(ctx))
                foreach (var t in scene.Doc.Tokens)
                    if (t.IsWord && char.IsUpper(t.Text[0]) &&
                        names.TryGetValue(LineRuleHelpers.WithoutPossessive(t.Text), out var c))
                    {
                        if (!present.TryGetValue(c, out var set)) present[c] = set = [];
                        set.Add(scene.ChapterIndex);
                    }

            int minGap = Math.Max(3, chapters.Count / 4);
            foreach (var (character, set) in present)
            {
                if (set.Count < 3) continue; // not a recurring character
                var list = set.ToList();
                for (int i = 1; i < list.Count; i++)
                {
                    int gap = list[i] - list[i - 1] - 1;
                    if (gap < minGap) continue;
                    yield return Note(Descriptor, NlpNoteSeverity.Info,
                        $"{character.Name} disappears after “{chapters[list[i - 1]]}” and doesn’t return until " +
                        $"“{chapters[list[i]]}” ({gap} chapters). Long enough for readers to lose track. " +
                        "A mention, a letter or another character thinking of them keeps the thread alive.");
                }
            }
        }
    }

    /// <summary>Contradictory physical details: Maya's eyes are blue in one chapter, green in another.</summary>
    public sealed class AttributeContinuityRule : IBookRule
    {
        public RuleDescriptor Descriptor => RuleCatalog.AttributeContinuity;

        private static readonly Dictionary<string, string> EyeColours = new(StringComparer.OrdinalIgnoreCase)
        {
            ["blue"] = "blue", ["green"] = "green", ["brown"] = "brown", ["grey"] = "grey", ["gray"] = "grey",
            ["hazel"] = "hazel", ["amber"] = "amber", ["black"] = "black", ["violet"] = "violet"
        };

        private static readonly Dictionary<string, string> HairColours = new(StringComparer.OrdinalIgnoreCase)
        {
            ["blond"] = "blond", ["blonde"] = "blond", ["black"] = "black", ["brown"] = "brown", ["red"] = "red",
            ["auburn"] = "auburn", ["grey"] = "grey", ["gray"] = "grey", ["white"] = "white", ["silver"] = "silver",
            ["golden"] = "golden", ["copper"] = "copper", ["chestnut"] = "chestnut", ["ginger"] = "ginger"
        };

        private static readonly Dictionary<string, Dictionary<string, string>> Features = new(StringComparer.OrdinalIgnoreCase)
        {
            ["eyes"] = EyeColours, ["hair"] = HairColours, ["beard"] = HairColours
        };

        private sealed record Claim(Character Who, string Feature, string Value, BookText.SceneDoc Scene, TokenInfo Colour);

        public IEnumerable<NlpNote> Evaluate(BookRuleContext ctx)
        {
            if (ctx.Book is null) yield break;
            var names = SceneRuleContext.BuildCharacterWords(ctx.Book);
            var claims = new List<Claim>();

            foreach (var scene in BookText.Scenes(ctx))
            {
                Character? lastNamed = null;
                foreach (var s in scene.Doc.Sentences)
                {
                    var words = s.Tokens.Where(t => t.IsWord).ToList();
                    for (int i = 0; i < words.Count; i++)
                    {
                        var t = words[i];
                        names.TryGetValue(LineRuleHelpers.WithoutPossessive(t.Text), out var named);
                        if (named is not null && char.IsUpper(t.Text[0])) lastNamed = named;
                        if (i + 2 >= words.Count) continue;

                        // "Maya's green eyes" or "her green eyes" (her = nearest matching name)
                        Character? owner = null;
                        if (named is not null && t.Text != LineRuleHelpers.WithoutPossessive(t.Text)) owner = named;
                        else if (PronounSets.Of(t.Text) is var set and not PronounSet.Unknown && t.Lower is "his" or "her"
                                 && lastNamed is not null && PronounInference.For(lastNamed, ctx.Book) == set)
                            owner = lastNamed;
                        if (owner is null) continue;

                        var colour = words[i + 1];
                        var feature = words[i + 2];
                        if (!Features.TryGetValue(feature.Text, out var colours)) continue;
                        if (!colours.TryGetValue(colour.Text, out var value)) continue;
                        claims.Add(new Claim(owner, feature.Lower, value, scene, colour));
                    }
                }
            }

            foreach (var group in claims.GroupBy(c => (c.Who, c.Feature)))
            {
                var byValue = group.GroupBy(c => c.Value).OrderByDescending(g => g.Count()).ToList();
                if (byValue.Count < 2) continue;
                var usual = byValue[0];
                var firstUsual = usual.First();
                foreach (var odd in byValue.Skip(1).SelectMany(g => g))
                {
                    var replacement = odd.Colour.Text.Length > 0 && char.IsUpper(odd.Colour.Text[0])
                        ? LineRuleHelpers.Capitalize(usual.Key) : usual.Key;
                    yield return Note(Descriptor, NlpNoteSeverity.Warning,
                            $"{odd.Who.Name}’s {odd.Feature} are {odd.Value} here, but {usual.Key} elsewhere " +
                            $"({usual.Count()}×, first in “{firstUsual.Scene.Result.SceneTitle}”). " +
                            "Readers notice details like this.")
                        .AtToken(odd.Scene, odd.Colour)
                        .WithSuggestion(replacement);
                }
            }
        }
    }

    // ── Setup and payoff ─────────────────────────────────────────────────────

    /// <summary>
    /// Story Bible items that are set up and then forgotten, or that turn up for the first
    /// time near the end without having been planted.
    /// </summary>
    public sealed class SetupPayoffRule : IBookRule
    {
        public RuleDescriptor Descriptor => RuleCatalog.SetupPayoff;

        public IEnumerable<NlpNote> Evaluate(BookRuleContext ctx)
        {
            var chapters = BookText.Chapters(ctx);
            if (ctx.Book is null || ctx.Book.Items.Count == 0 || chapters.Count < 4) yield break;
            var scenes = BookText.Scenes(ctx);

            foreach (var item in ctx.Book.Items)
            {
                var phrases = new[] { item.Name }
                    .Concat((item.Aka ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries))
                    .Select(p => p.Trim())
                    .Where(p => p.Length > 2 && !p.Equals("the", StringComparison.OrdinalIgnoreCase))
                    .Select(p => new Regex($@"\b{Regex.Escape(p)}\b", RegexOptions.IgnoreCase))
                    .ToList();
                if (phrases.Count == 0) continue;

                var mentions = new List<(BookText.SceneDoc Scene, int Sentence, Match Match)>();
                foreach (var scene in scenes)
                    for (int i = 0; i < scene.Result.Sentences.Count; i++)
                        foreach (var rx in phrases)
                            foreach (Match m in rx.Matches(scene.Result.Sentences[i].Text))
                                mentions.Add((scene, i, m));
                if (mentions.Count == 0) continue;

                var chapterSet = mentions.Select(m => m.Scene.ChapterIndex).Distinct().OrderBy(c => c).ToList();
                var first = mentions[0];

                string? message = null;
                if (chapterSet.Count == 1 && chapterSet[0] < chapters.Count * 0.7)
                    message = $"The {item.Name} is introduced in “{chapters[chapterSet[0]]}” and never mentioned again. " +
                              "If it matters, pay it off later; if it doesn’t, it may be drawing readers’ attention for nothing.";
                else if (chapterSet[0] >= chapters.Count * 0.85 && chapters.Count >= 6)
                    message = $"The {item.Name} first appears in “{chapters[chapterSet[0]]}”, near the end. " +
                              "If it’s important to the climax, plant it earlier so its arrival feels earned.";
                if (message is null) continue;

                var note = Note(Descriptor, NlpNoteSeverity.Info, message)
                    .ForScene(first.Scene.Result)
                    .AtSentence(first.Scene.Result, first.Sentence);
                note.Start += first.Match.Index;
                note.Length = first.Match.Length;
                note.AnchorOffset = first.Match.Index;
                note.Evidence = first.Match.Value;
                yield return note;
            }
        }
    }

    /// <summary>
    /// Distinctive phrases reused in different chapters ("light spilled across the wet
    /// stones"). Readers often remember the first use, so the second reads as an echo.
    /// </summary>
    public sealed class RepeatedPhraseRule : IBookRule
    {
        public RuleDescriptor Descriptor => RuleCatalog.RepeatedPhrases;

        private const int N = 6;
        private const int MaxNotes = 25;
        private static readonly Regex WordRx = new(@"[\p{L}]+(?:['’][\p{L}]+)?", RegexOptions.Compiled);

        public IEnumerable<NlpNote> Evaluate(BookRuleContext ctx)
        {
            var chapterOrder = BookText.Chapters(ctx);
            var seen = new Dictionary<string, (SceneAnalysisResult Result, int Chapter)>();
            int emitted = 0;

            foreach (var result in ctx.Results)
            {
                int chapter = chapterOrder.IndexOf(result.ChapterTitle);
                for (int si = 0; si < result.Sentences.Count; si++)
                {
                    var text = result.Sentences[si].Text;
                    if (text.Contains('"') || text.Contains('“')) continue; // dialogue repeats are voice
                    var words = WordRx.Matches(text).Cast<Match>().ToList();
                    int coveredUntil = -1;

                    for (int i = 0; i + N <= words.Count; i++)
                    {
                        var gram = words.Skip(i).Take(N).ToList();
                        if (gram.Count(w => w.Value.Length >= 4 && !Lexicons.Stopwords.Contains(w.Value)) < 3) continue;
                        var key = string.Join(" ", gram.Select(w => w.Value.ToLowerInvariant().Replace('’', '\'')));

                        if (!seen.TryGetValue(key, out var earlier))
                        {
                            seen[key] = (result, chapter);
                            continue;
                        }
                        if (earlier.Chapter == chapter || gram[0].Index <= coveredUntil) continue;

                        int start = gram[0].Index, end = gram[^1].Index + gram[^1].Length;
                        // Extend over a longer shared passage so it's one note, not several
                        while (i + N < words.Count)
                        {
                            var nextKey = string.Join(" ", words.Skip(i + 1).Take(N).Select(w => w.Value.ToLowerInvariant().Replace('’', '\'')));
                            if (!seen.ContainsKey(nextKey)) break;
                            i++;
                            end = words[i + N - 1].Index + words[i + N - 1].Length;
                        }
                        coveredUntil = end;

                        var phrase = text[start..end];
                        var note = Note(Descriptor, NlpNoteSeverity.Info,
                                $"“{phrase}” also appears in “{earlier.Result.SceneTitle}” ({chapterOrder[earlier.Chapter]}). " +
                                "A reused description can read as an echo; rephrase one of them, unless the callback is deliberate.")
                            .ForScene(result)
                            .AtSentence(result, si);
                        note.Start += start;
                        note.Length = end - start;
                        note.AnchorOffset = start;
                        note.Evidence = phrase;
                        yield return note;
                        if (++emitted >= MaxNotes) yield break;
                    }
                }
            }
        }
    }

    // ── Chapters ─────────────────────────────────────────────────────────────

    /// <summary>A chapter that opens with no sense of how much time has passed.</summary>
    public sealed class ChapterTimeCueRule : IBookRule
    {
        public RuleDescriptor Descriptor => RuleCatalog.ChapterTimeCues;

        public IEnumerable<NlpNote> Evaluate(BookRuleContext ctx)
        {
            var chapters = BookText.Chapters(ctx);
            if (chapters.Count < 3) yield break;

            foreach (var title in chapters.Skip(1))
            {
                var opening = ctx.Results.FirstOrDefault(r => r.ChapterTitle == title && r.Sentences.Count > 0);
                if (opening is null) continue;
                var words = 0;
                var text = string.Join(" ", opening.Sentences.TakeWhile(s => (words += s.WordCount) - s.WordCount < 200).Select(s => s.Text));
                if (Grounding.HasTimeCue(text)) continue;

                yield return Note(Descriptor, NlpNoteSeverity.Info,
                        $"“{title}” opens without a time cue, so readers can’t tell how much time has passed " +
                        "since the last chapter. “The next morning” or “Three days later” settles it in a phrase.")
                    .ForScene(opening)
                    .AtSentence(opening, 0);
            }
        }
    }

    /// <summary>
    /// How strongly each chapter ends. Quiet, resolved endings ("She finally slept.") give
    /// readers a natural place to stop; a few are fine, a run of them in the middle isn't.
    /// </summary>
    public sealed class ChapterEndingRule : IBookRule
    {
        public RuleDescriptor Descriptor => RuleCatalog.ChapterEndings;

        private static readonly HashSet<string> Pull = new(StringComparer.OrdinalIgnoreCase)
        {
            "suddenly", "until", "gone", "dead", "blood", "missing", "scream", "screamed", "knock", "knocked",
            "footsteps", "shot", "gunshot", "wrong", "lied", "secret", "vanished", "disappeared", "alone",
            "someone", "nobody", "never", "except", "but"
        };

        private static readonly HashSet<string> Settle = new(StringComparer.OrdinalIgnoreCase)
        {
            "slept", "sleep", "asleep", "safe", "home", "peace", "finally", "smiled", "relief", "rest",
            "content", "together", "goodnight", "comfortable", "warm"
        };

        public IEnumerable<NlpNote> Evaluate(BookRuleContext ctx)
        {
            var chapters = BookText.Chapters(ctx);
            if (chapters.Count < 3) yield break;

            int softRun = 0;
            for (int c = 0; c < chapters.Count - 1; c++)   // the final chapter is meant to settle
            {
                var last = ctx.Results.LastOrDefault(r => r.ChapterTitle == chapters[c] && r.Sentences.Count > 0);
                if (last is null) continue;

                var tail = last.Sentences.TakeLast(2).ToList();
                var final = tail[^1];
                var tailWords = tail.SelectMany(s => s.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                    .Select(w => w.Trim('.', ',', '!', '?', ';', ':', '"', '“', '”', '’')).ToList();

                int score = 0;
                if (final.Text.TrimEnd('"', '”', '\'', ' ').EndsWith('?')) score += 2;
                if (final.WordCount <= 8) score += 1;
                if (final.Text.StartsWith('"') || final.Text.StartsWith('“')) score += 1;
                bool pull = tailWords.Any(Pull.Contains);
                if (pull) score += 2;
                if (!pull && tailWords.Any(Settle.Contains)) score -= 2;

                if (score > 0) { softRun = 0; continue; }
                softRun++;
                bool middle = c >= chapters.Count * 0.2 && c < chapters.Count * 0.85;

                yield return Note(Descriptor, softRun >= 2 && middle ? NlpNoteSeverity.Warning : NlpNoteSeverity.Info,
                        $"“{chapters[c]}” ends on a settled, quiet beat. " +
                        (softRun >= 2
                            ? $"That’s {softRun} chapters in a row, each a natural place for readers to put the book down. "
                            : "That’s fine now and then. ") +
                        "An unanswered question, a turn, or a short final line gives readers a reason to keep going.")
                    .ForScene(last)
                    .AtSentence(last, last.Sentences.Count - 1);
            }
        }
    }
}
