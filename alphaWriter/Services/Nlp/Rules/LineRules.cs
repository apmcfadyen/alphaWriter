using System;
using System.Collections.Generic;
using System.Linq;
using alphaWriter.Models;
using alphaWriter.Models.Analysis;
using alphaWriter.Services.Nlp.Parsing;
using static alphaWriter.Services.Nlp.Rules.RuleHelpers;

namespace alphaWriter.Services.Nlp.Rules
{
    internal static class LineRuleHelpers
    {
        /// <summary>"Maya’s" → "Maya".</summary>
        public static string WithoutPossessive(string word) =>
            word.EndsWith("'s", StringComparison.OrdinalIgnoreCase) || word.EndsWith("’s", StringComparison.OrdinalIgnoreCase)
                ? word[..^2]
                : word;

        /// <summary>The character a name token refers to, if it names exactly one.</summary>
        public static Character? CharacterFor(TokenInfo t, SceneRuleContext ctx) =>
            t.IsWord && char.IsUpper(t.Text[0]) && ctx.CharacterWords.TryGetValue(WithoutPossessive(t.Text), out var c) ? c : null;

        /// <summary>Next word token after index <paramref name="i"/> in the sentence, or null.</summary>
        public static (TokenInfo Token, int Index)? NextWord(List<TokenInfo> tokens, int i)
        {
            for (int k = i + 1; k < tokens.Count; k++)
                if (tokens[k].IsWord) return (tokens[k], k);
            return null;
        }

        public static bool IsQuote(TokenInfo t) => t.Text is "\"" or "“" or "”";

        /// <summary>
        /// All tokens of each paragraph. Dialogue tags can sit in the "next sentence" as far
        /// as the splitter is concerned ("Really?" She asked. splits at the capital), so
        /// tag rules read whole paragraphs.
        /// </summary>
        public static IEnumerable<List<TokenInfo>> ParagraphTokens(SceneDocument doc) =>
            doc.Sentences.GroupBy(s => s.ParagraphIndex).Select(g => g.SelectMany(s => s.Tokens).ToList());

        public static string Capitalize(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];
    }

    // ── Viewpoint ────────────────────────────────────────────────────────────

    /// <summary>
    /// Head-hopping: in a single-viewpoint scene, narration that reports what another
    /// character privately thinks or feels ("Tomas knew she was lying" in Maya's scene).
    /// </summary>
    public sealed class PovSlipRule : ISceneRule
    {
        public RuleDescriptor Descriptor => RuleCatalog.PovSlip;

        public IEnumerable<NlpNote> Evaluate(SceneRuleContext ctx)
        {
            var pov = ctx.SingleViewpoint;
            if (pov is null) yield break;

            foreach (var s in ctx.Document.Sentences)
            {
                var tokens = s.Tokens;
                for (int i = 0; i < tokens.Count; i++)
                {
                    var t = tokens[i];
                    if (t.InQuote) continue;
                    var who = LineRuleHelpers.CharacterFor(t, ctx);
                    if (who is null || who == pov || t.Text.Contains('\'') || t.Text.Contains('’')) continue;

                    // Interior verb within the next two words: "Tomas knew", "Tomas suddenly wondered"
                    int words = 0;
                    for (int k = i + 1; k < tokens.Count && words < 2; k++)
                    {
                        var v = tokens[k];
                        if (!v.IsWord) break;
                        words++;
                        if (!LineLexicons.InteriorVerbStems.Contains(v.Stem)) continue;

                        yield return Note(Descriptor, NlpNoteSeverity.Warning,
                                $"“{t.Text} {v.Lower}” — this scene is told from {pov.Name}’s point of view, " +
                                $"so {pov.Name} can’t know what {who.Name} privately {v.Lower}. " +
                                $"Show it through what {pov.Name} can see or hear: a look, a pause, something {who.Name} says or does.")
                            .At(ctx.Document, t, v);
                        break;
                    }
                }
            }
        }
    }

    /// <summary>
    /// Filter words: the viewpoint character perceiving instead of the reader experiencing
    /// ("she saw the door swing open" → "the door swung open").
    /// </summary>
    public sealed class FilterWordsRule : ISceneRule
    {
        public RuleDescriptor Descriptor => RuleCatalog.FilterWords;

        public IEnumerable<NlpNote> Evaluate(SceneRuleContext ctx)
        {
            var pov = ctx.SingleViewpoint;
            foreach (var s in ctx.Document.Sentences)
            {
                var tokens = s.Tokens;
                for (int i = 0; i + 1 < tokens.Count; i++)
                {
                    var subject = tokens[i];
                    if (!subject.IsWord || subject.InQuote) continue;

                    // Main clauses only: "She saw…", "…, and she saw…". In "she hated that he
                    // could see it" the perception is part of what is being described.
                    if (!StartsMainClause(tokens, i)) continue;

                    bool isSubject = Lexicons.SubjectPronouns.Contains(subject.Text)
                        || (pov is not null ? LineRuleHelpers.CharacterFor(subject, ctx) == pov
                                            : LineRuleHelpers.CharacterFor(subject, ctx) is not null);
                    if (!isSubject) continue;

                    // Optional "could": "she could hear"
                    var next = LineRuleHelpers.NextWord(tokens, i);
                    if (next is null) continue;
                    var (verb, vi) = next.Value;
                    if (verb.Lower == "could" && LineRuleHelpers.NextWord(tokens, vi) is { } after)
                        (verb, vi) = after;
                    if (verb.InQuote || !LineLexicons.FilterVerbStems.Contains(verb.Stem)) continue;

                    // "felt angry" is a named emotion, handled by its own rule
                    if (LineRuleHelpers.NextWord(tokens, vi) is { } obj && Lexicons.TellingEmotionWords.Contains(obj.Token.Text))
                        continue;

                    var phrase = ctx.Document.Text.Substring(subject.Start, verb.End - subject.Start);
                    yield return Note(Descriptor, NlpNoteSeverity.Info,
                            $"“{phrase}” filters the moment through the character. " +
                            "Cutting the perception verb usually puts the reader there directly: " +
                            "“she saw the door swing open” becomes “the door swung open.”")
                        .At(ctx.Document, subject, verb);
                }
            }
        }

        private static readonly HashSet<string> Coordinators =
            new(StringComparer.OrdinalIgnoreCase) { "and", "but", "then", "so", "yet" };

        /// <summary>True when token <paramref name="i"/> begins a sentence or a coordinated main clause.</summary>
        private static bool StartsMainClause(List<TokenInfo> tokens, int i)
        {
            for (int k = i - 1; k >= 0; k--)
            {
                var t = tokens[k];
                if (!t.IsWord) return t.Text is "," or ";" or ":" or "—";
                return Coordinators.Contains(t.Text);
            }
            return true; // first token of the sentence
        }
    }

    // ── Dialogue mechanics ───────────────────────────────────────────────────

    /// <summary>
    /// Tag punctuation and capitalization:
    /// "Hello." she said → "Hello," she said;  "Really?" She asked → "Really?" she asked.
    /// </summary>
    public sealed class DialogueTagPunctuationRule : ISceneRule
    {
        public RuleDescriptor Descriptor => RuleCatalog.DialogueTagPunctuation;

        public IEnumerable<NlpNote> Evaluate(SceneRuleContext ctx)
        {
            foreach (var tokens in LineRuleHelpers.ParagraphTokens(ctx.Document))
            {
                for (int i = 1; i < tokens.Count; i++)
                {
                    if (!LineRuleHelpers.IsQuote(tokens[i])) continue;
                    var before = tokens[i - 1];
                    if (before.IsWord) continue; // opening quote
                    if (LineRuleHelpers.NextWord(tokens, i) is not { } w1 || w1.Token.InQuote) continue;
                    if (LineRuleHelpers.NextWord(tokens, w1.Index) is not { } w2) continue;

                    var subject = w1.Token;
                    bool speechTag = LineLexicons.SpeechVerbStems.Contains(w2.Token.Stem);
                    if (!speechTag) continue;
                    bool pronoun = LineLexicons.TagPronouns.Contains(subject.Text);
                    bool named = LineRuleHelpers.CharacterFor(subject, ctx) is not null;

                    if (before.Text == "." && (pronoun && char.IsLower(subject.Text[0]) || named))
                    {
                        yield return Note(Descriptor, NlpNoteSeverity.Warning,
                                "A dialogue line followed by a tag ends with a comma, not a period: " +
                                $"“Hello,” {subject.Text} {w2.Token.Text}.")
                            .At(before)
                            .WithSuggestion(",");
                    }
                    else if (before.Text is "," or "?" or "!" && pronoun && subject.Text != "I" && char.IsUpper(subject.Text[0]))
                    {
                        yield return Note(Descriptor, NlpNoteSeverity.Warning,
                                "The tag continues the same sentence, so its pronoun stays lowercase: " +
                                $"“Really?” {subject.Lower} {w2.Token.Text}.")
                            .At(subject)
                            .WithSuggestion(subject.Lower);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Said-bookisms ("she retorted", "he opined") and actions used as tags
    /// ("Hello," she smiled — smiling can't produce words).
    /// </summary>
    public sealed class DialogueTagVerbRule : ISceneRule
    {
        public RuleDescriptor Descriptor => RuleCatalog.DialogueTagVerbs;

        public IEnumerable<NlpNote> Evaluate(SceneRuleContext ctx)
        {
            var doc = ctx.Document;
            foreach (var tokens in LineRuleHelpers.ParagraphTokens(doc))
            {
                for (int i = 1; i < tokens.Count; i++)
                {
                    if (!LineRuleHelpers.IsQuote(tokens[i]) || tokens[i - 1].IsWord) continue;
                    var punct = tokens[i - 1];
                    var closingQuote = tokens[i];
                    if (LineRuleHelpers.NextWord(tokens, i) is not { } w1 || w1.Token.InQuote) continue;
                    if (LineRuleHelpers.NextWord(tokens, w1.Index) is not { } w2) continue;

                    // "she retorted" or "retorted Tomas"
                    TokenInfo? subject = null, verb = null;
                    if (IsTagSubject(w1.Token, ctx)) { subject = w1.Token; verb = w2.Token; }
                    else if (IsTagSubject(w2.Token, ctx)) { subject = w2.Token; verb = w1.Token; }
                    if (subject is null || verb is null) continue;

                    if (LineLexicons.SaidBookisms.Contains(verb.Text))
                    {
                        bool question = punct.Text == "?";
                        bool present = verb.Lower.EndsWith('s') && !verb.Lower.EndsWith("ed");
                        string plain = (question, present) switch
                        {
                            (true, true) => "asks",
                            (true, false) => "asked",
                            (false, true) => "says",
                            _ => "said"
                        };
                        yield return Note(Descriptor, NlpNoteSeverity.Info,
                                $"“{verb.Lower}” draws attention to the tag. “{plain}” is nearly invisible to readers, " +
                                "which keeps their focus on the words being spoken.")
                            .At(verb)
                            .WithSuggestion(plain);
                    }
                    else if (punct.Text == "," && subject == w1.Token && LineLexicons.ActionNotSpeechStems.Contains(verb.Stem))
                    {
                        // Rewrite ," she smiled → ." She smiled  (one span: comma … verb)
                        var quoteChar = closingQuote.Text == "“" ? "”" : closingQuote.Text;
                        var fix = $".{quoteChar} {LineRuleHelpers.Capitalize(subject.Text)} {verb.Text}";
                        var lastWord = i >= 2 && tokens[i - 2].IsWord ? tokens[i - 2].Text : "";
                        yield return Note(Descriptor, NlpNoteSeverity.Info,
                                $"People can’t {Base(verb)} words. Make it an action beat in its own sentence: " +
                                $"“…{lastWord}.” {LineRuleHelpers.Capitalize(subject.Text)} {verb.Text}.")
                            .At(doc, punct, verb)
                            .WithSuggestion(fix);
                    }
                }
            }
        }

        private static bool IsTagSubject(TokenInfo t, SceneRuleContext ctx) =>
            !t.InQuote && (LineLexicons.TagPronouns.Contains(t.Text) || LineRuleHelpers.CharacterFor(t, ctx) is not null);

        private static readonly string[] ActionBases =
            ["smile", "grin", "laugh", "sigh", "shrug", "nod", "smirk", "frown", "chuckle",
             "giggle", "snort", "beam", "wink", "scowl", "grimace"];

        /// <summary>"smiled" → "smile", for "People can't smile words".</summary>
        private static string Base(TokenInfo verb) =>
            ActionBases.FirstOrDefault(b => WordStemmer.Stem(b) == verb.Stem) ?? verb.Lower;
    }

    /// <summary>"she said angrily" — the adverb tells how it was said instead of letting the line show it.</summary>
    public sealed class DialogueAdverbTagRule : ISceneRule
    {
        public RuleDescriptor Descriptor => RuleCatalog.DialogueAdverbTags;

        public IEnumerable<NlpNote> Evaluate(SceneRuleContext ctx)
        {
            var doc = ctx.Document;
            foreach (var s in doc.Sentences)
            {
                if (!s.Tokens.Any(LineRuleHelpers.IsQuote)) continue;
                var tokens = s.Tokens;
                for (int i = 0; i < tokens.Count; i++)
                {
                    var verb = tokens[i];
                    if (!verb.IsWord || verb.InQuote || !LineLexicons.SpeechVerbStems.Contains(verb.Stem)) continue;
                    if (i + 1 >= tokens.Count || !tokens[i + 1].IsWord) continue;
                    var adverb = tokens[i + 1];
                    if (!AdverbDetection.IsFlaggableAdverb(adverb, doc, ctx)) continue;

                    yield return Note(Descriptor, NlpNoteSeverity.Info,
                            $"“{verb.Lower} {adverb.Lower}” explains the line’s tone. " +
                            "If the dialogue already carries it, the adverb can go; if it doesn’t, sharpen the words themselves.")
                        .At(doc, verb, adverb)
                        .WithSuggestion(verb.Text);
                }
            }
        }
    }

    /// <summary>Long runs of untagged dialogue where readers lose track of who is speaking.</summary>
    public sealed class TalkingHeadsRule : ISceneRule
    {
        public RuleDescriptor Descriptor => RuleCatalog.TalkingHeads;

        public IEnumerable<NlpNote> Evaluate(SceneRuleContext ctx)
        {
            var doc = ctx.Document;
            int maxRun = (int)ctx.Threshold(Descriptor.Id, "maxLines", 6);
            int start = -1, run = 0;

            for (int p = 0; p <= doc.Paragraphs.Count; p++)
            {
                bool bare = p < doc.Paragraphs.Count && doc.Paragraphs[p].Kind == ParagraphKind.Dialogue;
                if (bare)
                {
                    if (run == 0) start = p;
                    run++;
                    continue;
                }

                if (run >= maxRun)
                {
                    var first = doc.Sentences[doc.Paragraphs[start].FirstSentenceIndex];
                    var word = first.Words.FirstOrDefault();
                    var note = Note(Descriptor, NlpNoteSeverity.Warning,
                        $"{run} lines of dialogue in a row with no tag or action beat. " +
                        "Readers may lose track of who is speaking. A “she said” or a small gesture every few lines keeps them oriented.");
                    if (word is not null) note.At(word);
                    note.SentenceIndex = first.Index;
                    yield return note;
                }
                run = 0;
            }
        }
    }

    // ── Wordiness ────────────────────────────────────────────────────────────

    /// <summary>Redundant phrases ("nodded his head") and weak verb+noun pairs ("made a decision").</summary>
    public sealed class RedundancyRule : ISceneRule
    {
        public RuleDescriptor Descriptor => RuleCatalog.Redundancy;

        public IEnumerable<NlpNote> Evaluate(SceneRuleContext ctx)
        {
            var doc = ctx.Document;
            var redundancies = LineLexicons.Redundancies.Select(r => r.Pattern).ToList();
            var weak = LineLexicons.WeakVerbPhrases.Select(w => w.Pattern).ToList();

            foreach (var s in doc.Sentences)
            {
                foreach (var (p, words) in PhraseScanner.Find(s, redundancies, narrationOnly: true))
                {
                    var replacement = Expand(LineLexicons.Redundancies[p].Replacement, words);
                    var phrase = doc.Text.Substring(words[0].Start, words[^1].End - words[0].Start);
                    yield return Note(Descriptor, NlpNoteSeverity.Info,
                            $"“{phrase}” says the same thing twice. “{replacement}” is enough.")
                        .At(doc, words[0], words[^1])
                        .WithSuggestion(replacement);
                }

                foreach (var (p, words) in PhraseScanner.Find(s, weak, narrationOnly: true))
                {
                    var better = LineLexicons.WeakVerbPhrases[p].Better.Matching(words[0].Text);
                    var phrase = doc.Text.Substring(words[0].Start, words[^1].End - words[0].Start);
                    yield return Note(Descriptor, NlpNoteSeverity.Info,
                            $"“{phrase}” hides the action in a noun. “{better}” says it directly.")
                        .At(doc, words[0], words[^1])
                        .WithSuggestion(MatchCase(better, words[0].Text));
                }
            }
        }

        private static string Expand(string template, List<TokenInfo> words) =>
            template.StartsWith('$') && int.TryParse(template[1..], out var n) && n < words.Count
                ? words[n].Text
                : MatchCase(template, words[0].Text);

        private static string MatchCase(string replacement, string original) =>
            original.Length > 0 && char.IsUpper(original[0]) ? LineRuleHelpers.Capitalize(replacement) : replacement;
    }

    /// <summary>Stock phrases readers have seen too often to feel.</summary>
    public sealed class ClicheRule : ISceneRule
    {
        public RuleDescriptor Descriptor => RuleCatalog.Cliche;

        public IEnumerable<NlpNote> Evaluate(SceneRuleContext ctx)
        {
            var doc = ctx.Document;
            foreach (var s in doc.Sentences)
            {
                foreach (var (_, words) in PhraseScanner.Find(s, LineLexicons.Cliches, narrationOnly: true))
                {
                    var phrase = doc.Text.Substring(words[0].Start, words[^1].End - words[0].Start);
                    yield return Note(Descriptor, NlpNoteSeverity.Info,
                            $"“{phrase}” is a stock phrase. Readers slide past it. " +
                            "What would this character, in this moment, specifically notice instead?")
                        .At(doc, words[0], words[^1]);
                }
            }
        }
    }

    /// <summary>Crutch words that pile up within one scene ("just", "really", "began to").</summary>
    public sealed class CrutchWordsRule : ISceneRule
    {
        public RuleDescriptor Descriptor => RuleCatalog.CrutchWords;

        public IEnumerable<NlpNote> Evaluate(SceneRuleContext ctx)
        {
            var doc = ctx.Document;
            int words = Math.Max(doc.WordCount, 1);
            // At least 3 uses, and more than one per ~250 words of the scene
            int minCount = Math.Max(3, (int)Math.Ceiling(words / ctx.Threshold(Descriptor.Id, "wordsPerUse", 250)));
            var patterns = LineLexicons.CrutchWords.Select(c => c.Pattern).ToList();

            var hits = new Dictionary<int, List<List<TokenInfo>>>();
            foreach (var s in doc.Sentences)
                foreach (var (p, match) in PhraseScanner.Find(s, patterns, narrationOnly: true))
                {
                    if (ctx.StyleSheet.IsWordAllowed(LineLexicons.CrutchWords[p].Label)) continue;
                    if (!hits.TryGetValue(p, out var list)) hits[p] = list = [];
                    list.Add(match);
                }

            foreach (var (p, matches) in hits.OrderByDescending(h => h.Value.Count))
            {
                if (matches.Count < minCount) continue;
                var label = LineLexicons.CrutchWords[p].Label;
                var firstWords = matches[0];
                var note = Note(Descriptor, NlpNoteSeverity.Info,
                        $"“{label}” appears {matches.Count} times in this scene. " +
                        "Each use is easy to miss while writing and easy to notice while reading. " +
                        "Try the scene without most of them; keep the ones that change the meaning.")
                    .At(doc, firstWords[0], firstWords[^1]);
                note.Evidence = label;
                foreach (var m in matches.Skip(1))
                    note.ExtraSpans.Add(new TextSpan(m[0].Start, m[^1].End - m[0].Start));
                yield return note;
            }
        }
    }

    // ── Names ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Probable misspellings of a character, place or item name ("Kaelan" when the
    /// Story Bible says "Kaelen"), with the canonical spelling as a one-click fix.
    /// </summary>
    public sealed class NameSpellingRule : ISceneRule
    {
        public RuleDescriptor Descriptor => RuleCatalog.NameSpelling;

        public IEnumerable<NlpNote> Evaluate(SceneRuleContext ctx)
        {
            var doc = ctx.Document;
            var names = NameIndex(ctx.Book);
            if (names.Count == 0) yield break;

            // Words also used in lowercase in this scene are ordinary words, not names
            var lowercaseWords = doc.Tokens.Where(t => t.IsWord && char.IsLower(t.Text[0]))
                .Select(t => t.Text).ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var t in doc.Tokens)
            {
                if (!t.IsWord || !char.IsUpper(t.Text[0])) continue;
                var bare = LineRuleHelpers.WithoutPossessive(t.Text);
                if (bare.Length < 4 || !bare.All(char.IsLetter)) continue;
                if (ctx.EntityWords.Contains(bare.ToLowerInvariant()) || ctx.StyleSheet.IsWordAllowed(bare)) continue;
                if (Lexicons.Stopwords.Contains(bare) || lowercaseWords.Contains(bare)) continue;

                var match = names
                    .Select(n => (Name: n, Score: StringSimilarity.JaroWinkler(bare.ToLowerInvariant(), n.ToLowerInvariant())))
                    .Where(x => x.Score >= 0.9 && StringSimilarity.Levenshtein(bare.ToLowerInvariant(), x.Name.ToLowerInvariant()) <= 2)
                    .OrderByDescending(x => x.Score)
                    .FirstOrDefault();
                if (match.Name is null) continue;

                var fixedText = match.Name + t.Text[bare.Length..];   // keep a possessive ending
                yield return Note(Descriptor, NlpNoteSeverity.Warning,
                        $"“{bare}” looks like a misspelling of “{match.Name}” from your Story Bible. " +
                        $"If it’s a different name on purpose, use “Allow” so it isn’t flagged again.")
                    .At(t)
                    .WithSuggestion(fixedText);
            }
        }

        private static List<string> NameIndex(Book? book)
        {
            if (book is null) return [];
            return book.Characters.SelectMany(c => new[] { c.Name, c.FullName, c.Aka })
                .Concat(book.Locations.SelectMany(l => new[] { l.Name, l.Aka }))
                .Concat(book.Items.SelectMany(i => new[] { i.Name, i.Aka }))
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .SelectMany(n => n.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries))
                .Select(w => w.Trim().Trim('"', '\''))
                .Where(w => w.Length >= 4 && char.IsUpper(w[0]) && w.All(char.IsLetter))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }

    /// <summary>Cast names so alike that readers will confuse them (Jon / Joan, Marla / Marta).</summary>
    public sealed class SimilarNamesRule : IBookRule
    {
        public RuleDescriptor Descriptor => RuleCatalog.SimilarNames;

        public IEnumerable<NlpNote> Evaluate(BookRuleContext ctx)
        {
            var names = ctx.Book?.Characters.Select(c => c.Name.Trim()).Where(n => n.Length >= 3).Distinct().ToList() ?? [];
            for (int i = 0; i < names.Count; i++)
            for (int j = i + 1; j < names.Count; j++)
            {
                var (a, b) = (names[i].ToLowerInvariant(), names[j].ToLowerInvariant());
                bool alike = StringSimilarity.JaroWinkler(a, b) >= 0.88
                    || (a[0] == b[0] && a[^1] == b[^1] && Math.Abs(a.Length - b.Length) <= 1 && StringSimilarity.Levenshtein(a, b) <= 2);
                if (!alike) continue;

                yield return Note(Descriptor, NlpNoteSeverity.Info,
                    $"“{names[i]}” and “{names[j]}” look and sound alike. Readers skim names by shape, " +
                    "so similar ones get confused, especially when both appear in the same scenes.");
            }
        }
    }

    /// <summary>String distance measures for name matching.</summary>
    public static class StringSimilarity
    {
        public static int Levenshtein(string a, string b)
        {
            var prev = new int[b.Length + 1];
            var cur = new int[b.Length + 1];
            for (int j = 0; j <= b.Length; j++) prev[j] = j;
            for (int i = 1; i <= a.Length; i++)
            {
                cur[0] = i;
                for (int j = 1; j <= b.Length; j++)
                    cur[j] = Math.Min(Math.Min(cur[j - 1] + 1, prev[j] + 1), prev[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
                (prev, cur) = (cur, prev);
            }
            return prev[b.Length];
        }

        public static double JaroWinkler(string a, string b)
        {
            if (a == b) return 1;
            if (a.Length == 0 || b.Length == 0) return 0;

            int window = Math.Max(0, Math.Max(a.Length, b.Length) / 2 - 1);
            var aMatched = new bool[a.Length];
            var bMatched = new bool[b.Length];
            int matches = 0;
            for (int i = 0; i < a.Length; i++)
            {
                for (int j = Math.Max(0, i - window); j < Math.Min(b.Length, i + window + 1); j++)
                {
                    if (bMatched[j] || a[i] != b[j]) continue;
                    aMatched[i] = bMatched[j] = true;
                    matches++;
                    break;
                }
            }
            if (matches == 0) return 0;

            int transpositions = 0, k = 0;
            for (int i = 0; i < a.Length; i++)
            {
                if (!aMatched[i]) continue;
                while (!bMatched[k]) k++;
                if (a[i] != b[k]) transpositions++;
                k++;
            }

            double m = matches;
            double jaro = (m / a.Length + m / b.Length + (m - transpositions / 2.0) / m) / 3.0;
            int prefix = 0;
            while (prefix < Math.Min(4, Math.Min(a.Length, b.Length)) && a[prefix] == b[prefix]) prefix++;
            return jaro + prefix * 0.1 * (1 - jaro);
        }
    }
}
