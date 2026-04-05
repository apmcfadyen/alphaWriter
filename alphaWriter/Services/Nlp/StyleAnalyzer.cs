using System;
using System.Collections.Generic;
using System.Linq;
using alphaWriter.Models.Analysis;

namespace alphaWriter.Services.Nlp
{
    public class StyleAnalyzer : IStyleAnalyzer
    {
        // ── Be-verb lookup for passive voice detection ────────────────────────

        private static readonly HashSet<string> BeVerbs =
            new(["am", "is", "are", "was", "were", "be", "been", "being"],
                StringComparer.OrdinalIgnoreCase);

        // ── Emotion words that "name" a feeling directly (show-don't-tell) ───

        private static readonly HashSet<string> TellingEmotionWords =
            new(StringComparer.OrdinalIgnoreCase)
            {
                "angry", "angered", "sad", "sadness", "happy", "happiness", "afraid",
                "scared", "worried", "nervous", "excited", "surprised", "shocked",
                "confused", "tired", "exhausted", "upset", "frustrated", "embarrassed",
                "ashamed", "guilty", "proud", "jealous", "lonely", "bored", "curious",
                "disgusted", "horrified", "terrified", "anxious", "depressed", "miserable",
                "hopeful", "hopeless", "joyful", "cheerful", "melancholy", "content",
                "distressed", "frightened", "startled", "elated", "bitter", "resentful",
                "grief", "sorrow", "joy", "fear", "rage", "fury", "panic", "dread",
                "despair", "ecstasy", "bliss", "guilt", "shame", "envy", "jealousy",
                "resentment", "bitterness", "disappointment", "regret", "remorse",
                "apprehensive", "heartbroken", "devastated", "overjoyed", "thrilled",
                "gloomy", "forlorn", "wistful", "relieved", "humiliated", "shamed",
                "delighted", "enraged", "terrified", "bewildered"
            };

        // Verbs that introduce a told emotional state
        private static readonly HashSet<string> FeelingVerbs =
            new(StringComparer.OrdinalIgnoreCase)
            {
                "felt", "feel", "feels", "feeling",
                "seemed", "seem", "seems",
                "looked", "appeared", "appears", "appear",
                "was", "were", "is", "are", "become", "became"
            };

        // Subordinating conjunctions for clause-density detection
        private static readonly HashSet<string> SubordConjunctions =
            new(StringComparer.OrdinalIgnoreCase)
            {
                "because", "although", "though", "unless", "until",
                "whereas", "whenever", "wherever", "whoever", "whatever",
                "however", "if", "when", "while", "since", "before",
                "after", "as", "whether", "once", "provided"
            };

        // Stopwords for proximity-echo content-word filtering
        private static readonly HashSet<string> Stopwords =
            new(StringComparer.OrdinalIgnoreCase)
            {
                "the", "a", "an", "is", "are", "was", "were", "be", "been", "being",
                "have", "has", "had", "do", "does", "did", "will", "would", "could",
                "should", "may", "might", "must", "shall", "can", "to", "of", "in",
                "on", "at", "by", "for", "with", "as", "from", "up", "out", "into",
                "then", "than", "this", "that", "these", "those", "it", "its", "his",
                "her", "our", "your", "their", "my", "we", "they", "he", "she",
                "and", "or", "but", "not", "no", "so", "if", "when", "where", "how",
                "all", "any", "both", "each", "more", "most", "other", "some", "just",
                "also", "very", "about", "over", "such", "only", "here", "there",
                "what", "who", "him", "them", "me", "us", "you", "been", "still",
                "even", "back", "after", "again", "down", "through", "never", "now",
                "well", "said", "could", "would"
            };

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

        // ── Passive voice detection ───────────────────────────────────────────

        public List<NlpNote> DetectPassiveVoice(IReadOnlyList<string> sentences,
            IPosTaggingService posService,
            string sceneId, string sceneTitle, string chapterTitle)
        {
            var notes = new List<NlpNote>();
            if (sentences.Count == 0) return notes;

            var taggedSentences = posService.TagSentences(sentences);

            for (int i = 0; i < sentences.Count; i++)
            {
                if (ContainsPassiveConstruction(taggedSentences[i]))
                {
                    notes.Add(new NlpNote
                    {
                        Severity = NlpNoteSeverity.Info,
                        Category = NlpNoteCategory.CopyEditor,
                        SceneId = sceneId,
                        SceneTitle = sceneTitle,
                        ChapterTitle = chapterTitle,
                        SentenceIndex = i,
                        Message = $"Sentence {i + 1} uses passive voice: " +
                                  $"\"{TruncateSentence(sentences[i], 70)}\" — " +
                                  "the actor is hidden or delayed. Rewriting in active voice sharpens the action " +
                                  "and keeps readers oriented in who is doing what."
                    });
                }
            }

            return notes;
        }

        public int CountPassiveSentences(IReadOnlyList<string> sentences, IPosTaggingService posService)
        {
            if (sentences.Count == 0) return 0;
            var taggedSentences = posService.TagSentences(sentences);
            return taggedSentences.Count(ContainsPassiveConstruction);
        }

        private static bool ContainsPassiveConstruction(IReadOnlyList<(string Value, string Pos)> tokens)
        {
            for (int j = 0; j < tokens.Count - 1; j++)
            {
                var (value, pos) = tokens[j];
                if (pos != "AUX" || !BeVerbs.Contains(value)) continue;

                for (int k = j + 1; k < tokens.Count; k++)
                {
                    var nextPos = tokens[k].Pos;
                    if (nextPos == "VERB") return true;
                    if (nextPos != "ADV" && nextPos != "AUX" && nextPos != "PART")
                        break;
                }
            }
            return false;
        }

        // ── Adverb density detection ──────────────────────────────────────────

        public List<NlpNote> DetectAdverbDensity(IReadOnlyList<string> sentences,
            string sceneId, string sceneTitle, string chapterTitle)
        {
            var notes = new List<NlpNote>();
            if (sentences.Count == 0) return notes;

            int totalWords = 0;
            int totalLyWords = 0;

            for (int i = 0; i < sentences.Count; i++)
            {
                var words = sentences[i]
                    .Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries);

                var lyWords = words
                    .Select(w => w.Trim('.', ',', '!', '?', ';', ':', '"',
                                        '\u201C', '\u201D', '\'', '\u2018', '\u2019'))
                    .Where(w => w.Length >= 5 &&
                                w.EndsWith("ly", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                totalWords += words.Length;
                totalLyWords += lyWords.Count;

                if (lyWords.Count >= 2)
                {
                    var examples = string.Join(", ", lyWords.Take(3).Select(w => $"\"{w}\""));
                    notes.Add(new NlpNote
                    {
                        Severity = NlpNoteSeverity.Info,
                        Category = NlpNoteCategory.CopyEditor,
                        SceneId = sceneId,
                        SceneTitle = sceneTitle,
                        ChapterTitle = chapterTitle,
                        SentenceIndex = i,
                        Message = $"Sentence {i + 1} leans on {lyWords.Count} adverbs ({examples}) — " +
                                  "a signal that the verbs may not be doing enough work. " +
                                  "Replacing 'walked quickly' with 'strode' or 'spoke softly' with 'murmured' " +
                                  "eliminates the modifier and sharpens the image."
                    });
                }
            }

            if (totalWords >= 50 && totalLyWords > 0)
            {
                double density = (double)totalLyWords / totalWords;
                if (density > 0.03)
                {
                    notes.Add(new NlpNote
                    {
                        Severity = NlpNoteSeverity.Info,
                        Category = NlpNoteCategory.CopyEditor,
                        SceneId = sceneId,
                        SceneTitle = sceneTitle,
                        ChapterTitle = chapterTitle,
                        Message = $"'{sceneTitle}' has an adverb density of {density:P0} ({totalLyWords} adverbs across {totalWords} words) — " +
                                  "above the threshold where adverbs start to pad rather than clarify. " +
                                  "Audit each -ly word and ask whether a more specific verb makes it redundant."
                    });
                }
            }

            return notes;
        }

        public double ComputeAdverbDensity(IReadOnlyList<string> sentences)
        {
            int totalWords = 0, totalLyWords = 0;
            foreach (var sentence in sentences)
            {
                var words = sentence.Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
                totalWords += words.Length;
                totalLyWords += words
                    .Select(w => w.Trim('.', ',', '!', '?', ';', ':', '"', '\u201C', '\u201D', '\'', '\u2018', '\u2019'))
                    .Count(w => w.Length >= 5 && w.EndsWith("ly", StringComparison.OrdinalIgnoreCase));
            }
            return totalWords > 0 ? (double)totalLyWords / totalWords : 0;
        }

        // ── Show-don't-tell detection ─────────────────────────────────────────

        public List<NlpNote> DetectShowDontTell(IReadOnlyList<string> sentences,
            string sceneId, string sceneTitle, string chapterTitle)
        {
            var notes = new List<NlpNote>();
            if (sentences.Count == 0) return notes;

            for (int i = 0; i < sentences.Count; i++)
            {
                var (feelingVerb, emotionWord) = FindTellingPattern(sentences[i]);
                if (feelingVerb == null || emotionWord == null) continue;

                // Skip dialogue lines — "telling" is more natural in speech
                if (NlpTextExtractor.IsDialogue(sentences[i])) continue;

                notes.Add(new NlpNote
                {
                    Severity = NlpNoteSeverity.Warning,
                    Category = NlpNoteCategory.LineEditor,
                    SceneId = sceneId,
                    SceneTitle = sceneTitle,
                    ChapterTitle = chapterTitle,
                    SentenceIndex = i,
                    Message = $"Sentence {i + 1} names the emotion directly — \"{feelingVerb} {emotionWord}\" labels the " +
                              "feeling where a physical sign or action would be more powerful. " +
                              $"What does {emotionWord} look like in the body, in the room, in what the character does next?"
                });
            }

            return notes;
        }

        private static (string? feelingVerb, string? emotionWord) FindTellingPattern(string sentence)
        {
            var words = sentence
                .Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries)
                .Select(w => w.Trim('.', ',', '!', '?', ';', ':', '"', '\'', '\u201C', '\u201D', '\u2018', '\u2019'))
                .Where(w => w.Length > 0)
                .ToArray();

            for (int i = 0; i < words.Length; i++)
            {
                if (!FeelingVerbs.Contains(words[i])) continue;

                // Look ahead up to 4 words for an emotion word
                for (int j = i + 1; j < Math.Min(i + 5, words.Length); j++)
                {
                    if (TellingEmotionWords.Contains(words[j]))
                        return (words[i].ToLowerInvariant(), words[j].ToLowerInvariant());
                }
            }
            return (null, null);
        }

        // ── Proximity echo detection ──────────────────────────────────────────

        public List<NlpNote> DetectProximityEchoes(IReadOnlyList<string> sentences,
            string sceneId, string sceneTitle, string chapterTitle)
        {
            var notes = new List<NlpNote>();
            if (sentences.Count < 3) return notes;

            // Collect content words (unique per sentence)
            var sentenceWords = sentences
                .Select(s => ExtractContentWords(s))
                .ToList();

            // Map each word to the sentence indices where it appears
            var wordOccurrences = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < sentenceWords.Count; i++)
            {
                foreach (var word in sentenceWords[i])
                {
                    if (!wordOccurrences.TryGetValue(word, out var list))
                        wordOccurrences[word] = list = [];
                    list.Add(i);
                }
            }

            var flaggedWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var (word, indices) in wordOccurrences.OrderBy(kv => kv.Value[0]))
            {
                if (flaggedWords.Contains(word) || indices.Count < 3) continue;

                // Check if any 3 occurrences span ≤ 4 sentence gaps (5-sentence window)
                for (int i = 0; i <= indices.Count - 3; i++)
                {
                    if (indices[i + 2] - indices[i] > 4) continue;

                    flaggedWords.Add(word);
                    int windowCount = indices.Count(idx => idx >= indices[i] && idx <= indices[i] + 4);
                    notes.Add(new NlpNote
                    {
                        Severity = NlpNoteSeverity.Warning,
                        Category = NlpNoteCategory.CopyEditor,
                        SceneId = sceneId,
                        SceneTitle = sceneTitle,
                        ChapterTitle = chapterTitle,
                        SentenceIndex = indices[i],
                        Message = $"'{word}' appears {windowCount} times within five sentences of sentence {indices[i] + 1} — " +
                                  "the repetition may register as an echo even when readers can't name why. " +
                                  "A synonym or restructured sentence breaks the rhythm without losing the idea."
                    });
                    break;
                }
            }

            return notes;
        }

        private static HashSet<string> ExtractContentWords(string sentence)
        {
            return sentence
                .Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .Select(w => w.ToLowerInvariant()
                    .Trim('.', ',', '!', '?', ';', ':', '"', '\'', '\u201C', '\u201D', '\u2018', '\u2019', '-', '\u2014'))
                .Where(w => w.Length >= 5 && !Stopwords.Contains(w) && w.All(char.IsLetter))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        // ── Sentence opener monotony detection ───────────────────────────────

        public List<NlpNote> DetectSentenceOpenerMonotony(IReadOnlyList<string> sentences,
            IPosTaggingService posService,
            string sceneId, string sceneTitle, string chapterTitle)
        {
            var notes = new List<NlpNote>();
            if (sentences.Count < 5) return notes;

            var taggedSentences = posService.TagSentences(sentences);

            var openerCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            int taggedCount = 0;

            foreach (var tokens in taggedSentences)
            {
                var firstContent = tokens.FirstOrDefault(t =>
                    t.Pos != "PUNCT" && t.Pos != "SPACE" && !string.IsNullOrWhiteSpace(t.Value));
                if (firstContent == default) continue;

                var category = ClassifyOpener(firstContent.Pos);
                openerCounts.TryGetValue(category, out int count);
                openerCounts[category] = count + 1;
                taggedCount++;
            }

            if (taggedCount < 5) return notes;

            var dominant = openerCounts.OrderByDescending(kv => kv.Value).First();
            double dominantFraction = (double)dominant.Value / taggedCount;

            if (dominantFraction > 0.60)
            {
                int percent = (int)Math.Round(dominantFraction * 100);
                notes.Add(new NlpNote
                {
                    Severity = NlpNoteSeverity.Info,
                    Category = NlpNoteCategory.LineEditor,
                    SceneId = sceneId,
                    SceneTitle = sceneTitle,
                    ChapterTitle = chapterTitle,
                    Message = $"{percent}% of sentences in '{sceneTitle}' open with a {dominant.Key} — " +
                              "the rhythmic pattern becomes noticeable on the page. " +
                              "Varying your openers (starting with an adverb, a dependent clause, " +
                              "or an inverted subject) creates variety without changing meaning."
                });
            }

            return notes;
        }

        private static string ClassifyOpener(string pos) => pos switch
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

        private static double ComputeOpenerVariety(IReadOnlyList<(string Value, string Pos)>[] taggedSentences)
        {
            if (taggedSentences.Length <= 1) return 0;

            var openerCounts = new Dictionary<string, int>();
            foreach (var tokens in taggedSentences)
            {
                var firstContent = tokens.FirstOrDefault(t =>
                    t.Pos != "PUNCT" && t.Pos != "SPACE" && !string.IsNullOrWhiteSpace(t.Value));
                if (firstContent == default) continue;
                var category = ClassifyOpener(firstContent.Pos);
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

        // ── Subordinate clause density detection ─────────────────────────────

        public List<NlpNote> DetectSubordinateClauseDensity(IReadOnlyList<string> sentences,
            string sceneId, string sceneTitle, string chapterTitle)
        {
            var notes = new List<NlpNote>();
            if (sentences.Count == 0) return notes;

            var perSentence = sentences
                .Select(s => CountSubordConjunctions(s))
                .ToList();

            double sceneMean = perSentence.Average();

            // Per-sentence: flag when 3+ subordinate conjunctions in one sentence
            for (int i = 0; i < sentences.Count; i++)
            {
                if (perSentence[i] >= 3)
                {
                    notes.Add(new NlpNote
                    {
                        Severity = NlpNoteSeverity.Info,
                        Category = NlpNoteCategory.LineEditor,
                        SceneId = sceneId,
                        SceneTitle = sceneTitle,
                        ChapterTitle = chapterTitle,
                        SentenceIndex = i,
                        Message = $"Sentence {i + 1} stacks {perSentence[i]} subordinate clauses in a single breath — " +
                                  "the main idea risks getting buried under qualifications. " +
                                  "Try splitting into two sentences, or cutting the least essential clause."
                    });
                }
            }

            // Scene-level: flag consistently complex prose (mean > 1.5 and at least 8 sentences)
            if (sentences.Count >= 8 && sceneMean > 1.5)
            {
                notes.Add(new NlpNote
                {
                    Severity = NlpNoteSeverity.Info,
                    Category = NlpNoteCategory.LineEditor,
                    SceneId = sceneId,
                    SceneTitle = sceneTitle,
                    ChapterTitle = chapterTitle,
                    Message = $"'{sceneTitle}' averages {sceneMean:F1} subordinate clauses per sentence — " +
                              "the prose carries consistent structural weight throughout. " +
                              "This works for introspective or literary passages; in action or dialogue-driven scenes, " +
                              "flatter sentence structure sharpens the pace."
                });
            }

            return notes;
        }

        private static int CountSubordConjunctions(string sentence)
        {
            return sentence
                .Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .Select(w => w.Trim('.', ',', '!', '?', ';', ':', '"', '\'', '\u201C', '\u201D'))
                .Count(w => SubordConjunctions.Contains(w));
        }

        private static double ComputeClauseComplexity(IReadOnlyList<(string Value, string Pos)>[] taggedSentences)
        {
            if (taggedSentences.Length == 0) return 0;
            int totalSconjTokens = taggedSentences.Sum(tokens => tokens.Count(t => t.Pos == "SCONJ"));
            return (double)totalSconjTokens / taggedSentences.Length;
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static string TruncateSentence(string s, int maxLen) =>
            s.Length <= maxLen ? s : s[..maxLen] + "…";

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
