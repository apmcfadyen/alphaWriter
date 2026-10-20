using System;
using System.Collections.Generic;
using System.Linq;
using alphaWriter.Models.Analysis;
using alphaWriter.Services.Nlp.Parsing;
using alphaWriter.Services.Nlp.Rules;

namespace alphaWriter.Services.Nlp
{
    public class StyleAnalyzer : IStyleAnalyzer
    {
        // ── Analyze ───────────────────────────────────────────────────────────

        public StyleProfile Analyze(IReadOnlyList<string> sentences)
        {
            if (sentences.Count == 0)
                return new StyleProfile();

            var wordCounts = new List<int>(sentences.Count);
            int totalWords = 0;
            int totalWordLength = 0;
            int totalSyllables = 0;
            int contractionCount = 0;
            int dialogueCount = 0;

            foreach (var sentence in sentences)
            {
                var words = sentence.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
                wordCounts.Add(words.Length);
                totalWords += words.Length;

                foreach (var word in words)
                {
                    totalWordLength += word.Length;
                    totalSyllables += CountSyllables(word);
                }

                if (NlpTextExtractor.HasContraction(sentence))
                    contractionCount++;
                if (NlpTextExtractor.IsDialogue(sentence))
                    dialogueCount++;
            }

            double avgLength = totalWords > 0 ? (double)totalWords / sentences.Count : 0;
            double variance = wordCounts.Count > 1
                ? wordCounts.Sum(wc => Math.Pow(wc - avgLength, 2)) / (wordCounts.Count - 1)
                : 0;

            var allWords = sentences
                .SelectMany(s => s.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries))
                .Select(w => w.ToLowerInvariant().Trim('.', ',', '!', '?', ';', ':', '"', '\'', '\u201C', '\u201D'))
                .Where(w => w.Length > 0)
                .ToList();

            double vocabRichness = allWords.Count > 0
                ? (double)allWords.Distinct().Count() / allWords.Count
                : 0;

            // Flesch-Kincaid Reading Ease: higher = easier to read (0–100)
            double readability = 0;
            if (totalWords > 0 && sentences.Count > 0)
            {
                double wordsPerSentence = (double)totalWords / sentences.Count;
                double syllablesPerWord = (double)totalSyllables / totalWords;
                readability = Math.Max(0, Math.Min(100,
                    206.835 - 1.015 * wordsPerSentence - 84.6 * syllablesPerWord));
            }

            return new StyleProfile
            {
                AverageSentenceLength = avgLength,
                SentenceLengthStdDev = Math.Sqrt(variance),
                VocabularyRichness = vocabRichness,
                AverageWordLength = totalWords > 0 ? (double)totalWordLength / totalWords : 0,
                ContractionRate = sentences.Count > 0 ? (double)contractionCount / sentences.Count : 0,
                TotalSentences = sentences.Count,
                TotalWords = totalWords,
                DialogueRatio = sentences.Count > 0 ? (double)dialogueCount / sentences.Count : 0,
                ReadabilityScore = readability
            };
        }

        public StyleProfile AnalyzeExtended(IReadOnlyList<string> sentences, IPosTaggingService? posService)
        {
            var profile = Analyze(sentences);

            if (posService != null && sentences.Count > 0)
            {
                var taggedSentences = posService.TagSentences(sentences);
                profile.OpenerVariety = ComputeOpenerVariety(taggedSentences);
                profile.ClauseComplexity = ComputeClauseComplexity(taggedSentences);
            }

            return profile;
        }

        // ── Detect anomalies ─────────────────────────────────────────────────

        public List<NlpNote> DetectAnomalies(StyleProfile sceneProfile, StyleProfile chapterProfile,
            string sceneId, string sceneTitle, string chapterTitle)
        {
            var notes = new List<NlpNote>();

            if (chapterProfile.TotalSentences < 2 || sceneProfile.TotalSentences < 2)
                return notes;

            // Sentence length deviation
            if (chapterProfile.SentenceLengthStdDev > 0)
            {
                double zScore = Math.Abs(sceneProfile.AverageSentenceLength - chapterProfile.AverageSentenceLength)
                    / chapterProfile.SentenceLengthStdDev;
                if (zScore > 1.5)
                {
                    string direction = sceneProfile.AverageSentenceLength > chapterProfile.AverageSentenceLength
                        ? "longer" : "shorter";
                    string implication = sceneProfile.AverageSentenceLength > chapterProfile.AverageSentenceLength
                        ? "Longer sentences slow the reader's pace — if this is an action or tension scene, consider tightening."
                        : "Shorter sentences accelerate pace — if this is a reflective or emotional scene, that urgency may undercut the tone.";
                    notes.Add(new NlpNote
                    {
                        RuleId = RuleCatalog.SentenceLengthDrift.Id,
                        Severity = NlpNoteSeverity.Warning,
                        Category = NlpNoteCategory.CopyEditor,
                        SceneId = sceneId,
                        SceneTitle = sceneTitle,
                        ChapterTitle = chapterTitle,
                        Message = $"'{sceneTitle}' averages {sceneProfile.AverageSentenceLength:F1} words per sentence, " +
                                  $"significantly {direction} than the chapter average of {chapterProfile.AverageSentenceLength:F1}. " +
                                  implication
                    });
                }
            }

            // Vocabulary richness deviation
            if (chapterProfile.VocabularyRichness > 0)
            {
                double diff = Math.Abs(sceneProfile.VocabularyRichness - chapterProfile.VocabularyRichness);
                if (diff > 0.15)
                {
                    string direction = sceneProfile.VocabularyRichness > chapterProfile.VocabularyRichness
                        ? "more varied" : "more repetitive";
                    string advice = sceneProfile.VocabularyRichness > chapterProfile.VocabularyRichness
                        ? "Rich word choice here — make sure the register fits the scene's emotional temperature."
                        : "Repeated words accumulate into a numbing effect; broadening the vocabulary would add texture.";
                    notes.Add(new NlpNote
                    {
                        Severity = NlpNoteSeverity.Info,
                        RuleId = RuleCatalog.VocabularyDrift.Id,
                        Category = NlpNoteCategory.DevelopmentalEditor,
                        SceneId = sceneId,
                        SceneTitle = sceneTitle,
                        ChapterTitle = chapterTitle,
                        Message = $"'{sceneTitle}' uses a {direction} vocabulary than the surrounding chapter " +
                                  $"(type-token ratio: {sceneProfile.VocabularyRichness:P0} vs {chapterProfile.VocabularyRichness:P0}). " +
                                  advice
                    });
                }
            }

            // Readability deviation
            if (chapterProfile.ReadabilityScore > 0 && sceneProfile.ReadabilityScore > 0)
            {
                double diff = sceneProfile.ReadabilityScore - chapterProfile.ReadabilityScore;
                if (Math.Abs(diff) > 15)
                {
                    string direction = diff < 0 ? "harder" : "easier";
                    string advice = diff < 0
                        ? "Dense prose can be intentional in introspective or literary passages — but if this scene needs to move quickly, simplifying sentence structure will help readers keep up."
                        : "Simpler prose reads faster; if this scene carries emotional or intellectual weight, the light density may undercut its impact.";
                    notes.Add(new NlpNote
                    {
                        Severity = NlpNoteSeverity.Info,
                        RuleId = RuleCatalog.ReadabilityDrift.Id,
                        Category = NlpNoteCategory.DevelopmentalEditor,
                        SceneId = sceneId,
                        SceneTitle = sceneTitle,
                        ChapterTitle = chapterTitle,
                        Message = $"'{sceneTitle}' reads {Math.Abs(diff):F0} points {direction} than the chapter average " +
                                  $"(readability: {sceneProfile.ReadabilityScore:F0} vs {chapterProfile.ReadabilityScore:F0}). " +
                                  advice
                    });
                }
            }

            return notes;
        }

        // ── Rule adapters ────────────────────────────────────────────────────
        // The detectors below predate the rule engine. They now build a SceneDocument
        // from the sentence list and delegate to the corresponding rule, so the legacy
        // API and the engine can never disagree.

        private List<NlpNote> RunRules(IReadOnlyList<string> sentences, IPosTaggingService? posService,
            string sceneId, string sceneTitle, string chapterTitle, params ISceneRule[] rules)
        {
            if (sentences.Count == 0) return [];

            var doc = BuildDocument(sentences, posService);
            var ctx = new SceneRuleContext
            {
                Document = doc,
                SceneId = sceneId,
                SceneTitle = sceneTitle,
                ChapterTitle = chapterTitle,
                Style = Analyze(sentences)
            };
            return new ProseRuleEngine(rules, []).EvaluateScene(ctx);
        }

        private static SceneDocument BuildDocument(IReadOnlyList<string> sentences, IPosTaggingService? posService)
        {
            var doc = SceneDocumentBuilder.FromSentences(sentences);
            if (posService is not null)
                SceneDocumentBuilder.ApplyLegacyTags(doc, posService.TagSentences(sentences));
            return doc;
        }

        public List<NlpNote> DetectPassiveVoice(IReadOnlyList<string> sentences,
            IPosTaggingService posService,
            string sceneId, string sceneTitle, string chapterTitle) =>
            RunRules(sentences, posService, sceneId, sceneTitle, chapterTitle, new PassiveVoiceRule());

        public int CountPassiveSentences(IReadOnlyList<string> sentences, IPosTaggingService posService)
        {
            if (sentences.Count == 0) return 0;
            var doc = BuildDocument(sentences, posService);
            return doc.Sentences.Count(s => PassiveVoiceRule.FindPassive(s.Tokens) is not null);
        }

        public List<NlpNote> DetectAdverbDensity(IReadOnlyList<string> sentences,
            string sceneId, string sceneTitle, string chapterTitle) =>
            RunRules(sentences, null, sceneId, sceneTitle, chapterTitle,
                new AdverbClusterRule(), new AdverbDensityRule());

        public double ComputeAdverbDensity(IReadOnlyList<string> sentences) =>
            sentences.Count == 0 ? 0 : AdverbDetection.Density(SceneDocumentBuilder.FromSentences(sentences));

        public List<NlpNote> DetectShowDontTell(IReadOnlyList<string> sentences,
            string sceneId, string sceneTitle, string chapterTitle) =>
            RunRules(sentences, null, sceneId, sceneTitle, chapterTitle, new ShowDontTellRule());

        public List<NlpNote> DetectProximityEchoes(IReadOnlyList<string> sentences,
            string sceneId, string sceneTitle, string chapterTitle) =>
            RunRules(sentences, null, sceneId, sceneTitle, chapterTitle, new EchoRule());

        public List<NlpNote> DetectSentenceOpenerMonotony(IReadOnlyList<string> sentences,
            IPosTaggingService posService,
            string sceneId, string sceneTitle, string chapterTitle) =>
            RunRules(sentences, posService, sceneId, sceneTitle, chapterTitle, new OpenerMonotonyRule());

        public List<NlpNote> DetectSubordinateClauseDensity(IReadOnlyList<string> sentences,
            string sceneId, string sceneTitle, string chapterTitle) =>
            RunRules(sentences, null, sceneId, sceneTitle, chapterTitle,
                new ClauseStackingRule(), new ClauseDensityRule());

        // ── Extended profile metrics ─────────────────────────────────────────

        private static double ComputeOpenerVariety(IReadOnlyList<(string Value, string Pos)>[] taggedSentences)
        {
            if (taggedSentences.Length <= 1) return 0;

            var openerCounts = new Dictionary<string, int>();
            foreach (var tokens in taggedSentences)
            {
                var firstContent = tokens.FirstOrDefault(t =>
                    t.Pos != "PUNCT" && t.Pos != "SPACE" && !string.IsNullOrWhiteSpace(t.Value));
                if (firstContent == default) continue;
                var category = OpenerMonotonyRule.ClassifyOpener(firstContent.Pos);
                openerCounts.TryGetValue(category, out int count);
                openerCounts[category] = count + 1;
            }

            if (openerCounts.Count <= 1) return 0;

            int total = openerCounts.Values.Sum();
            double entropy = 0;
            foreach (var count in openerCounts.Values)
            {
                double p = (double)count / total;
                if (p > 0) entropy -= p * Math.Log2(p);
            }

            double maxEntropy = Math.Log2(openerCounts.Count);
            return maxEntropy > 0 ? entropy / maxEntropy : 0;
        }

        private static double ComputeClauseComplexity(IReadOnlyList<(string Value, string Pos)>[] taggedSentences)
        {
            if (taggedSentences.Length == 0) return 0;
            int totalSconjTokens = taggedSentences.Sum(tokens => tokens.Count(t => t.Pos == "SCONJ"));
            return (double)totalSconjTokens / taggedSentences.Length;
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static int CountSyllables(string word)
        {
            if (string.IsNullOrEmpty(word)) return 1;

            // Strip punctuation and convert to lowercase
            var clean = new string(word.Where(c => char.IsLetter(c)).ToArray()).ToLowerInvariant();
            if (clean.Length == 0) return 1;

            // Remove silent trailing 'e' (not after another vowel)
            if (clean.Length > 2 && clean[^1] == 'e' && !"aeiou".Contains(clean[^2]))
                clean = clean[..^1];

            // Count vowel groups
            int count = 0;
            bool prevVowel = false;
            foreach (char c in clean)
            {
                bool isVowel = "aeiou".Contains(c);
                if (isVowel && !prevVowel) count++;
                prevVowel = isVowel;
            }

            return Math.Max(1, count);
        }
    }
}
