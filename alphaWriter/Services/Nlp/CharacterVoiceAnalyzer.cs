using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using alphaWriter.Models;
using alphaWriter.Models.Analysis;

namespace alphaWriter.Services.Nlp
{
    public class CharacterVoiceAnalyzer : ICharacterVoiceAnalyzer
    {
        private readonly IStyleAnalyzer _styleAnalyzer;

        public CharacterVoiceAnalyzer(IStyleAnalyzer styleAnalyzer)
        {
            _styleAnalyzer = styleAnalyzer;
        }

        public List<CharacterVoiceProfile> BuildProfiles(List<SceneAnalysisResult> results, Book book,
            IPosTaggingService? posService = null)
        {
            var profiles = new List<CharacterVoiceProfile>();

            foreach (var character in book.Characters)
            {
                var viewpointScenes = results
                    .Where(r => r.ViewpointCharacterIds.Contains(character.Id))
                    .ToList();

                if (viewpointScenes.Count == 0)
                    continue;

                var allSentences = viewpointScenes
                    .SelectMany(r => r.Sentences)
                    .ToList();

                var sentenceTexts = allSentences.Select(s => s.Text).ToList();

                if (sentenceTexts.Count == 0)
                    continue;

                // Full style profile including readability score
                var style = _styleAnalyzer.AnalyzeExtended(sentenceTexts, posService);

                var emotionDist = ComputeEmotionDistribution(allSentences);
                var distinctiveWords = ComputeDistinctiveWords(sentenceTexts, results);

                double adverbDensity = _styleAnalyzer.ComputeAdverbDensity(sentenceTexts);
                double passiveVoiceRate = posService != null && posService.IsLoaded && sentenceTexts.Count > 0
                    ? (double)_styleAnalyzer.CountPassiveSentences(sentenceTexts, posService) / sentenceTexts.Count
                    : 0;

                // Build per-scene snapshots for the consistency timeline
                var snapshots = BuildSceneSnapshots(viewpointScenes, posService);

                profiles.Add(new CharacterVoiceProfile
                {
                    CharacterId = character.Id,
                    CharacterName = character.Name,
                    Style = style,
                    EmotionDistribution = emotionDist,
                    DistinctiveWords = distinctiveWords,
                    SceneCount = viewpointScenes.Count,
                    TotalWords = style.TotalWords,
                    AdverbDensity = adverbDensity,
                    PassiveVoiceRate = passiveVoiceRate,
                    OpenerVariety = style.OpenerVariety,
                    ClauseComplexity = style.ClauseComplexity,
                    ReadabilityScore = style.ReadabilityScore,
                    SceneSnapshots = snapshots
                });
            }

            return profiles;
        }

        private List<SceneVoiceSnapshot> BuildSceneSnapshots(
            List<SceneAnalysisResult> scenes, IPosTaggingService? posService)
        {
            var snapshots = new List<SceneVoiceSnapshot>();

            foreach (var scene in scenes)
            {
                var texts = scene.Sentences.Select(s => s.Text).ToList();
                if (texts.Count == 0) continue;

                var sceneStyle = _styleAnalyzer.AnalyzeExtended(texts, posService);
                double adverb = _styleAnalyzer.ComputeAdverbDensity(texts);
                double passive = posService != null && posService.IsLoaded && texts.Count > 0
                    ? (double)_styleAnalyzer.CountPassiveSentences(texts, posService) / texts.Count
                    : 0;

                snapshots.Add(new SceneVoiceSnapshot
                {
                    SceneTitle = scene.SceneTitle,
                    ChapterTitle = scene.ChapterTitle,
                    AvgSentenceLength = sceneStyle.AverageSentenceLength,
                    VocabularyRichness = sceneStyle.VocabularyRichness,
                    ContractionRate = sceneStyle.ContractionRate,
                    DialogueRatio = sceneStyle.DialogueRatio,
                    AdverbDensity = adverb,
                    PassiveVoiceRate = passive,
                    ReadabilityScore = sceneStyle.ReadabilityScore
                });
            }

            return snapshots;
        }

        public List<NlpNote> DetectVoiceAnomalies(List<CharacterVoiceProfile> profiles,
            List<SceneAnalysisResult> results, Book book)
        {
            var notes = new List<NlpNote>();

            foreach (var profile in profiles)
            {
                var viewpointScenes = results
                    .Where(r => r.ViewpointCharacterIds.Contains(profile.CharacterId))
                    .ToList();

                if (viewpointScenes.Count < 3)
                    continue;

                DetectContractionInconsistency(profile, viewpointScenes, notes);
                DetectDialogueRatioInconsistency(profile, viewpointScenes, notes);
                DetectReadabilityDrift(profile, viewpointScenes, notes);
            }

            DetectNarratorDrift(results, notes);

            return notes;
        }

        // ── Cross-scene voice consistency ────────────────────────────────────

        private void DetectContractionInconsistency(CharacterVoiceProfile profile,
            List<SceneAnalysisResult> scenes, List<NlpNote> notes)
        {
            var perSceneRates = new List<(SceneAnalysisResult scene, double rate)>();

            foreach (var scene in scenes)
            {
                var texts = scene.Sentences.Select(s => s.Text).ToList();
                if (texts.Count == 0) continue;
                var sceneStyle = _styleAnalyzer.Analyze(texts);
                perSceneRates.Add((scene, sceneStyle.ContractionRate));
            }

            if (perSceneRates.Count < 3) return;

            double avg = perSceneRates.Average(r => r.rate);
            double stdDev = Math.Sqrt(perSceneRates.Sum(r => Math.Pow(r.rate - avg, 2))
                / (perSceneRates.Count - 1));

            if (stdDev < 0.01) return;

            foreach (var (scene, rate) in perSceneRates)
            {
                double z = Math.Abs(rate - avg) / stdDev;
                if (z > 2.0)
                {
                    string direction = rate > avg ? "more casual" : "more formal";
                    string rateStr = rate > avg
                        ? $"{rate:P0} vs their usual {avg:P0}"
                        : $"{rate:P0} vs their usual {avg:P0}";
                    notes.Add(new NlpNote
                    {
                        Severity = NlpNoteSeverity.Warning,
                        Category = NlpNoteCategory.LineEditor,
                        SceneId = scene.SceneId,
                        SceneTitle = scene.SceneTitle,
                        ChapterTitle = scene.ChapterTitle,
                        Message = $"{profile.CharacterName} sounds {direction} in '{scene.SceneTitle}' — " +
                                  $"contraction rate is {rateStr}. " +
                                  "If this isn't a deliberate shift in their arc, the voice may have drifted. " +
                                  "Reading the scene aloud against an earlier one can make the difference audible."
                    });
                }
            }
        }

        private void DetectDialogueRatioInconsistency(CharacterVoiceProfile profile,
            List<SceneAnalysisResult> scenes, List<NlpNote> notes)
        {
            var perSceneRatios = new List<(SceneAnalysisResult scene, double ratio)>();

            foreach (var scene in scenes)
            {
                var texts = scene.Sentences.Select(s => s.Text).ToList();
                if (texts.Count == 0) continue;
                var sceneStyle = _styleAnalyzer.Analyze(texts);
                perSceneRatios.Add((scene, sceneStyle.DialogueRatio));
            }

            if (perSceneRatios.Count < 3) return;

            double avg = perSceneRatios.Average(r => r.ratio);
            double stdDev = Math.Sqrt(perSceneRatios.Sum(r => Math.Pow(r.ratio - avg, 2))
                / (perSceneRatios.Count - 1));

            if (stdDev < 0.01) return;

            foreach (var (scene, ratio) in perSceneRatios)
            {
                double z = Math.Abs(ratio - avg) / stdDev;
                if (z > 2.0)
                {
                    string direction = ratio > avg ? "more dialogue-heavy" : "more narration-heavy";
                    notes.Add(new NlpNote
                    {
                        Severity = NlpNoteSeverity.Info,
                        Category = NlpNoteCategory.LineEditor,
                        SceneId = scene.SceneId,
                        SceneTitle = scene.SceneTitle,
                        ChapterTitle = scene.ChapterTitle,
                        Message = $"'{scene.SceneTitle}' is {direction} than {profile.CharacterName}'s typical scenes " +
                                  $"({ratio:P0} dialogue vs their average {avg:P0}). " +
                                  "This may be intentional — a quiet introspective scene or an unusually charged exchange — " +
                                  "but if unexpected, it may signal a structural imbalance."
                    });
                }
            }
        }

        private static void DetectReadabilityDrift(CharacterVoiceProfile profile,
            List<SceneAnalysisResult> scenes, List<NlpNote> notes)
        {
            if (profile.SceneSnapshots.Count < 3) return;

            var snapshots = profile.SceneSnapshots;
            double avg = snapshots.Average(s => s.ReadabilityScore);
            double stdDev = snapshots.Count > 1
                ? Math.Sqrt(snapshots.Sum(s => Math.Pow(s.ReadabilityScore - avg, 2)) / (snapshots.Count - 1))
                : 0;

            if (stdDev < 3.0) return; // negligible variation

            // Check for a significant trend in the second half vs. first half
            int mid = snapshots.Count / 2;
            double firstHalfAvg = snapshots.Take(mid).Average(s => s.ReadabilityScore);
            double secondHalfAvg = snapshots.Skip(mid).Average(s => s.ReadabilityScore);
            double shift = Math.Abs(secondHalfAvg - firstHalfAvg);

            if (shift < 10) return;

            string direction = secondHalfAvg < firstHalfAvg ? "denser" : "simpler";
            // Find the scene that represents the shift boundary (first scene in second half)
            var boundaryScene = scenes.Count >= mid ? scenes[mid] : scenes.Last();

            notes.Add(new NlpNote
            {
                Severity = NlpNoteSeverity.Warning,
                Category = NlpNoteCategory.LineEditor,
                SceneId = boundaryScene.SceneId,
                SceneTitle = boundaryScene.SceneTitle,
                ChapterTitle = boundaryScene.ChapterTitle,
                Message = $"{profile.CharacterName}'s prose grows {direction} in their later scenes — " +
                          $"readability shifts from {firstHalfAvg:F0} in early appearances to {secondHalfAvg:F0} later. " +
                          "A gradual change can signal natural character development; " +
                          "an abrupt one may indicate unintentional voice drift."
            });
        }

        // ── Narrator voice drift ─────────────────────────────────────────────

        private static void DetectNarratorDrift(List<SceneAnalysisResult> results, List<NlpNote> notes)
        {
            foreach (var scene in results)
            {
                if (scene.ViewpointCharacterIds.Count == 0)
                    continue;

                var narrationSentences = scene.Sentences
                    .Where(s => !NlpTextExtractor.IsDialogue(s.Text))
                    .ToList();

                if (narrationSentences.Count < 15)
                    continue;

                const int baselineSize = 10;
                var baseline = narrationSentences.Take(baselineSize).ToList();
                double baselineAvgLength = baseline.Average(s => s.WordCount);
                double baselineStdDev = baseline.Count > 1
                    ? Math.Sqrt(baseline.Sum(s => Math.Pow(s.WordCount - baselineAvgLength, 2))
                        / (baseline.Count - 1))
                    : 0;

                if (baselineStdDev < 1.0) baselineStdDev = 1.0;

                for (int i = baselineSize; i < narrationSentences.Count; i++)
                {
                    var sentence = narrationSentences[i];
                    double lengthZ = Math.Abs(sentence.WordCount - baselineAvgLength) / baselineStdDev;

                    bool styleDrift = lengthZ > 3.0;
                    bool embeddingDrift = sentence.EmbeddingDistance > 0.4f;

                    if (styleDrift && embeddingDrift)
                    {
                        notes.Add(new NlpNote
                        {
                            Severity = NlpNoteSeverity.Info,
                            Category = NlpNoteCategory.LineEditor,
                            SceneId = scene.SceneId,
                            SceneTitle = scene.SceneTitle,
                            ChapterTitle = scene.ChapterTitle,
                            SentenceIndex = sentence.Index,
                            Message = $"Sentence {sentence.Index + 1} in '{scene.SceneTitle}' breaks from the narrator's " +
                                      "established voice — both the sentence structure and the subject matter shift " +
                                      "significantly from the opening baseline. " +
                                      "This may be an unintentional slip into a different register."
                        });
                    }
                }
            }
        }

        // ── Dialogue voice profiling ─────────────────────────────────────────

        public List<DialogueVoiceProfile> BuildDialogueProfiles(List<SceneAnalysisResult> results, Book book,
            IPosTaggingService? posService = null)
        {
            var profiles = new List<DialogueVoiceProfile>();
            if (book.Characters.Count == 0 || results.Count == 0)
                return profiles;

            var nameLookup = DialogueSpeakerAttributor.BuildNameLookup(book.Characters);
            if (nameLookup.Count == 0)
                return profiles;

            // Attribute dialogue across all scenes
            var allAttributions = new List<DialogueAttribution>();
            foreach (var scene in results)
            {
                allAttributions.AddRange(
                    DialogueSpeakerAttributor.AttributeDialogue(scene.Sentences, scene.SceneId, nameLookup, book.Characters));
            }

            if (allAttributions.Count == 0)
                return profiles;

            // Build a lookup for SentenceAnalysis by (sceneId, sentenceIndex)
            var sentenceLookup = new Dictionary<(string sceneId, int index), SentenceAnalysis>();
            foreach (var scene in results)
            {
                foreach (var sentence in scene.Sentences)
                    sentenceLookup.TryAdd((scene.SceneId, sentence.Index), sentence);
            }

            // Group attributions by character
            var grouped = allAttributions.GroupBy(a => a.CharacterId);

            foreach (var group in grouped)
            {
                var charAttributions = group.ToList();
                var dialogueTexts = charAttributions.Select(a => a.DialogueText).ToList();

                if (dialogueTexts.Count == 0)
                    continue;

                // Find matching SentenceAnalysis objects for emotion data
                var matchingSentences = charAttributions
                    .Select(a => sentenceLookup.GetValueOrDefault((a.SceneId, a.SentenceIndex)))
                    .Where(s => s != null)
                    .Cast<SentenceAnalysis>()
                    .ToList();

                var style = _styleAnalyzer.AnalyzeExtended(dialogueTexts, posService);
                var emotionDist = ComputeEmotionDistribution(matchingSentences);
                var distinctiveWords = ComputeDistinctiveWords(dialogueTexts, results);

                double adverbDensity = _styleAnalyzer.ComputeAdverbDensity(dialogueTexts);
                double passiveVoiceRate = posService != null && posService.IsLoaded && dialogueTexts.Count > 0
                    ? (double)_styleAnalyzer.CountPassiveSentences(dialogueTexts, posService) / dialogueTexts.Count
                    : 0;

                int exclamationCount = dialogueTexts.Count(t => t.TrimEnd().EndsWith('!'));
                double exclamationRate = dialogueTexts.Count > 0 ? (double)exclamationCount / dialogueTexts.Count : 0;

                int sceneCount = charAttributions.Select(a => a.SceneId).Distinct().Count();

                profiles.Add(new DialogueVoiceProfile
                {
                    CharacterId = group.Key,
                    CharacterName = charAttributions[0].CharacterName,
                    Style = style,
                    EmotionDistribution = emotionDist,
                    DistinctiveWords = distinctiveWords,
                    DialogueLineCount = dialogueTexts.Count,
                    TotalWords = style.TotalWords,
                    SceneCount = sceneCount,
                    AvgSentenceLength = style.AverageSentenceLength,
                    VocabularyRichness = style.VocabularyRichness,
                    ContractionRate = style.ContractionRate,
                    ReadabilityScore = style.ReadabilityScore,
                    AdverbDensity = adverbDensity,
                    PassiveVoiceRate = passiveVoiceRate,
                    ClauseComplexity = style.ClauseComplexity,
                    ExclamationRate = exclamationRate
                });
            }

            return profiles;
        }

        public List<NlpNote> DetectDialogueVoiceAnomalies(List<DialogueVoiceProfile> profiles)
        {
            var notes = new List<NlpNote>();
            if (profiles.Count < 2)
                return notes;

            // Extract style vectors for normalization
            var metricKeys = new Func<DialogueVoiceProfile, double>[]
            {
                p => p.AvgSentenceLength,
                p => p.VocabularyRichness,
                p => p.ContractionRate,
                p => p.ReadabilityScore,
                p => p.ClauseComplexity,
                p => p.ExclamationRate
            };

            // Compute min/max for normalization
            var mins = metricKeys.Select(fn => profiles.Min(fn)).ToArray();
            var maxs = metricKeys.Select(fn => profiles.Max(fn)).ToArray();

            // Normalize each profile to 0-1 vectors
            var normalized = profiles.Select(p =>
                metricKeys.Select((fn, i) =>
                {
                    double range = maxs[i] - mins[i];
                    return range > 0 ? (fn(p) - mins[i]) / range : 0.5;
                }).ToArray()
            ).ToList();

            // Compare each pair
            for (int i = 0; i < profiles.Count; i++)
            {
                for (int j = i + 1; j < profiles.Count; j++)
                {
                    // Euclidean distance on normalized style vector
                    double sumSq = 0;
                    for (int k = 0; k < metricKeys.Length; k++)
                    {
                        double diff = normalized[i][k] - normalized[j][k];
                        sumSq += diff * diff;
                    }
                    double distance = Math.Sqrt(sumSq / metricKeys.Length); // RMS distance, 0-1
                    double styleSimilarity = 1.0 - distance;

                    // Jaccard coefficient on top-10 distinctive words
                    var wordsA = new HashSet<string>(
                        profiles[i].DistinctiveWords.Take(10).Select(w => w.Word),
                        StringComparer.OrdinalIgnoreCase);
                    var wordsB = new HashSet<string>(
                        profiles[j].DistinctiveWords.Take(10).Select(w => w.Word),
                        StringComparer.OrdinalIgnoreCase);

                    var intersection = wordsA.Intersect(wordsB, StringComparer.OrdinalIgnoreCase).ToList();
                    var union = wordsA.Union(wordsB, StringComparer.OrdinalIgnoreCase);
                    double jaccard = union.Any() ? (double)intersection.Count / union.Count() : 0;

                    double combined = 0.7 * styleSimilarity + 0.3 * jaccard;

                    if (combined > 0.85)
                    {
                        string sharedWords = intersection.Count > 0
                            ? $" Shared vocabulary: {string.Join(", ", intersection)}."
                            : "";

                        notes.Add(new NlpNote
                        {
                            Severity = NlpNoteSeverity.Warning,
                            Category = NlpNoteCategory.DevelopmentalEditor,
                            Message = $"{profiles[i].CharacterName} and {profiles[j].CharacterName} sound very similar in dialogue " +
                                      $"(similarity: {combined:P0}). " +
                                      "Readers may struggle to distinguish their voices without speech tags. " +
                                      "Consider differentiating through sentence length, vocabulary, " +
                                      $"or formality level.{sharedWords}"
                        });
                    }
                }
            }

            return notes;
        }

        // ── Emotion distribution ─────────────────────────────────────────────

        private static Dictionary<EmotionLabel, double> ComputeEmotionDistribution(
            List<SentenceAnalysis> sentences)
        {
            var labelSums = new Dictionary<EmotionLabel, double>();
            double total = 0;

            foreach (var sentence in sentences)
            {
                foreach (var (label, confidence) in sentence.Emotions)
                {
                    labelSums[label] = labelSums.GetValueOrDefault(label) + confidence;
                    total += confidence;
                }
            }

            if (total > 0)
            {
                foreach (var label in labelSums.Keys.ToList())
                    labelSums[label] /= total;
            }

            return labelSums;
        }

        // ── TF-IDF distinctive words ─────────────────────────────────────────

        private static List<(string Word, double TfIdf)> ComputeDistinctiveWords(
            List<string> characterSentences, List<SceneAnalysisResult> allResults)
        {
            var charWordFreq = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            int charTotalWords = 0;

            foreach (var sentence in characterSentences)
            {
                foreach (var word in ExtractWords(sentence))
                {
                    if (StopWords.Contains(word.ToLowerInvariant())) continue;
                    charWordFreq[word.ToLowerInvariant()] =
                        charWordFreq.GetValueOrDefault(word.ToLowerInvariant()) + 1;
                    charTotalWords++;
                }
            }

            if (charTotalWords == 0) return [];

            var sceneWordSets = allResults
                .Select(r => new HashSet<string>(
                    r.Sentences.SelectMany(s => ExtractWords(s.Text))
                        .Select(w => w.ToLowerInvariant()),
                    StringComparer.OrdinalIgnoreCase))
                .ToList();

            int numDocuments = sceneWordSets.Count;
            if (numDocuments == 0) return [];

            var tfIdfScores = new List<(string Word, double TfIdf)>();

            foreach (var (word, count) in charWordFreq)
            {
                double tf = (double)count / charTotalWords;
                int docCount = sceneWordSets.Count(set => set.Contains(word));
                double idf = Math.Log((double)(numDocuments + 1) / (docCount + 1)) + 1;
                tfIdfScores.Add((word, tf * idf));
            }

            return tfIdfScores
                .OrderByDescending(x => x.TfIdf)
                .Take(20)
                .ToList();
        }

        private static readonly Regex WordPattern = new(@"[\w][\w'\u2019]*[\w]|[\w]+", RegexOptions.Compiled);

        private static IEnumerable<string> ExtractWords(string text)
        {
            return WordPattern.Matches(text)
                .Select(m => m.Value)
                .Where(w => w.Length > 1);
        }

        private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
        {
            "the", "a", "an", "and", "or", "but", "in", "on", "at", "to", "for",
            "of", "with", "by", "from", "as", "is", "was", "are", "were", "been",
            "be", "have", "has", "had", "do", "does", "did", "will", "would", "could",
            "should", "may", "might", "shall", "can", "need", "dare", "ought", "used",
            "it", "its", "he", "she", "they", "them", "their", "his", "her", "my",
            "your", "our", "we", "you", "me", "him", "us", "this", "that", "these",
            "those", "who", "whom", "which", "what", "where", "when", "how", "why",
            "not", "no", "nor", "if", "then", "than", "too", "very", "just", "about",
            "up", "out", "so", "into", "over", "after", "before", "between", "under",
            "again", "once", "here", "there", "all", "each", "every", "both", "few",
            "more", "most", "other", "some", "such", "only", "own", "same", "also",
            "back", "even", "still", "just", "now", "well", "like", "said", "one",
            "two", "first", "new", "way", "any", "many", "much", "make", "made",
            "get", "got", "go", "went", "see", "saw", "come", "came", "take", "took",
            "know", "knew", "think", "thought", "look", "looked", "say", "tell", "told",
            // contraction fragments (safety net)
            "isn", "don", "aren", "couldn", "wouldn", "shouldn", "wasn", "weren",
            "hasn", "haven", "hadn", "won", "didn", "mustn", "needn", "shan"
        };
    }
}
