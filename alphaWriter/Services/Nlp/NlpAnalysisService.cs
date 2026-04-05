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
        private readonly IPosTaggingService? _posTaggingService;

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

        // Phase 6 constructor (+ POS tagging for passive voice detection)
        public NlpAnalysisService(IStyleAnalyzer styleAnalyzer, IPacingAnalyzer pacingAnalyzer,
            IEmbeddingService embeddingService, INlpModelManager modelManager,
            IEmotionService emotionService, ICharacterVoiceAnalyzer voiceAnalyzer,
            IPosTaggingService posTaggingService)
        {
            _styleAnalyzer = styleAnalyzer;
            _pacingAnalyzer = pacingAnalyzer;
            _embeddingService = embeddingService;
            _modelManager = modelManager;
            _emotionService = emotionService;
            _voiceAnalyzer = voiceAnalyzer;
            _posTaggingService = posTaggingService;
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

        public Task<(List<NlpNote> Notes, List<SceneAnalysisResult> Results, List<CharacterVoiceProfile> VoiceProfiles, List<DialogueVoiceProfile> DialogueProfiles)> AnalyzeBookAsync(Book book,
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

                // Load POS tagger if available (downloads ~5–10 MB on first run)
                if (_posTaggingService is not null)
                {
                    try
                    {
                        progress?.Report("Loading POS tagger...");
                        await _posTaggingService.LoadAsync(ct);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        // Download failed, no internet, or initialization error.
                        // Report the reason so it's visible in the UI, then continue
                        // analysis without passive voice detection.
                        var reason = ex.InnerException?.Message ?? ex.Message;
                        progress?.Report(
                            $"POS tagger unavailable — passive voice detection skipped. " +
                            $"({ex.GetType().Name}: {reason.Split('\n')[0]})");
                    }
                }

                var allNotes = new List<NlpNote>();
                var allResults = new List<SceneAnalysisResult>();
                var voiceProfiles = new List<CharacterVoiceProfile>();
                var dialogueProfiles = new List<DialogueVoiceProfile>();

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

                        // Phase 6: Prose quality — rule-based (always) + POS-based (when POS loaded)
                        foreach (var result in chapterResults)
                        {
                            ct.ThrowIfCancellationRequested();
                            var sentences = result.Sentences.Select(s => s.Text).ToList();

                            result.Notes.AddRange(_styleAnalyzer.DetectAdverbDensity(
                                sentences, result.SceneId, result.SceneTitle, result.ChapterTitle));

                            result.Notes.AddRange(_styleAnalyzer.DetectShowDontTell(
                                sentences, result.SceneId, result.SceneTitle, result.ChapterTitle));

                            result.Notes.AddRange(_styleAnalyzer.DetectProximityEchoes(
                                sentences, result.SceneId, result.SceneTitle, result.ChapterTitle));

                            result.Notes.AddRange(_styleAnalyzer.DetectSubordinateClauseDensity(
                                sentences, result.SceneId, result.SceneTitle, result.ChapterTitle));

                            if (_posTaggingService is not null && _posTaggingService.IsLoaded)
                            {
                                ct.ThrowIfCancellationRequested();
                                progress?.Report($"Detecting passive voice in '{result.SceneTitle}'...");
                                result.Notes.AddRange(_styleAnalyzer.DetectPassiveVoice(
                                    sentences, _posTaggingService,
                                    result.SceneId, result.SceneTitle, result.ChapterTitle));

                                result.Notes.AddRange(_styleAnalyzer.DetectSentenceOpenerMonotony(
                                    sentences, _posTaggingService,
                                    result.SceneId, result.SceneTitle, result.ChapterTitle));
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
                        var posService = _posTaggingService?.IsLoaded == true ? _posTaggingService : null;
                        voiceProfiles = _voiceAnalyzer.BuildProfiles(allResults, book, posService);
                        allNotes.AddRange(
                            _voiceAnalyzer.DetectVoiceAnomalies(voiceProfiles, allResults, book));

                        progress?.Report("Analyzing dialogue voices...");
                        dialogueProfiles = _voiceAnalyzer.BuildDialogueProfiles(allResults, book, posService);
                        allNotes.AddRange(
                            _voiceAnalyzer.DetectDialogueVoiceAnomalies(dialogueProfiles));
                    }

                    progress?.Report("Analysis complete.");
                }
                finally
                {
                    // Phase 5/6: Unload all models after analysis to free memory.
                    // They will be lazily reloaded on the next analysis run.
                    _embeddingService?.UnloadModel();
                    _emotionService?.UnloadModel();
                    _posTaggingService?.UnloadModel();
                }

                return (allNotes, allResults, voiceProfiles, dialogueProfiles);
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
                            Category = NlpNoteCategory.DevelopmentalEditor,
                            SceneId = scene.Id,
                            SceneTitle = scene.Title,
                            ChapterTitle = chapter.Title,
                            SentenceIndex = i,
                            Message = $"Sentence {i + 1} runs {wc} words while the scene averages {styleProfile.AverageSentenceLength:F0} — " +
                                      "readers may lose the thread before the payoff arrives. " +
                                      "Break at the natural clause boundary, letting each idea land separately."
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
                        Category = NlpNoteCategory.DevelopmentalEditor,
                        SceneId = results[i].SceneId,
                        SceneTitle = results[i].SceneTitle,
                        ChapterTitle = chapter.Title,
                        Message = $"'{results[i].SceneTitle}' is only {similarity:P0} semantically similar to the rest of '{chapter.Title}' — " +
                                  "its subjects and language feel distinctly different from the surrounding scenes. " +
                                  "This could be an intentional tonal break, but if unexpected, it may need better connective tissue."
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
                            Category = NlpNoteCategory.DevelopmentalEditor,
                            SceneId = result.SceneId,
                            SceneTitle = result.SceneTitle,
                            ChapterTitle = chapterTitle,
                            SentenceIndex = sentenceIndex,
                            Message = $"The scene's focus shifts noticeably around sentence {sentenceIndex + 1} in '{result.SceneTitle}' " +
                                      $"(window similarity: {similarity:P0}). This isn't necessarily a problem — " +
                                      "but if the shift feels unearned, a brief transitional beat would help."
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
            HashSet<EmotionLabel>? prevTop3 = null;
            EmotionLabel? prevDominantLabel = null;

            // Use non-overlapping windows to detect abrupt shifts
            for (int start = 0; start <= sentencesWithEmotions.Count - windowSize; start += windowSize)
            {
                var window = sentencesWithEmotions.Skip(start).Take(windowSize).ToList();
                var windowSums = ComputeWindowEmotionProfile(window);

                // Top-3 labels by confidence sum
                var top3 = windowSums
                    .OrderByDescending(kv => kv.Value)
                    .Take(3)
                    .Select(kv => kv.Key)
                    .ToHashSet();

                // Dominant = single highest-confidence label
                var dominant = windowSums
                    .OrderByDescending(kv => kv.Value)
                    .First().Key;

                if (prevTop3 is not null && prevDominantLabel.HasValue && !prevTop3.Contains(dominant))
                {
                    // Overlap: fraction of new window's total mass on labels in previous top-3
                    double totalMass = windowSums.Values.Sum();
                    double overlap = totalMass > 0
                        ? windowSums
                            .Where(kv => prevTop3.Contains(kv.Key))
                            .Sum(kv => kv.Value) / totalMass
                        : 0;

                    if (overlap < 0.30)
                    {
                        int sentenceIndex = sentencesWithEmotions[start].Index;
                        var severity = overlap < 0.15
                            ? NlpNoteSeverity.Warning
                            : NlpNoteSeverity.Info;

                        string qualifier = overlap < 0.15 ? "Dramatic emotional" : "Tone";

                        string toneMessage = overlap < 0.15
                            ? $"The emotional register shifts sharply — from {prevDominantLabel.Value} to {dominant} — " +
                              $"around sentence {sentenceIndex + 1} in '{result.SceneTitle}'. " +
                              "The transition feels abrupt; a brief bridge moment could make the shift feel intentional."
                            : $"The mood shifts from {prevDominantLabel.Value} to {dominant} around sentence {sentenceIndex + 1} " +
                              $"in '{result.SceneTitle}'. This may be a deliberate turn — " +
                              "read it against the previous scene to check whether the shift lands.";

                        notes.Add(new NlpNote
                        {
                            Severity = severity,
                            Category = NlpNoteCategory.LineEditor,
                            SceneId = result.SceneId,
                            SceneTitle = result.SceneTitle,
                            ChapterTitle = result.ChapterTitle,
                            SentenceIndex = sentenceIndex,
                            Message = toneMessage
                        });

                        // Skip ahead to avoid duplicate notes for the same shift
                        start += windowSize - 1;
                        prevTop3 = null;
                        prevDominantLabel = null;
                        continue;
                    }
                }

                prevTop3 = top3;
                prevDominantLabel = dominant;
            }

            return notes;
        }

        private static Dictionary<EmotionLabel, double> ComputeWindowEmotionProfile(
            List<SentenceAnalysis> window)
        {
            var sums = new Dictionary<EmotionLabel, double>();

            foreach (var sentence in window)
            {
                foreach (var (label, confidence) in sentence.Emotions)
                {
                    sums[label] = sums.GetValueOrDefault(label) + confidence;
                }
            }

            return sums;
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
                                Category = NlpNoteCategory.DevelopmentalEditor,
                                SceneId = string.Empty,
                                SceneTitle = string.Empty,
                                ChapterTitle = chapter.Title,
                                Message = $"'{chapter.Title}' has {count} scenes against a book average of {avg:F0} — " +
                                          $"{direction} than most chapters. " +
                                          "A disparity this large can affect how readers experience the chapter's pacing " +
                                          "relative to the rest of the book."
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
                                Category = NlpNoteCategory.DevelopmentalEditor,
                                ChapterTitle = chTitle,
                                Message = $"'{chTitle}' is {wc:N0} words — {direction} than the book average of {avg:N0}. " +
                                          "A chapter this far outside the norm disrupts the implicit pacing contract with readers. " +
                                          "Consider trimming, or splitting it into two shorter chapters."
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
                            string dlgDirection = z > 0 ? "unusually dialogue-heavy" : "unusually narration-heavy";
                            notes.Add(new NlpNote
                            {
                                Severity = NlpNoteSeverity.Info,
                                Category = NlpNoteCategory.DevelopmentalEditor,
                                SceneId = r.SceneId,
                                SceneTitle = r.SceneTitle,
                                ChapterTitle = r.ChapterTitle,
                                Message = $"'{r.SceneTitle}' is {dlgDirection} — {r.Style.DialogueRatio:P0} dialogue " +
                                          $"vs the book average of {avgDialogue:P0}. " +
                                          "This imbalance can feel jarring if it isn't marking a structural pivot."
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
                            string ctrDirection = z > 0 ? "more casual" : "more formal";
                            notes.Add(new NlpNote
                            {
                                Severity = NlpNoteSeverity.Info,
                                Category = NlpNoteCategory.LineEditor,
                                SceneId = r.SceneId,
                                SceneTitle = r.SceneTitle,
                                ChapterTitle = r.ChapterTitle,
                                Message = $"'{r.SceneTitle}' sounds {ctrDirection} — contraction rate {r.Style.ContractionRate:P0} " +
                                          $"vs the book average of {avgContraction:P0}. " +
                                          "The register shift may be deliberate (a formal ceremony, a tense confrontation) — " +
                                          "if not, it can quietly erode the book's tonal consistency."
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
                        Category = NlpNoteCategory.DevelopmentalEditor,
                        SceneId = r.SceneId,
                        SceneTitle = r.SceneTitle,
                        ChapterTitle = r.ChapterTitle,
                        SentenceIndex = 0,
                        Message = $"'{r.SceneTitle}' opens with a {first.WordCount}-word sentence against a scene average of {r.Style.AverageSentenceLength:F0} — " +
                                  "readers are still orienting themselves and may stumble on a complex opener. " +
                                  "A shorter hook pulls them in faster."
                    });
                }

                if (last.WordCount > 35 && r.Style.AverageSentenceLength < 20)
                {
                    notes.Add(new NlpNote
                    {
                        Severity = NlpNoteSeverity.Info,
                        Category = NlpNoteCategory.DevelopmentalEditor,
                        SceneId = r.SceneId,
                        SceneTitle = r.SceneTitle,
                        ChapterTitle = r.ChapterTitle,
                        SentenceIndex = r.Sentences.Count - 1,
                        Message = $"'{r.SceneTitle}' closes with a {last.WordCount}-word sentence — the final beat carries extra weight, " +
                                  "and a long ending can dissipate it. A tighter closing sentence tends to land harder."
                    });
                }
            }

            // ── Formality trajectory ────────────────────────────────────────
            // Compare average contraction rate in the first vs second half of the book
            var chapterOrder = allResults
                .GroupBy(r => r.ChapterTitle)
                .Select(g => new { Chapter = g.Key, AvgContraction = g.Average(r => r.Style.ContractionRate) })
                .ToList();

            if (chapterOrder.Count >= 4)
            {
                int mid = chapterOrder.Count / 2;
                double firstHalfContraction = chapterOrder.Take(mid).Average(c => c.AvgContraction);
                double secondHalfContraction = chapterOrder.Skip(mid).Average(c => c.AvgContraction);
                double shift = secondHalfContraction - firstHalfContraction;

                if (Math.Abs(shift) > 0.06)
                {
                    string direction = shift < 0 ? "more formal" : "more casual";
                    notes.Add(new NlpNote
                    {
                        Severity = NlpNoteSeverity.Warning,
                        Category = NlpNoteCategory.LineEditor,
                        Message = $"The book's prose grows {direction} across the second half — " +
                                  $"contraction rate shifts from {firstHalfContraction:P0} in early chapters " +
                                  $"to {secondHalfContraction:P0} later. " +
                                  "A gradual register shift can signal natural character development; " +
                                  "an abrupt one may indicate unintentional voice drift."
                    });
                }
            }

            // ── Vocabulary plateau ──────────────────────────────────────────
            // Check whether new vocabulary stops appearing in the second half
            if (allResults.Count >= 6)
            {
                int mid = allResults.Count / 2;
                var firstHalfWords = allResults.Take(mid)
                    .SelectMany(r => r.Sentences.Select(s => s.Text))
                    .SelectMany(s => s.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries))
                    .Select(w => w.ToLowerInvariant().Trim('.', ',', '!', '?'))
                    .Where(w => w.Length > 3)
                    .ToHashSet();

                var secondHalfWords = allResults.Skip(mid)
                    .SelectMany(r => r.Sentences.Select(s => s.Text))
                    .SelectMany(s => s.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries))
                    .Select(w => w.ToLowerInvariant().Trim('.', ',', '!', '?'))
                    .Where(w => w.Length > 3)
                    .ToList();

                if (secondHalfWords.Count > 0)
                {
                    int newWords = secondHalfWords.Distinct().Count(w => !firstHalfWords.Contains(w));
                    double noveltyRate = (double)newWords / secondHalfWords.Distinct().Count();

                    if (noveltyRate < 0.10)
                    {
                        notes.Add(new NlpNote
                        {
                            Severity = NlpNoteSeverity.Info,
                            Category = NlpNoteCategory.DevelopmentalEditor,
                            Message = $"The second half of the book introduces very little new vocabulary ({noveltyRate:P0} of words are new) — " +
                                      "the prose may start to feel repetitive as readers move through the back half. " +
                                      "Varying sentence structures and word choices can maintain freshness."
                        });
                    }
                }
            }

            // ── Act-level pacing imbalance ──────────────────────────────────
            // Compare average scene word count across three acts (chapter thirds)
            var chapters = allResults.GroupBy(r => r.ChapterTitle).ToList();
            if (chapters.Count >= 6)
            {
                int actSize = chapters.Count / 3;
                double act1Avg = chapters.Take(actSize)
                    .SelectMany(g => g).Average(r => r.Style.TotalWords);
                double act2Avg = chapters.Skip(actSize).Take(actSize)
                    .SelectMany(g => g).Average(r => r.Style.TotalWords);

                if (act2Avg > act1Avg * 1.5 && act1Avg > 0)
                {
                    notes.Add(new NlpNote
                    {
                        Severity = NlpNoteSeverity.Warning,
                        Category = NlpNoteCategory.DevelopmentalEditor,
                        Message = $"Your middle act scenes average {act2Avg:N0} words vs {act1Avg:N0} in Act 1 — " +
                                  "the pacing slows significantly in the second third of the book. " +
                                  "Consider tightening middle act scenes or subdividing longer ones."
                    });
                }
            }

            // ── Emotional flatline ──────────────────────────────────────────
            // Flag chapters where emotion data shows <3 distinct dominant emotions
            var chapterEmotions = allResults
                .Where(r => r.Sentences.Any(s => s.Emotions.Count > 0))
                .GroupBy(r => r.ChapterTitle);

            foreach (var chapterGroup in chapterEmotions)
            {
                var dominantEmotions = chapterGroup
                    .Select(r => r.Sentences
                        .Where(s => s.Emotions.Count > 0)
                        .SelectMany(s => s.Emotions)
                        .GroupBy(e => e.Label)
                        .OrderByDescending(g => g.Sum(e => e.Confidence))
                        .FirstOrDefault()?.Key)
                    .Where(e => e.HasValue)
                    .Select(e => e!.Value)
                    .Distinct()
                    .ToList();

                if (dominantEmotions.Count > 0 && dominantEmotions.Count < 3)
                {
                    string emotions = string.Join(", ", dominantEmotions);
                    notes.Add(new NlpNote
                    {
                        Severity = NlpNoteSeverity.Info,
                        Category = NlpNoteCategory.DevelopmentalEditor,
                        ChapterTitle = chapterGroup.Key,
                        Message = $"'{chapterGroup.Key}' runs almost entirely in {emotions} — " +
                                  "fewer than 3 distinct emotional tones across all scenes. " +
                                  "Even a single moment of contrasting intensity would give readers an emotional beat to anchor to."
                    });
                }
            }

            // ── Dialogue desert ─────────────────────────────────────────────
            // Flag runs of 4+ consecutive scenes with < 5% dialogue
            int desertStart = -1;
            int desertCount = 0;
            for (int i = 0; i < allResults.Count; i++)
            {
                bool isDesert = allResults[i].Style.DialogueRatio < 0.05
                    && allResults[i].Style.TotalSentences >= 5;
                if (isDesert)
                {
                    if (desertStart < 0) desertStart = i;
                    desertCount++;
                }
                else
                {
                    if (desertCount >= 4)
                    {
                        var first = allResults[desertStart];
                        var last = allResults[i - 1];
                        notes.Add(new NlpNote
                        {
                            Severity = NlpNoteSeverity.Warning,
                            Category = NlpNoteCategory.DevelopmentalEditor,
                            SceneId = first.SceneId,
                            SceneTitle = first.SceneTitle,
                            ChapterTitle = first.ChapterTitle,
                            Message = $"'{first.SceneTitle}' through '{last.SceneTitle}' — {desertCount} consecutive scenes " +
                                      "contain almost no dialogue. Pure narration at this length can feel airless; " +
                                      "even a brief exchange grounds readers and resets the rhythm."
                        });
                    }
                    desertStart = -1;
                    desertCount = 0;
                }
            }
            // Catch a run that extends to the end
            if (desertCount >= 4)
            {
                var first = allResults[desertStart];
                notes.Add(new NlpNote
                {
                    Severity = NlpNoteSeverity.Warning,
                    Category = NlpNoteCategory.DevelopmentalEditor,
                    SceneId = first.SceneId,
                    SceneTitle = first.SceneTitle,
                    ChapterTitle = first.ChapterTitle,
                    Message = $"The book ends with {desertCount} consecutive scenes containing almost no dialogue — " +
                              "pure narration this sustained can feel remote. " +
                              "Even a brief exchange grounds readers before the final beat."
                });
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
