using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using alphaWriter.Models;
using alphaWriter.Models.Analysis;
using alphaWriter.Services.Nlp.Parsing;
using alphaWriter.Services.Nlp.Rules;

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

        public ProseRuleEngine Engine { get; } = ProseRuleEngine.CreateDefault();

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

        // Live (synchronous) analysis reads SensoryLexicon.Default, so start loading it early
        static NlpAnalysisService() => _ = SensoryLexicon.LoadDefaultAsync();

        private bool EmbeddingsAvailable =>
            _embeddingService is not null && _modelManager is not null
            && _modelManager.IsEmbeddingModelAvailable;

        private bool EmotionsAvailable =>
            _emotionService is not null && _modelManager is not null
            && _modelManager.IsEmotionModelAvailable;

        private IPosTaggingService? LoadedPos =>
            _posTaggingService?.IsLoaded == true ? _posTaggingService : null;

        public Task<SceneAnalysisResult> AnalyzeSceneAsync(Scene scene, Chapter chapter, Book book,
            CancellationToken ct = default)
        {
            return Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();
                return AnalyzeScene(scene, chapter, book, out _);
            }, ct);
        }

        public LiveSceneAnalysis AnalyzeSceneLive(Scene scene, Chapter chapter, Book book)
        {
            var result = AnalyzeScene(scene, chapter, book, out var doc);
            var tiers = new List<RuleTier> { RuleTier.Lexical };
            if (doc.HasPos) tiers.Add(RuleTier.Pos);
            return new LiveSceneAnalysis(doc, result, Engine.SceneRuleIds(tiers));
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
                    results.Add(AnalyzeScene(scene, chapter, book, out _));
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
            IProgress<string>? progress = null, CancellationToken ct = default,
            IReadOnlyList<SceneAnalysisResult>? previousResults = null)
        {
            return Task.Run(async () =>
            {
                await SensoryLexicon.LoadDefaultAsync();

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
                        // analysis without POS-tier rules.
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

                var previous = previousResults?
                    .GroupBy(r => r.SceneId)
                    .ToDictionary(g => g.Key, g => g.First())
                    ?? new Dictionary<string, SceneAnalysisResult>();
                int reused = 0;

                try
                {
                    foreach (var chapter in book.Chapters)
                    {
                        ct.ThrowIfCancellationRequested();
                        progress?.Report($"Analyzing '{chapter.Title}'...");

                        double chapterAvgWords = ChapterAverageWords(chapter);
                        var chapterResults = new List<SceneAnalysisResult>();
                        var needsEmotions = new List<SceneAnalysisResult>();

                        foreach (var scene in chapter.Scenes)
                        {
                            ct.ThrowIfCancellationRequested();
                            if (scene.Status == SceneStatus.Outline) continue;

                            if (TryReuse(scene, chapter, chapterAvgWords, previous, out var cached))
                            {
                                reused++;
                                chapterResults.Add(cached);
                                if (!HasEmotionData(cached)) needsEmotions.Add(cached);
                            }
                            else
                            {
                                var fresh = AnalyzeScene(scene, chapter, book, out _);
                                chapterResults.Add(fresh);
                                needsEmotions.Add(fresh);
                            }
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

                        // Emotion classification + tone shift detection (batched).
                        // Scenes reused with their emotion data keep their tone-shift notes.
                        if (_emotionService is not null && _emotionService.IsLoaded)
                        {
                            foreach (var result in needsEmotions)
                            {
                                ct.ThrowIfCancellationRequested();
                                progress?.Report($"Classifying emotions in '{result.SceneTitle}'...");
                                ClassifyEmotionsBatched(result, ct);
                                result.Notes.RemoveAll(n => n.RuleId == RuleCatalog.ToneShift.Id);
                                result.Notes.AddRange(DetectToneShifts(result));
                            }
                        }

                        allNotes.AddRange(chapterResults.SelectMany(r => r.Notes));
                        allResults.AddRange(chapterResults);
                    }

                    // Book-level structural notes
                    var reference = await ReferenceFrequencies.LoadDefaultAsync();
                    allNotes.AddRange(Engine.EvaluateBook(new BookRuleContext
                    {
                        Results = allResults,
                        Book = book,
                        StyleSheet = book.StyleSheet ?? new EditorialStyleSheet(),
                        Reference = reference
                    }));

                    // Character voice profiling + narrator drift
                    if (_voiceAnalyzer is not null && allResults.Count > 0)
                    {
                        progress?.Report("Analyzing character voices...");
                        var posService = LoadedPos;
                        voiceProfiles = _voiceAnalyzer.BuildProfiles(allResults, book, posService);
                        allNotes.AddRange(WithRule(RuleCatalog.CharacterVoice,
                            _voiceAnalyzer.DetectVoiceAnomalies(voiceProfiles, allResults, book)));

                        progress?.Report("Analyzing dialogue voices...");
                        dialogueProfiles = _voiceAnalyzer.BuildDialogueProfiles(allResults, book, posService);
                        allNotes.AddRange(WithRule(RuleCatalog.DialogueVoice,
                            _voiceAnalyzer.DetectDialogueVoiceAnomalies(dialogueProfiles)));
                    }

                    // Anchor and fingerprint notes produced outside the rule engine
                    NoteAnchoring.AnchorToResults(allNotes, allResults);

                    progress?.Report(reused > 0
                        ? $"Analysis complete ({reused} unchanged scene{(reused == 1 ? "" : "s")} reused)."
                        : "Analysis complete.");
                }
                finally
                {
                    // Unload all models after analysis to free memory.
                    // They will be lazily reloaded on the next analysis run.
                    _embeddingService?.UnloadModel();
                    _emotionService?.UnloadModel();
                    _posTaggingService?.UnloadModel();
                }

                return (allNotes, allResults, voiceProfiles, dialogueProfiles);
            }, ct);
        }

        private static IEnumerable<NlpNote> WithRule(RuleDescriptor rule, IEnumerable<NlpNote> notes)
        {
            foreach (var note in notes)
            {
                if (string.IsNullOrEmpty(note.RuleId)) note.RuleId = rule.Id;
                yield return note;
            }
        }

        // ── Incremental reuse ────────────────────────────────────────────────

        /// <summary>
        /// Reuses a previous scene result when the scene's content, title and chapter are
        /// unchanged and it was produced by the current engine with at least the same
        /// capabilities. Only scene-scoped notes are kept; chapter and book comparisons
        /// are always recomputed because they depend on other scenes.
        /// </summary>
        private bool TryReuse(Scene scene, Chapter chapter, double chapterAvgWords,
            Dictionary<string, SceneAnalysisResult> previous, out SceneAnalysisResult result)
        {
            result = null!;
            if (!previous.TryGetValue(scene.Id, out var prev)) return false;
            if (prev.EngineVersion != RuleCatalog.EngineVersion) return false;
            if (prev.ContentHash != SceneHash(scene)) return false;
            if (prev.SceneTitle != scene.Title || prev.ChapterTitle != chapter.Title) return false;
            if (LoadedPos is not null && !prev.HadPos) return false;

            var keep = Engine.SceneRuleIds([RuleTier.Lexical, RuleTier.Pos]);
            keep.Remove(RuleCatalog.SceneLength.Id); // depends on the chapter's other scenes
            bool keepEmotions = HasEmotionData(prev);
            if (keepEmotions) keep.Add(RuleCatalog.ToneShift.Id);

            prev.Notes = prev.Notes.Where(n => keep.Contains(n.RuleId)).ToList();
            prev.ViewpointCharacterIds = scene.ViewpointCharacterIds?.ToList() ?? [];

            // Re-derive the chapter-relative scene length note
            prev.Pacing.ChapterAverageWordCount = chapterAvgWords;
            prev.Pacing.SceneToChapterRatio = chapterAvgWords > 0 ? prev.Pacing.SceneWordCount / chapterAvgWords : 0;
            foreach (var note in SceneLengthRule.CreateNotes(prev.Pacing, scene.Title))
            {
                note.SceneId = scene.Id;
                note.SceneTitle = scene.Title;
                note.ChapterTitle = chapter.Title;
                NoteAnchoring.EnsureFingerprint(note);
                prev.Notes.Add(note);
            }

            result = prev;
            return true;
        }

        /// <summary>
        /// Everything scene rules read: the text plus the scene's cast and viewpoint tags
        /// (presence rules compare the text against the tags).
        /// </summary>
        private static string SceneHash(Scene scene) =>
            NlpCacheService.ComputeHash(string.Join("",
                scene.Content ?? string.Empty,
                string.Join(",", scene.CharacterIds ?? []),
                string.Join(",", scene.ViewpointCharacterIds ?? [])));

        private static bool HasEmotionData(SceneAnalysisResult r) =>
            r.Sentences.Any(s => s.Emotions.Any(e => e.Confidence > 0));

        private static double ChapterAverageWords(Chapter chapter)
        {
            var scenes = chapter.Scenes.Where(s => s.Status != SceneStatus.Outline).ToList();
            return scenes.Count > 0 ? scenes.Average(s => s.WordCount) : 0;
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

        /// <summary>
        /// Analyzes one scene: builds its document once (with POS tags when the tagger is
        /// loaded), computes style and pacing profiles, and runs every available scene rule.
        /// </summary>
        private SceneAnalysisResult AnalyzeScene(Scene scene, Chapter chapter, Book? book,
            out SceneDocument doc)
        {
            var document = SceneDocumentBuilder.FromHtml(scene.Content ?? string.Empty, LoadedPos);
            doc = document;

            var sentenceTexts = document.Sentences.Select(s => s.Text).ToList();
            var paragraphTexts = document.Paragraphs
                .Select(p => document.Text.Substring(p.Start, p.Length))
                .ToList();

            var styleProfile = _styleAnalyzer.Analyze(sentenceTexts);
            var pacingMetrics = _pacingAnalyzer.Analyze(sentenceTexts, paragraphTexts,
                scene.WordCount, ChapterAverageWords(chapter));

            var result = new SceneAnalysisResult
            {
                SceneId = scene.Id,
                SceneTitle = scene.Title,
                ChapterTitle = chapter.Title,
                Style = styleProfile,
                Pacing = pacingMetrics,
                ViewpointCharacterIds = scene.ViewpointCharacterIds?.ToList() ?? [],
                ContentHash = SceneHash(scene),
                EngineVersion = RuleCatalog.EngineVersion,
                HadPos = document.HasPos,
                Sentences = document.Sentences.Select(s => new SentenceAnalysis
                {
                    Index = s.Index,
                    Text = s.Text,
                    WordCount = s.WordCount,
                    Start = s.Start,
                    ParagraphIndex = s.ParagraphIndex
                }).ToList()
            };

            var ctx = new SceneRuleContext
            {
                Document = document,
                SceneId = scene.Id,
                SceneTitle = scene.Title,
                ChapterTitle = chapter.Title,
                Book = book,
                StyleSheet = book?.StyleSheet ?? new EditorialStyleSheet(),
                ViewpointCharacterIds = result.ViewpointCharacterIds,
                TaggedCharacterIds = scene.CharacterIds?.ToList() ?? [],
                Style = styleProfile,
                Pacing = pacingMetrics,
                Senses = SensoryLexicon.Default
            };
            result.Notes.AddRange(Engine.EvaluateScene(ctx));
            if (SensoryLexicon.Default is { } senses)
                result.Senses = SensoryAnalyzer.Profile(document, senses);
            var beats = BeatClassifier.Classify(document);
            result.ParagraphBeats = beats.Select(b => b.ToString()).ToList();
            result.Tension = TensionAnalyzer.Signals(document, beats);

            foreach (var note in result.Notes)
            {
                if (note.RuleId == RuleCatalog.LongSentence.Id && note.SentenceIndex is int i)
                    result.Sentences[i].Flags.Add("unusually-long");
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
                        RuleId = RuleCatalog.SceneOutlier.Id,
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
                            RuleId = RuleCatalog.TopicDrift.Id,
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

                        string toneMessage = overlap < 0.15
                            ? $"The emotional register shifts sharply — from {prevDominantLabel.Value} to {dominant} — " +
                              $"around sentence {sentenceIndex + 1} in '{result.SceneTitle}'. " +
                              "The transition feels abrupt; a brief bridge moment could make the shift feel intentional."
                            : $"The mood shifts from {prevDominantLabel.Value} to {dominant} around sentence {sentenceIndex + 1} " +
                              $"in '{result.SceneTitle}'. This may be a deliberate turn — " +
                              "read it against the previous scene to check whether the shift lands.";

                        notes.Add(new NlpNote
                        {
                            RuleId = RuleCatalog.ToneShift.Id,
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

        /// <summary>
        /// Cross-book developmental notes (every book rule except the per-chapter scene
        /// count, which needs the Book's outline rather than analysis results).
        /// </summary>
        internal static List<NlpNote> GenerateBookNotes(List<SceneAnalysisResult> allResults, Book book)
        {
            if (allResults.Count < 2) return [];
            var defaults = ProseRuleEngine.CreateDefault();
            var engine = new ProseRuleEngine([], defaults.BookRules.Where(r => r is not SceneCountRule));
            return engine.EvaluateBook(new BookRuleContext
            {
                Results = allResults,
                Book = book,
                StyleSheet = book?.StyleSheet ?? new EditorialStyleSheet()
            });
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

    /// <summary>Result of a fast, single-scene analysis run while the author edits.</summary>
    /// <param name="Document">The freshly built scene document.</param>
    /// <param name="Result">Scene profile and the notes from the rules that ran.</param>
    /// <param name="CoveredRuleIds">Rules that ran; their older notes for this scene are superseded.</param>
    public sealed record LiveSceneAnalysis(
        SceneDocument Document,
        SceneAnalysisResult Result,
        HashSet<string> CoveredRuleIds);
}
