using System;
using System.Collections.Generic;
using System.Linq;
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

        public List<CharacterVoiceProfile> BuildProfiles(List<SceneAnalysisResult> results, Book book)
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

                var style = _styleAnalyzer.Analyze(sentenceTexts);
                var emotionDist = ComputeEmotionDistribution(allSentences);
                var distinctiveWords = ComputeDistinctiveWords(sentenceTexts, results);

                profiles.Add(new CharacterVoiceProfile
                {
                    CharacterId = character.Id,
                    CharacterName = character.Name,
                    Style = style,
                    EmotionDistribution = emotionDist,
                    DistinctiveWords = distinctiveWords,
                    SceneCount = viewpointScenes.Count,
                    TotalWords = style.TotalWords
                });
            }

            return profiles;
        }

        public List<NlpNote> DetectVoiceAnomalies(List<CharacterVoiceProfile> profiles,
            List<SceneAnalysisResult> results, Book book)
        {
            var notes = new List<NlpNote>();

            // (a) Cross-scene voice consistency per character
            foreach (var profile in profiles)
            {
                var viewpointScenes = results
                    .Where(r => r.ViewpointCharacterIds.Contains(profile.CharacterId))
                    .ToList();

                if (viewpointScenes.Count < 3)
                    continue;

                DetectContractionInconsistency(profile, viewpointScenes, notes);
                DetectDialogueRatioInconsistency(profile, viewpointScenes, notes);
            }

            // (b) Narrator voice drift within scenes
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

            if (stdDev < 0.01) return; // negligible variation

            foreach (var (scene, rate) in perSceneRates)
            {
                double z = Math.Abs(rate - avg) / stdDev;
                if (z > 2.0)
                {
                    notes.Add(new NlpNote
                    {
                        Severity = NlpNoteSeverity.Warning,
                        Category = NlpNoteCategory.Voice,
                        SceneId = scene.SceneId,
                        SceneTitle = scene.SceneTitle,
                        ChapterTitle = scene.ChapterTitle,
                        Message = $"{profile.CharacterName} uses contractions at {avg:P0} " +
                                  $"across their scenes, but '{scene.SceneTitle}' has a " +
                                  $"contraction rate of {rate:P0}. " +
                                  "Voice may be inconsistent."
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
                    notes.Add(new NlpNote
                    {
                        Severity = NlpNoteSeverity.Info,
                        Category = NlpNoteCategory.Voice,
                        SceneId = scene.SceneId,
                        SceneTitle = scene.SceneTitle,
                        ChapterTitle = scene.ChapterTitle,
                        Message = $"{profile.CharacterName}'s scenes average " +
                                  $"{avg:P0} dialogue, but '{scene.SceneTitle}' " +
                                  $"has {ratio:P0}. This may indicate a " +
                                  "shift in the character's typical scene composition."
                    });
                }
            }
        }

        // ── Narrator voice drift ─────────────────────────────────────────────

        private static void DetectNarratorDrift(List<SceneAnalysisResult> results, List<NlpNote> notes)
        {
            foreach (var scene in results)
            {
                if (scene.ViewpointCharacterIds.Count == 0)
                    continue;

                // Get non-dialogue sentences
                var narrationSentences = scene.Sentences
                    .Where(s => !NlpTextExtractor.IsDialogue(s.Text))
                    .ToList();

                if (narrationSentences.Count < 15) // Need enough for baseline + comparison
                    continue;

                // Establish baseline from first 10 non-dialogue sentences
                const int baselineSize = 10;
                var baseline = narrationSentences.Take(baselineSize).ToList();
                double baselineAvgLength = baseline.Average(s => s.WordCount);
                double baselineStdDev = baseline.Count > 1
                    ? Math.Sqrt(baseline.Sum(s => Math.Pow(s.WordCount - baselineAvgLength, 2))
                        / (baseline.Count - 1))
                    : 0;

                if (baselineStdDev < 1.0) baselineStdDev = 1.0; // minimum to avoid div-by-zero

                // Check subsequent sentences for drift
                for (int i = baselineSize; i < narrationSentences.Count; i++)
                {
                    var sentence = narrationSentences[i];
                    double lengthZ = Math.Abs(sentence.WordCount - baselineAvgLength) / baselineStdDev;

                    // Flag if both style AND embedding distance suggest drift
                    bool styleDrift = lengthZ > 3.0;
                    bool embeddingDrift = sentence.EmbeddingDistance > 0.4f;

                    if (styleDrift && embeddingDrift)
                    {
                        notes.Add(new NlpNote
                        {
                            Severity = NlpNoteSeverity.Info,
                            Category = NlpNoteCategory.Voice,
                            SceneId = scene.SceneId,
                            SceneTitle = scene.SceneTitle,
                            ChapterTitle = scene.ChapterTitle,
                            SentenceIndex = sentence.Index,
                            Message = $"Sentence {sentence.Index + 1} in '{scene.SceneTitle}' " +
                                      "doesn't match the narrator's established voice — " +
                                      "the sentence structure and topic shift significantly."
                        });
                    }
                }
            }
        }

        // ── Emotion distribution ─────────────────────────────────────────────

        private static Dictionary<EmotionCluster, double> ComputeEmotionDistribution(
            List<SentenceAnalysis> sentences)
        {
            var clusterSums = new Dictionary<EmotionCluster, double>();
            foreach (EmotionCluster cluster in Enum.GetValues<EmotionCluster>())
                clusterSums[cluster] = 0;

            double total = 0;

            foreach (var sentence in sentences)
            {
                foreach (var (label, confidence) in sentence.Emotions)
                {
                    var cluster = label.ToCluster();
                    clusterSums[cluster] += confidence;
                    total += confidence;
                }
            }

            // Normalize to proportions
            if (total > 0)
            {
                foreach (var cluster in clusterSums.Keys.ToList())
                    clusterSums[cluster] /= total;
            }

            return clusterSums;
        }

        // ── TF-IDF distinctive words ─────────────────────────────────────────

        private static List<(string Word, double TfIdf)> ComputeDistinctiveWords(
            List<string> characterSentences, List<SceneAnalysisResult> allResults)
        {
            // Build character word frequencies
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

            // Build per-scene "document" word sets for IDF
            var sceneWordSets = allResults
                .Select(r => new HashSet<string>(
                    r.Sentences.SelectMany(s => ExtractWords(s.Text))
                        .Select(w => w.ToLowerInvariant()),
                    StringComparer.OrdinalIgnoreCase))
                .ToList();

            int numDocuments = sceneWordSets.Count;
            if (numDocuments == 0) return [];

            // Compute TF-IDF
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

        private static IEnumerable<string> ExtractWords(string text)
        {
            return text.Split([' ', '\t', '\r', '\n', ',', '.', '!', '?', ';', ':', '"', '\'', '(', ')', '[', ']', '{', '}', '-', '\u2014', '\u2013'],
                    StringSplitOptions.RemoveEmptyEntries)
                .Where(w => w.Length > 1);
        }

        // Common English stop words
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
            "know", "knew", "think", "thought", "look", "looked", "say", "tell", "told"
        };
    }
}
