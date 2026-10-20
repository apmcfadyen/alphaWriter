using System;
using System.Collections.Generic;
using System.Linq;
using alphaWriter.Models.Analysis;
using alphaWriter.Services.Nlp.Parsing;
using static alphaWriter.Services.Nlp.Rules.RuleHelpers;

namespace alphaWriter.Services.Nlp.Rules
{
    // ── Pacing ───────────────────────────────────────────────────────────────

    public sealed class LongSentenceRule : ISceneRule
    {
        public RuleDescriptor Descriptor => RuleCatalog.LongSentence;

        public IEnumerable<NlpNote> Evaluate(SceneRuleContext ctx)
        {
            var doc = ctx.Document;
            var style = ctx.Style;
            if (doc.Sentences.Count <= 3 || style.SentenceLengthStdDev <= 0) yield break;

            double minWords = ctx.Threshold(Descriptor.Id, "minWords", 40);

            foreach (var s in doc.Sentences)
            {
                int wc = s.WordCount;
                double z = Math.Abs(wc - style.AverageSentenceLength) / style.SentenceLengthStdDev;
                if (z > 2.5 && wc > minWords)
                {
                    yield return Note(Descriptor, NlpNoteSeverity.Info,
                        $"Sentence {s.Index + 1} runs {wc} words while the scene averages {style.AverageSentenceLength:F0} — " +
                        "readers may lose the thread before the payoff arrives. " +
                        "Break at the natural clause boundary, letting each idea land separately.")
                        .At(s);
                }
            }
        }
    }

    public sealed class SceneLengthRule : ISceneRule
    {
        public RuleDescriptor Descriptor => RuleCatalog.SceneLength;

        public IEnumerable<NlpNote> Evaluate(SceneRuleContext ctx) =>
            CreateNotes(ctx.Pacing, ctx.SceneTitle);

        /// <summary>Shared with <see cref="PacingAnalyzer.DetectIssues"/>.</summary>
        internal static IEnumerable<NlpNote> CreateNotes(PacingMetrics m, string sceneTitle)
        {
            if (m.ChapterAverageWordCount <= 0) yield break;

            if (m.SceneToChapterRatio > 3.0)
            {
                yield return Note(RuleCatalog.SceneLength, NlpNoteSeverity.Warning,
                    $"'{sceneTitle}' is {m.SceneWordCount:N0} words — " +
                    $"{m.SceneToChapterRatio:F1}× the chapter average of {m.ChapterAverageWordCount:N0}. " +
                    "A scene this long against its surroundings disrupts the chapter's rhythm. " +
                    "Consider splitting at a natural tension point to give readers a breath.");
            }
            else if (m.SceneToChapterRatio < 0.2 && m.SceneWordCount > 0)
            {
                yield return Note(RuleCatalog.SceneLength, NlpNoteSeverity.Info,
                    $"'{sceneTitle}' is only {m.SceneWordCount:N0} words against a chapter average " +
                    $"of {m.ChapterAverageWordCount:N0}. Very short scenes can deliver sharp, " +
                    "impactful moments — but if this one feels incomplete, " +
                    "consider merging it with an adjacent scene.");
            }
        }
    }

    public sealed class NarrationRunRule : ISceneRule
    {
        public RuleDescriptor Descriptor => RuleCatalog.NarrationRun;

        public IEnumerable<NlpNote> Evaluate(SceneRuleContext ctx)
        {
            double maxRun = ctx.Threshold(Descriptor.Id, "maxRun", 15);

            int bestStart = -1, bestLen = 0, curStart = -1, curLen = 0;
            foreach (var s in ctx.Document.Sentences)
            {
                if (s.IsDialogue) { curLen = 0; curStart = -1; continue; }
                if (curLen == 0) curStart = s.Index;
                curLen++;
                if (curLen > bestLen) { bestLen = curLen; bestStart = curStart; }
            }

            if (bestLen > maxRun && bestStart >= 0)
            {
                var first = ctx.Document.Sentences[bestStart];
                var note = Note(Descriptor, NlpNoteSeverity.Warning, Message(ctx.SceneTitle, bestLen));
                // Point at the opening words of the run rather than painting the whole stretch
                var firstWord = first.Words.FirstOrDefault();
                if (firstWord is not null) note.At(firstWord);
                note.SentenceIndex = bestStart;
                yield return note;
            }
        }

        internal static string Message(string sceneTitle, int run) =>
            $"'{sceneTitle}' has a run of {run} consecutive narration sentences " +
            "without a single line of dialogue. Pure narration at this length can feel airless — " +
            "even a brief exchange or piece of direct speech can ground readers and reset the rhythm.";
    }

    // ── Adverbs ──────────────────────────────────────────────────────────────

    internal static class AdverbDetection
    {
        /// <summary>
        /// A manner adverb in narration: an -ly word that is not on the exclusion list
        /// (family, only, early…), not a mid-sentence proper noun (Emily, Kelly), not in
        /// dialogue (that's the character's voice), and — when POS tags exist — tagged ADV.
        /// </summary>
        public static bool IsFlaggableAdverb(TokenInfo t, SceneDocument doc, SceneRuleContext? ctx)
        {
            if (!t.IsWord || t.InQuote || t.Length < 5) return false;
            if (!t.Text.EndsWith("ly", StringComparison.OrdinalIgnoreCase)) return false;
            if (Lexicons.NotAdverbs.Contains(t.Text)) return false;
            if (t.IsCapitalizedMidSentence) return false;
            if (ctx is not null && (ctx.StyleSheet.IsWordAllowed(t.Text) || ctx.EntityWords.Contains(t.Lower)))
                return false;
            if (doc.HasPos && t.Pos.Length > 0 && t.Pos != "ADV") return false;
            return true;
        }

        public static double Density(SceneDocument doc)
        {
            int words = 0, adverbs = 0;
            foreach (var t in doc.Tokens)
            {
                if (!t.IsWord) continue;
                words++;
                if (IsFlaggableAdverb(t, doc, null)) adverbs++;
            }
            return words > 0 ? (double)adverbs / words : 0;
        }
    }

    public sealed class AdverbClusterRule : ISceneRule
    {
        public RuleDescriptor Descriptor => RuleCatalog.Adverbs;

        public IEnumerable<NlpNote> Evaluate(SceneRuleContext ctx)
        {
            var doc = ctx.Document;
            foreach (var s in doc.Sentences)
            {
                var adverbs = s.Tokens.Where(t => AdverbDetection.IsFlaggableAdverb(t, doc, ctx)).ToList();
                if (adverbs.Count < 2) continue;

                var examples = string.Join(", ", adverbs.Take(3).Select(w => $"\"{w.Text}\""));
                yield return Note(Descriptor, NlpNoteSeverity.Info,
                        $"Sentence {s.Index + 1} leans on {adverbs.Count} adverbs ({examples}) — " +
                        "a signal that the verbs may not be doing enough work. " +
                        "Replacing 'walked quickly' with 'strode' or 'spoke softly' with 'murmured' " +
                        "eliminates the modifier and sharpens the image.")
                    .At(adverbs[0])
                    .Also(adverbs.Skip(1));
            }
        }
    }

    public sealed class AdverbDensityRule : ISceneRule
    {
        public RuleDescriptor Descriptor => RuleCatalog.AdverbDensity;

        public IEnumerable<NlpNote> Evaluate(SceneRuleContext ctx)
        {
            var doc = ctx.Document;
            int totalWords = 0, totalAdverbs = 0;
            foreach (var t in doc.Tokens)
            {
                if (!t.IsWord) continue;
                totalWords++;
                if (AdverbDetection.IsFlaggableAdverb(t, doc, ctx)) totalAdverbs++;
            }

            if (totalWords < 50 || totalAdverbs == 0) yield break;

            double density = (double)totalAdverbs / totalWords;
            if (density > ctx.Threshold(Descriptor.Id, "maxDensity", 0.03))
            {
                yield return Note(Descriptor, NlpNoteSeverity.Info,
                    $"'{ctx.SceneTitle}' has an adverb density of {density:P0} ({totalAdverbs} adverbs across {totalWords} words) — " +
                    "above the threshold where adverbs start to pad rather than clarify. " +
                    "Audit each -ly word and ask whether a more specific verb makes it redundant.");
            }
        }
    }

    // ── Show, don't tell ─────────────────────────────────────────────────────

    public sealed class ShowDontTellRule : ISceneRule
    {
        public RuleDescriptor Descriptor => RuleCatalog.ShowDontTell;

        public IEnumerable<NlpNote> Evaluate(SceneRuleContext ctx)
        {
            var doc = ctx.Document;
            foreach (var s in doc.Sentences)
            {
                // "Telling" is natural in speech
                if (s.IsDialogue) continue;

                var match = FindTelling(s, ctx);
                if (match is null) continue;
                var (verb, emotion) = match.Value;

                yield return Note(Descriptor, NlpNoteSeverity.Warning,
                        $"Sentence {s.Index + 1} names the emotion directly — \"{verb.Lower} {emotion.Lower}\" labels the " +
                        "feeling where a physical sign or action would be more powerful. " +
                        $"What does {emotion.Lower} look like in the body, in the room, in what the character does next?")
                    .At(doc, verb, emotion);
            }
        }

        private static (TokenInfo Verb, TokenInfo Emotion)? FindTelling(SentenceInfo s, SceneRuleContext ctx)
        {
            var tokens = s.Tokens;
            for (int i = 0; i < tokens.Count; i++)
            {
                var verb = tokens[i];
                if (!verb.IsWord || verb.InQuote || !Lexicons.FeelingVerbStems.Contains(verb.Stem)) continue;
                if (!HasCharacterSubject(tokens, i, ctx)) continue;

                // Look ahead up to 4 words, stopping at clause punctuation or a negation
                int wordsSeen = 0;
                for (int j = i + 1; j < tokens.Count && wordsSeen < 4; j++)
                {
                    var t = tokens[j];
                    if (!t.IsWord)
                    {
                        if (t.Text is "," or ";" or ":" or "." or "!" or "?" or "—") break;
                        continue;
                    }
                    wordsSeen++;
                    if (IsNegation(t)) break;
                    if (Lexicons.TellingEmotionWords.Contains(t.Text))
                        return (verb, t);
                }
            }
            return null;
        }

        /// <summary>
        /// The verb's subject should be a person: a pronoun or a name. This keeps
        /// "the room seemed gloomy" and "the answer was content" from firing.
        /// Negations between subject and verb ("she didn't feel afraid") cancel the match.
        /// </summary>
        private static bool HasCharacterSubject(List<TokenInfo> tokens, int verbIndex, SceneRuleContext ctx)
        {
            for (int k = verbIndex - 1, words = 0; k >= 0 && words < 3; k--)
            {
                var t = tokens[k];
                if (!t.IsWord) return false;
                words++;
                if (IsNegation(t)) return false;
                if (Lexicons.InterveningAdverbs.Contains(t.Text) || t.Text.EndsWith("ly", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (Lexicons.SubjectPronouns.Contains(t.Text)) return true;
                if (ctx.EntityWords.Contains(t.Lower)) return true;
                if (char.IsUpper(t.Text[0]) && !Lexicons.Stopwords.Contains(t.Text))
                    return !ctx.Document.HasPos || t.Pos is "" or "PROPN";
                return false;
            }
            return false;
        }

        private static bool IsNegation(TokenInfo t) =>
            Lexicons.Negations.Contains(t.Text)
            || t.Text.EndsWith("n't", StringComparison.OrdinalIgnoreCase)
            || t.Text.EndsWith("n’t", StringComparison.OrdinalIgnoreCase);
    }

    // ── Echoes ───────────────────────────────────────────────────────────────

    public sealed class EchoRule : ISceneRule
    {
        public RuleDescriptor Descriptor => RuleCatalog.Echo;

        public IEnumerable<NlpNote> Evaluate(SceneRuleContext ctx)
        {
            var doc = ctx.Document;
            if (doc.Sentences.Count < 3) yield break;

            int window = (int)ctx.Threshold(Descriptor.Id, "window", 5);
            int minCount = (int)ctx.Threshold(Descriptor.Id, "minCount", 3);

            // stem → sentence indices (unique per sentence) and every token of that family
            var sentencesByStem = new Dictionary<string, List<int>>(StringComparer.Ordinal);
            var tokensByStem = new Dictionary<string, List<TokenInfo>>(StringComparer.Ordinal);

            foreach (var s in doc.Sentences)
            {
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var t in s.Tokens)
                {
                    if (!IsContentWord(t, ctx)) continue;
                    if (!tokensByStem.TryGetValue(t.Stem, out var toks))
                        tokensByStem[t.Stem] = toks = [];
                    toks.Add(t);
                    if (!seen.Add(t.Stem)) continue;
                    if (!sentencesByStem.TryGetValue(t.Stem, out var list))
                        sentencesByStem[t.Stem] = list = [];
                    list.Add(s.Index);
                }
            }

            foreach (var (stem, indices) in sentencesByStem.OrderBy(kv => kv.Value[0]))
            {
                if (indices.Count < minCount) continue;

                for (int i = 0; i <= indices.Count - minCount; i++)
                {
                    if (indices[i + minCount - 1] - indices[i] > window - 1) continue;

                    int first = indices[i], last = first + window - 1;
                    var inWindow = tokensByStem[stem]
                        .Where(t => { var si = doc.SentenceAt(t.Start)!.Index; return si >= first && si <= last; })
                        .ToList();
                    int windowCount = indices.Count(idx => idx >= first && idx <= last);
                    var word = inWindow[0].Lower;

                    yield return Note(Descriptor, NlpNoteSeverity.Warning,
                            $"'{word}' appears {windowCount} times within five sentences of sentence {first + 1} — " +
                            "the repetition may register as an echo even when readers can't name why. " +
                            "A synonym or restructured sentence breaks the rhythm without losing the idea.")
                        .At(inWindow[0])
                        .Also(inWindow.Skip(1));
                    break;
                }
            }
        }

        private static bool IsContentWord(TokenInfo t, SceneRuleContext ctx) =>
            t.IsWord
            && t.Length >= 5
            && t.Text.All(char.IsLetter)
            && !Lexicons.Stopwords.Contains(t.Text)
            && !ctx.EntityWords.Contains(t.Lower)
            && !ctx.StyleSheet.IsWordAllowed(t.Text);
    }

    // ── Clause complexity ────────────────────────────────────────────────────

    public sealed class ClauseStackingRule : ISceneRule
    {
        public RuleDescriptor Descriptor => RuleCatalog.ClauseStacking;

        public IEnumerable<NlpNote> Evaluate(SceneRuleContext ctx)
        {
            int max = (int)ctx.Threshold(Descriptor.Id, "maxPerSentence", 3);
            foreach (var s in ctx.Document.Sentences)
            {
                var conj = s.Tokens.Where(t => t.IsWord && Lexicons.SubordinatingConjunctions.Contains(t.Text)).ToList();
                if (conj.Count < max) continue;

                yield return Note(Descriptor, NlpNoteSeverity.Info,
                        $"Sentence {s.Index + 1} stacks {conj.Count} subordinate clauses in a single breath — " +
                        "the main idea risks getting buried under qualifications. " +
                        "Try splitting into two sentences, or cutting the least essential clause.")
                    .At(conj[0])
                    .Also(conj.Skip(1));
            }
        }
    }

    public sealed class ClauseDensityRule : ISceneRule
    {
        public RuleDescriptor Descriptor => RuleCatalog.ClauseDensity;

        public IEnumerable<NlpNote> Evaluate(SceneRuleContext ctx)
        {
            var sentences = ctx.Document.Sentences;
            if (sentences.Count < 8) yield break;

            double mean = sentences.Average(s =>
                s.Tokens.Count(t => t.IsWord && Lexicons.SubordinatingConjunctions.Contains(t.Text)));

            if (mean > ctx.Threshold(Descriptor.Id, "maxMean", 1.5))
            {
                yield return Note(Descriptor, NlpNoteSeverity.Info,
                    $"'{ctx.SceneTitle}' averages {mean:F1} subordinate clauses per sentence — " +
                    "the prose carries consistent structural weight throughout. " +
                    "This works for introspective or literary passages; in action or dialogue-driven scenes, " +
                    "flatter sentence structure sharpens the pace.");
            }
        }
    }

    // ── POS-tier rules ───────────────────────────────────────────────────────

    public sealed class PassiveVoiceRule : ISceneRule
    {
        public RuleDescriptor Descriptor => RuleCatalog.Passive;

        public IEnumerable<NlpNote> Evaluate(SceneRuleContext ctx)
        {
            var doc = ctx.Document;
            if (!doc.HasPos) yield break;

            foreach (var s in doc.Sentences)
            {
                var match = FindPassive(s.Tokens);
                if (match is null) continue;
                var (aux, participle, agent) = match.Value;

                string agentHint = agent is null
                    ? "the actor is hidden or delayed"
                    : $"the actor ('{agent}') arrives only at the end";

                yield return Note(Descriptor, NlpNoteSeverity.Info,
                        $"Sentence {s.Index + 1} uses passive voice: " +
                        $"\"{Truncate(s.Text, 70)}\" — {agentHint}. " +
                        "Rewriting in active voice sharpens the action " +
                        "and keeps readers oriented in who is doing what.")
                    .At(doc, aux, participle);
            }
        }

        /// <summary>
        /// A form of "be" (tagged AUX) followed — past any adverbs, auxiliaries or
        /// negation — by a past participle tagged VERB. Progressives ("was running")
        /// and statal participles ("was tired", "was supposed to") are excluded unless
        /// an explicit "by" agent follows.
        /// </summary>
        internal static (TokenInfo Aux, TokenInfo Participle, string? Agent)? FindPassive(List<TokenInfo> tokens)
        {
            for (int j = 0; j < tokens.Count - 1; j++)
            {
                var aux = tokens[j];
                if (aux.Pos != "AUX" || !Lexicons.BeVerbs.Contains(aux.Text)) continue;

                for (int k = j + 1; k < tokens.Count; k++)
                {
                    var t = tokens[k];
                    if (!t.IsWord) break;
                    if (t.Pos == "VERB")
                    {
                        if (!IsPastParticiple(t.Text)) break;
                        var agent = FindAgent(tokens, k);
                        if (Lexicons.StatalParticiples.Contains(t.Text) && agent is null) break;
                        return (aux, t, agent);
                    }
                    if (t.Pos is not ("ADV" or "AUX" or "PART")) break;
                }
            }
            return null;
        }

        private static bool IsPastParticiple(string word)
        {
            if (word.EndsWith("ing", StringComparison.OrdinalIgnoreCase)) return false;
            return word.EndsWith("ed", StringComparison.OrdinalIgnoreCase)
                || word.EndsWith("en", StringComparison.OrdinalIgnoreCase)
                || Lexicons.IrregularParticiples.Contains(word);
        }

        /// <summary>Returns the words after "by" when an agent phrase follows within a few tokens.</summary>
        private static string? FindAgent(List<TokenInfo> tokens, int participleIndex)
        {
            for (int k = participleIndex + 1; k < tokens.Count && k <= participleIndex + 4; k++)
            {
                if (!tokens[k].IsWord) return null;
                if (!tokens[k].Text.Equals("by", StringComparison.OrdinalIgnoreCase)) continue;
                var agentWords = tokens.Skip(k + 1).TakeWhile(t => t.IsWord).Take(3).Select(t => t.Text).ToList();
                return agentWords.Count > 0 ? string.Join(" ", agentWords) : null;
            }
            return null;
        }
    }

    public sealed class OpenerMonotonyRule : ISceneRule
    {
        public RuleDescriptor Descriptor => RuleCatalog.OpenerMonotony;

        public IEnumerable<NlpNote> Evaluate(SceneRuleContext ctx)
        {
            var doc = ctx.Document;
            if (!doc.HasPos || doc.Sentences.Count < 5) yield break;

            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            int tagged = 0;
            foreach (var s in doc.Sentences)
            {
                var first = s.Tokens.FirstOrDefault(t => t.IsWord && t.Pos.Length > 0);
                if (first is null) continue;
                var category = ClassifyOpener(first.Pos);
                counts[category] = counts.GetValueOrDefault(category) + 1;
                tagged++;
            }
            if (tagged < 5) yield break;

            var dominant = counts.OrderByDescending(kv => kv.Value).First();
            double share = (double)dominant.Value / tagged;
            if (share > ctx.Threshold(Descriptor.Id, "maxShare", 0.60))
            {
                int percent = (int)Math.Round(share * 100);
                yield return Note(Descriptor, NlpNoteSeverity.Info,
                    $"{percent}% of sentences in '{ctx.SceneTitle}' open with a {dominant.Key} — " +
                    "the rhythmic pattern becomes noticeable on the page. " +
                    "Varying your openers (starting with an adverb, a dependent clause, " +
                    "or an inverted subject) creates variety without changing meaning.");
            }
        }

        internal static string ClassifyOpener(string pos) => pos switch
        {
            "NOUN" or "PROPN" => "noun subject",
            "PRON" => "pronoun",
            "VERB" => "verb phrase",
            "ADV" => "adverb",
            "SCONJ" or "CCONJ" => "conjunction",
            "DET" => "article or determiner",
            "ADJ" => "adjective",
            "ADP" => "prepositional phrase",
            "NUM" => "numeral",
            _ => "other"
        };
    }
}
