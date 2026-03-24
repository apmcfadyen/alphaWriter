using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using alphaWriter.Models;
using alphaWriter.Models.Analysis;

namespace alphaWriter.Services.Nlp
{
    public class NlpAnalysisService : INlpAnalysisService
    {
        private readonly IStyleAnalyzer _styleAnalyzer;
        private readonly IPacingAnalyzer _pacingAnalyzer;
        private readonly IEmbeddingService? _embeddingService;
        private readonly INlpModelManager? _modelManager;
        private readonly IEmotionService? _emotionService;
        private readonly ICharacterVoiceAnalyzer? _voiceAnalyzer;

        // Phase 1 constructor (rule-based only)
        public NlpAnalysisService(IStyleAnalyzer styleAnalyzer, IPacingAnalyzer pacingAnalyzer)
        {
            _styleAnalyzer = styleAnalyzer;
            _pacingAnalyzer = pacingAnalyzer;
        }

        // Phase 2 constructor (with embeddings)
        public NlpAnalysisService(IStyleAnalyzer styleAnalyzer, IPacingAnalyzer pacingAnalyzer,
            IEmbeddingService embeddingService, INlpModelManager modelManager)
        {
            _styleAnalyzer = styleAnalyzer;
            _pacingAnalyzer = pacingAnalyzer;
            _embeddingService = embeddingService;
            _modelManager = modelManager;
        }

        // Phase 3 constructor (with emotion + voice)
        public NlpAnalysisService(IStyleAnalyzer styleAnalyzer, IPacingAnalyzer pacingAnalyzer,
            IEmbeddingService embeddingService, INlpModelManager modelManager,
            IEmotionService emotionService, ICharacterVoiceAnalyzer voiceAnalyzer)
        {
            _styleAnalyzer = styleAnalyzer;
            _pacingAnalyzer = pacingAnalyzer;
            _embeddingService = embeddingService;
            _modelManager = modelManager;
            _emotionService = emotionService;
            _voiceAnalyzer = voiceAnalyzer;
        }

        private bool EmbeddingsAvailable =>
            _embeddingService is not null && _modelManager is not null
            && _modelManager.IsEmbeddingModelAvailable;

        private bool EmotionsAvailable =>
            _emotionService is not null && _modelManager is not null
            && _modelManager.IsEmotionModelAvailable;

        public Task<SceneAnalysisResult> AnalyzeSceneAsync(Scene scene, Chapter chapter, Book book,
            CancellationToken ct = default)
        {
            return Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();
                return AnalyzeScene(scene, chapter);
            }, ct);
        }

        public Task<List<SceneAnalysisResult>> AnalyzeChapterAsync(Chapter chapter, Book book,
            CancellationToken ct = default)
        {
            return Task.Run(async () =>
            {
                var results = new List<SceneAnalysisResult>();
                foreach (var scene in chapter.Scenes)
                {
                    ct.ThrowIfCancellationRequested();
                    if (scene.Status == SceneStatus.Outline) continue;
                    results.Add(AnalyzeScene(scene, chapter));
                }

                // Cross-scene style comparison within the chapter
                if (results.Count > 1)
                    AddChapterLevelNotes(results, chapter);

                // Embedding-based analysis
                if (results.Count > 1 && EmbeddingsAvailable)
                {
                    await EnsureEmbeddingsLoaded(ct);
                    AddEmbeddingBasedNotes(results, chapter, ct);
                }

                return results;
            }, ct);
        }

        public Task<(List<NlpNote> Notes, List<SceneAnalysisResult> Results)> AnalyzeBookAsync(Book book,
            IProgress<string>? progress = null, CancellationToken ct = default)
        {
            return Task.Run(async () =>
            {
                // Load embedding model if available
                if (EmbeddingsAvailable)
                {
                    progress?.Report("Loading embedding model...");
                    await EnsureEmbeddingsLoaded(ct);
                }

                // Load emotion model if available
                if (EmotionsAvailable)
                {
                    progress?.Report("Loading emotion model...");
                    await EnsureEmotionsLoaded(ct);
                }

                var allNotes = new List<NlpNote>();
                var allResults = new List<SceneAnalysisResult>();

                try
                {
                    foreach (var chapter in book.Chapters)
                    {
                        ct.ThrowIfCancellationRequested();
                        progress?.Report($"Analyzing '{chapter.Title}'...");

                        var chapterResults = new List<SceneAnalysisResult>();
                        foreach (var scene in chapter.Scenes)
                        {
                            ct.ThrowIfCancellationRequested();
                            if (scene.Status == SceneStatus.Outline) continue;
                            chapterResults.Add(AnalyzeScene(scene, chapter));
                        }

                        // Cross-scene style comparison
                        if (chapterResults.Count > 1)
                            AddChapterLevelNotes(chapterResults, chapter);

                        // Embedding-based scene outlier + topic drift
                        if (chapterResults.Count > 1 && _embeddingService is not null && _embeddingService.IsLoaded)
                        {
                            progress?.Report($"Computing embeddings for '{chapter.Title}'...");
                            AddEmbeddingBasedNotes(chapterResults, chapter, ct);
                        }

                        // Phase 3: Emotion classification + tone shift detection (batched)
                        if (_emotionService is not null && _emotionService.IsLoaded)
                        {
                            foreach (var result in chapterResults)
                            {
                                ct.ThrowIfCancellationRequested();
                                progress?.Report($"Classifying emotions in '{result.SceneTitle}'...");
                                ClassifyEmotionsBatched(result, ct);
                                result.Notes.AddRange(DetectToneShifts(result));
                            }
                        }

                        allNotes.AddRange(chapterResults.SelectMany(r => r.Notes));
                        allResults.AddRange(chapterResults);
                    }

                    // Book-level structural notes
                    AddBookLevelNotes(allNotes, book);

                    // Phase 4: Cross-book developmental notes
                    allNotes.AddRange(GenerateBookNotes(allResults, book));

                    // Phase 3: Character voice profiling + narrator drift
                    if (_voiceAnalyzer is not null && allResults.Count > 0)
                    {
                        progress?.Report("Analyzing character voices...");
                        var profiles = _voiceAnalyzer.BuildProfiles(allResults, book);
                        allNotes.AddRange(
                            _voiceAnalyzer.DetectVoiceAnomalies(profiles, allResults, book));
                    }

                    progress?.Report("Analysis complete.");
                }
                finally
                {
                    // Phase 5: Unload models after analysis to free ~500MB of memory.
                    // They will be lazily reloaded on the next analysis run.
                    _embeddingService?.UnloadModel();
                    _emotionService?.UnloadModel();
                }

                return (allNotes, allResults);
            }, ct);
        }

        /// <summary>
        /// Classifies emotions for all sentences in a scene using batched inference.
        /// Much faster than per-sentence calls for scenes with many sentences.
        /// </summary>
        private void ClassifyEmotionsBatched(SceneAnalysisResult result, CancellationToken ct)
        {
            if (_emotionService is null || !_emotionService.IsLoaded) return;
            if (result.Sentences.Count == 0) return;

            var texts = result.Sentences.Select(s => s.Text).ToList();

            ct.ThrowIfCancellationRequested();
            var batchResults = _emotionService.ClassifyBatch(texts);

            for (int i = 0; i < result.Sentences.Count && i < batchResults.Count; i++)
            {
                result.Sentences[i].Emotions = batchResults[i];
            }
        }

        private async Task EnsureEmbeddingsLoaded(CancellationToken ct)
        {
            if (_embeddingService is not null && !_embeddingService.IsLoaded)
                await _embeddingService.LoadModelAsync(ct);
        }

        private async Task EnsureEmotionsLoaded(CancellationToken ct)
        {
            if (_emotionService is not null && !_emotionService.IsLoaded)
                await _emotionService.LoadModelAsync(ct);
        }

        private SceneAnalysisResult AnalyzeScene(Scene scene, Chapter chapter)
        {
            var plainText = NlpTextExtractor.ExtractPlainText(scene.Content);
            var sentences = NlpTextExtractor.SplitSentences(plainText);
            var paragraphs = NlpTextExtractor.SplitParagraphs(plainText);

            // Compute chapter average word count for pacing comparison
            var nonOutlineScenes = chapter.Scenes
                .Where(s => s.Status != SceneStatus.Outline)
                .ToList();
            double chapterAvgWords = nonOutlineScenes.Count > 0
                ? nonOutlineScenes.Average(s => s.WordCount)
                : 0;

            var styleProfile = _styleAnalyzer.Analyze(sentences);
            var pacingMetrics = _pacingAnalyzer.Analyze(sentences, paragraphs,
                scene.WordCount, chapterAvgWords);

            var result = new SceneAnalysisResult
            {
                SceneId = scene.Id,
                SceneTitle = scene.Title,
                ChapterTitle = chapter.Title,
                Style = styleProfile,
                Pacing = pacingMetrics,
                ViewpointCharacterIds = scene.ViewpointCharacterIds?.ToList() ?? [],
                Sentences = sentences.Select((s, i) => new SentenceAnalysis
                {
                    Index = i,
                    Text = s,
                    WordCount = s.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries).Length
                }).ToList()
            };

            // Add pacing notes
            result.Notes.AddRange(
                _pacingAnalyzer.DetectIssues(pacingMetrics, scene.Id, scene.Title, chapter.Title));

            // Flag individual sentences that are extreme outliers in length
            if (sentences.Count > 3 && styleProfile.SentenceLengthStdDev > 0)
            {
                for (int i = 0; i < sentences.Count; i++)
                {
                    int wc = result.Sentences[i].WordCount;
                    double z = Math.Abs(wc - styleProfile.AverageSentenceLength) / styleProfile.SentenceLengthStdDev;
                    if (z > 2.5 && wc > 40)
                    {
                        result.Sentences[i].Flags.Add("unusually-long");
                        result.Notes.Add(new NlpNote
                        {
                            Severity = NlpNoteSeverity.Info,
                            Category = NlpNoteCategory.Pacing,
                            SceneId = scene.Id,
                            SceneTitle = scene.Title,
                            ChapterTitle = chapter.Title,
                            SentenceIndex = i,
                            Message = $"Sentence {i + 1} in '{scene.Title}' is {wc} words — " +
                                      $"the scene average is {styleProfile.AverageSentenceLength:F0}. " +
                                      "Consider breaking it up for readability."
                        });
                    }
                }
            }

            return result;
        }

        // ── Embedding-based analysis ─────────────────────────────────────────

        private void AddEmbeddingBasedNotes(List<SceneAnalysisResult> results, Chapter chapter,
            CancellationToken ct)
        {
            if (_embeddingService is null || !_embeddingService.IsLoaded) return;

            // Compute scene-level embeddings (mean of sentence embeddings)
            var sceneEmbeddings = new float[results.Count][];
            for (int i = 0; i < results.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var sentences = results[i].Sentences.Select(s => s.Text).ToList();
                if (sentences.Count == 0)
                {
                    sceneEmbeddings[i] = Array.Empty<float>();
                    continue;
                }

                var sentenceEmbeddings = _embeddingService.ComputeEmbeddings(sentences);

                // Store per-sentence embedding distances (for narrator drift detection)
                var sceneMean = MeanVector(sentenceEmbeddings);
                for (int j = 0; j < sentenceEmbeddings.Length; j++)
                {
                    results[i].Sentences[j].EmbeddingDistance =
                        1f - EmbeddingService.CosineSimilarityStatic(sentenceEmbeddings[j], sceneMean);
                }

                sceneEmbeddings[i] = sceneMean;
            }

            // Compute chapter centroid
            var validEmbeddings = sceneEmbeddings.Where(e => e.Length > 0).ToArray();
            if (validEmbeddings.Length < 2) return;

            var chapterCentroid = MeanVector(validEmbeddings);

            // Flag scene outliers (cosine similarity < 0.75 to chapter centroid)
            for (int i = 0; i < results.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                if (sceneEmbeddings[i].Length == 0) continue;

                float similarity = EmbeddingService.CosineSimilarityStatic(sceneEmbeddings[i], chapterCentroid);
                results[i].ChapterSimilarity = similarity;

                if (similarity < 0.75f)
                {
                    results[i].Notes.Add(new NlpNote
                    {
                        Severity = NlpNoteSeverity.Warning,
                        Category = NlpNoteCategory.Style,
                        SceneId = results[i].SceneId,
                        SceneTitle = results[i].SceneTitle,
                        ChapterTitle = chapter.Title,
                        Message = $"'{results[i].SceneTitle}' has a semantic similarity of {similarity:P0} " +
                                  $"to the rest of '{chapter.Title}' — it may feel stylistically out of place. " +
                                  "Consider whether it belongs in this chapter."
                    });
                }
            }

            // Topic drift detection within each scene
            for (int i = 0; i < results.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                DetectTopicDrift(results[i], chapter.Title);
            }
        }

        private void DetectTopicDrift(SceneAnalysisResult result, string chapterTitle)
        {
            if (_embeddingService is null || !_embeddingService.IsLoaded) return;

            var sentences = result.Sentences;
            if (sentences.Count < 10) return; // Need enough sentences for windowed comparison

            // Sliding window of 5 sentences — compare consecutive windows
            const int windowSize = 5;
            float[]? prevWindowEmbedding = null;

            for (int start = 0; start <= sentences.Count - windowSize; start += windowSize)
            {
                var windowTexts = sentences
                    .Skip(start)
                    .Take(windowSize)
                    .Select(s => s.Text)
                    .ToList();

                var windowEmbeddings = _embeddingService.ComputeEmbeddings(windowTexts);
                var windowMean = MeanVector(windowEmbeddings);

                if (prevWindowEmbedding is not null)
                {
                    float similarity = EmbeddingService.CosineSimilarityStatic(prevWindowEmbedding, windowMean);
                    if (similarity < 0.5f)
                    {
                        int sentenceIndex = start;
                        result.Sentences[sentenceIndex].Flags.Add("topic-drift");
                        result.Notes.Add(new NlpNote
                        {
                            Severity = NlpNoteSeverity.Info,
                            Category = NlpNoteCategory.Style,
                            SceneId = result.SceneId,
                            SceneTitle = result.SceneTitle,
                            ChapterTitle = chapterTitle,
                            SentenceIndex = sentenceIndex,
                            Message = $"Topic drift detected around sentence {sentenceIndex + 1} in " +
                                      $"'{result.SceneTitle}' (similarity: {similarity:P0}). " +
                                      "The subject matter shifts significantly here."
                        });
                    }
                }

                prevWindowEmbedding = windowMean;
            }
        }

        // ── Tone shift detection (Phase 3) ───────────────────────────────────

        internal static List<NlpNote> DetectToneShifts(SceneAnalysisResult result)
        {
            var notes = new List<NlpNote>();

            // Require enough sentences with emotion data
            var sentencesWithEmotions = result.Sentences
                .Where(s => s.Emotions.Count > 0)
                .ToList();

            if (sentencesWithEmotions.Count < 10)
                return notes;

            const int windowSize = 5;
            EmotionCluster? prevDominant = null;
            Dictionary<EmotionCluster, double>? prevWindowClusters = null;

            // Use non-overlapping windows to detect abrupt shifts (same approach as topic drift)
            for (int start = 0; start <= sentencesWithEmotions.Count - windowSize; start += windowSize)
            {
                var window = sentencesWithEmotions.Skip(start).Take(windowSize).ToList();
                var windowClusters = ComputeWindowEmotionProfile(window);

                var dominant = windowClusters
                    .OrderByDescending(kv => kv.Value)
                    .First().Key;

                if (prevDominant.HasValue && dominant != prevDominant.Value
                    && prevWindowClusters is not null)
                {
                    // Compute overlap: how much of the new window's emotion mass
                    // is in the old dominant cluster
                    double totalMass = windowClusters.Values.Sum();
                    double overlap = totalMass > 0
                        ? windowClusters.GetValueOrDefault(prevDominant.Value) / totalMass
                        : 0;

                    if (overlap < 0.30)
                    {
                        int sentenceIndex = sentencesWithEmotions[start].Index;
                        var severity = overlap < 0.15
                            ? NlpNoteSeverity.Warning
                            : NlpNoteSeverity.Info;

                        string qualifier = overlap < 0.15 ? "Dramatic emotional" : "Tone";

                        notes.Add(new NlpNote
                        {
                            Severity = severity,
                            Category = NlpNoteCategory.Emotion,
                            SceneId = result.SceneId,
                            SceneTitle = result.SceneTitle,
                            ChapterTitle = result.ChapterTitle,
                            SentenceIndex = sentenceIndex,
                            Message = $"{qualifier} shift from {prevDominant.Value} to {dominant} " +
                                      $"around sentence {sentenceIndex + 1} in '{result.SceneTitle}'. " +
                                      (overlap < 0.15
                                          ? "The transition may feel abrupt."
                                          : "Consider whether the transition feels earned.")
                        });

                        // Skip ahead to avoid duplicate notes for the same shift
                        start += windowSize - 1;
                        prevDominant = null;
                        prevWindowClusters = null;
                        continue;
                    }
                }

                prevDominant = dominant;
                prevWindowClusters = windowClusters;
            }

            return notes;
        }

        private static Dictionary<EmotionCluster, double> ComputeWindowEmotionProfile(
            List<SentenceAnalysis> window)
        {
            var clusters = new Dictionary<EmotionCluster, double>();
            foreach (EmotionCluster c in Enum.GetValues<EmotionCluster>())
                clusters[c] = 0;

            foreach (var sentence in window)
            {
                foreach (var (label, confidence) in sentence.Emotions)
                {
                    clusters[label.ToCluster()] += confidence;
                }
            }

            return clusters;
        }

        // ── Chapter and book level notes ─────────────────────────────────────

        private void AddChapterLevelNotes(List<SceneAnalysisResult> results, Chapter chapter)
        {
            // Build a chapter-wide style profile from all scene sentences
            var allSentences = results.SelectMany(r => r.Sentences.Select(s => s.Text)).ToList();
            var chapterProfile = _styleAnalyzer.Analyze(allSentences);

            foreach (var result in results)
            {
                result.Notes.AddRange(
                    _styleAnalyzer.DetectAnomalies(result.Style, chapterProfile,
                        result.SceneId, result.SceneTitle, chapter.Title));
            }
        }

        private void AddBookLevelNotes(List<NlpNote> allNotes, Book book)
        {
            // Flag chapters with very different scene counts
            var chapterSceneCounts = book.Chapters
                .Select(c => c.Scenes.Count(s => s.Status != SceneStatus.Outline))
                .Where(c => c > 0)
                .ToList();

            if (chapterSceneCounts.Count > 2)
            {
                double avg = chapterSceneCounts.Average();
                double stdDev = Math.Sqrt(chapterSceneCounts.Sum(c => Math.Pow(c - avg, 2))
                    / (chapterSceneCounts.Count - 1));

                if (stdDev > 0)
                {
                    foreach (var chapter in book.Chapters)
                    {
                        int count = chapter.Scenes.Count(s => s.Status != SceneStatus.Outline);
                        if (count == 0) continue;
                        double z = Math.Abs(count - avg) / stdDev;
                        if (z > 2.0)
                        {
                            string direction = count > avg ? "many more" : "far fewer";
                            allNotes.Add(new NlpNote
                            {
                                Severity = NlpNoteSeverity.Info,
                                Category = NlpNoteCategory.Structure,
                                SceneId = string.Empty,
                                SceneTitle = string.Empty,
                                ChapterTitle = chapter.Title,
                                Message = $"'{chapter.Title}' has {count} scenes — {direction} than " +
                                          $"the book average of {avg:F0}. This may indicate a structural imbalance."
                            });
                        }
                    }
                }
            }
        }

        internal static List<NlpNote> GenerateBookNotes(List<SceneAnalysisResult> allResults, Book book)
        {
            var notes = new List<NlpNote>();
            if (allResults.Count < 2) return notes;

            // ── Chapter word count imbalance ────────────────────────────────
            var chapterWordCounts = new Dictionary<string, int>();
            foreach (var r in allResults)
                chapterWordCounts[r.ChapterTitle] = chapterWordCounts.GetValueOrDefault(r.ChapterTitle) + r.Style.TotalWords;

            if (chapterWordCounts.Count > 2)
            {
                var counts = chapterWordCounts.Values.ToList();
                double avg = counts.Average();
                double stdDev = counts.Count > 1
                    ? Math.Sqrt(counts.Sum(c => Math.Pow(c - avg, 2)) / (counts.Count - 1))
                    : 0;

                if (stdDev > 0)
                {
                    foreach (var (chTitle, wc) in chapterWordCounts)
                    {
                        double z = Math.Abs(wc - avg) / stdDev;
                        if (z > 2.0)
                        {
                            string direction = wc > avg ? "significantly longer" : "significantly shorter";
                            notes.Add(new NlpNote
                            {
                                Severity = NlpNoteSeverity.Warning,
                                Category = NlpNoteCategory.Structure,
                                ChapterTitle = chTitle,
                                Message = $"'{chTitle}' is {wc:N0} words — {direction} than " +
                                          $"the book average of {avg:N0} words per chapter. " +
                                          "Consider whether this chapter needs rebalancing."
                            });
                        }
                    }
                }
            }

            // ── Dialogue density anomaly ────────────────────────────────────
            var dialogueRatios = allResults
                .Where(r => r.Style.TotalSentences > 5)
                .ToList();

            if (dialogueRatios.Count > 2)
            {
                double avgDialogue = dialogueRatios.Average(r => r.Style.DialogueRatio);
                double stdDev = Math.Sqrt(
                    dialogueRatios.Sum(r => Math.Pow(r.Style.DialogueRatio - avgDialogue, 2))
                    / (dialogueRatios.Count - 1));

                if (stdDev > 0)
                {
                    foreach (var r in dialogueRatios)
                    {
                        double z = (r.Style.DialogueRatio - avgDialogue) / stdDev;
                        if (Math.Abs(z) > 2.0)
                        {
                            string direction = z > 0 ? "much more dialogue" : "much less dialogue";
                            notes.Add(new NlpNote
                            {
                                Severity = NlpNoteSeverity.Info,
                                Category = NlpNoteCategory.Style,
                                SceneId = r.SceneId,
                                SceneTitle = r.SceneTitle,
                                ChapterTitle = r.ChapterTitle,
                                Message = $"'{r.SceneTitle}' has {direction} ({r.Style.DialogueRatio:P0}) " +
                                          $"than the book average ({avgDialogue:P0}). " +
                                          "This can affect pacing — consider whether the balance feels intentional."
                            });
                        }
                    }
                }
            }

            // ── Contraction rate inconsistency ──────────────────────────────
            var contractionScenes = allResults
                .Where(r => r.Style.TotalSentences > 5)
                .ToList();

            if (contractionScenes.Count > 2)
            {
                double avgContraction = contractionScenes.Average(r => r.Style.ContractionRate);
                double stdDev = Math.Sqrt(
                    contractionScenes.Sum(r => Math.Pow(r.Style.ContractionRate - avgContraction, 2))
                    / (contractionScenes.Count - 1));

                if (stdDev > 0)
                {
                    foreach (var r in contractionScenes)
                    {
                        double z = (r.Style.ContractionRate - avgContraction) / stdDev;
                        if (Math.Abs(z) > 2.0)
                        {
                            // Higher contraction rate = more casual, lower = more formal
                            string direction = z > 0 ? "more casual" : "more formal";
                            notes.Add(new NlpNote
                            {
                                Severity = NlpNoteSeverity.Info,
                                Category = NlpNoteCategory.Voice,
                                SceneId = r.SceneId,
                                SceneTitle = r.SceneTitle,
                                ChapterTitle = r.ChapterTitle,
                                Message = $"'{r.SceneTitle}' feels {direction} (contraction rate {r.Style.ContractionRate:P0}) " +
                                          $"than the book average ({avgContraction:P0}). " +
                                          "This may create a tonal inconsistency."
                            });
                        }
                    }
                }
            }

            // ── Scene opening/closing rhythm ────────────────────────────────
            foreach (var r in allResults)
            {
                if (r.Sentences.Count < 5) continue;

                var first = r.Sentences[0];
                var last = r.Sentences[^1];

                if (first.WordCount > 35 && r.Style.AverageSentenceLength < 20)
                {
                    notes.Add(new NlpNote
                    {
                        Severity = NlpNoteSeverity.Info,
                        Category = NlpNoteCategory.Pacing,
                        SceneId = r.SceneId,
                        SceneTitle = r.SceneTitle,
                        ChapterTitle = r.ChapterTitle,
                        SentenceIndex = 0,
                        Message = $"'{r.SceneTitle}' opens with a {first.WordCount}-word sentence " +
                                  $"(scene average: {r.Style.AverageSentenceLength:F0}). " +
                                  "A long opening sentence can slow a reader's entry into the scene."
                    });
                }

                if (last.WordCount > 35 && r.Style.AverageSentenceLength < 20)
                {
                    notes.Add(new NlpNote
                    {
                        Severity = NlpNoteSeverity.Info,
                        Category = NlpNoteCategory.Pacing,
                        SceneId = r.SceneId,
                        SceneTitle = r.SceneTitle,
                        ChapterTitle = r.ChapterTitle,
                        SentenceIndex = r.Sentences.Count - 1,
                        Message = $"'{r.SceneTitle}' closes with a {last.WordCount}-word sentence. " +
                                  "Shorter closing sentences often create stronger scene endings."
                    });
                }
            }

            return notes;
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        private static float[] MeanVector(float[][] vectors)
        {
            if (vectors.Length == 0) return [];
            int dim = vectors[0].Length;
            var mean = new float[dim];
            foreach (var v in vectors)
                for (int d = 0; d < dim; d++)
                    mean[d] += v[d];
            for (int d = 0; d < dim; d++)
                mean[d] /= vectors.Length;
            return mean;
        }
    }
}
