using System;
using System.Collections.Generic;
using alphaWriter.Models.Analysis;

namespace alphaWriter.Services.Nlp
{
    public class PacingAnalyzer : IPacingAnalyzer
    {
        public PacingMetrics Analyze(IReadOnlyList<string> sentences, IReadOnlyList<string> paragraphs,
            int sceneWordCount, double chapterAverageWordCount)
        {
            int dialogueCount = 0;
            int longestNarrationStreak = 0;
            int currentStreak = 0;

            foreach (var sentence in sentences)
            {
                if (NlpTextExtractor.IsDialogue(sentence))
                {
                    dialogueCount++;
                    currentStreak = 0;
                }
                else
                {
                    currentStreak++;
                    if (currentStreak > longestNarrationStreak)
                        longestNarrationStreak = currentStreak;
                }
            }

            return new PacingMetrics
            {
                DialogueDensity = sentences.Count > 0 ? (double)dialogueCount / sentences.Count : 0,
                LongestNarrationStreak = longestNarrationStreak,
                SceneWordCount = sceneWordCount,
                ChapterAverageWordCount = chapterAverageWordCount,
                SceneToChapterRatio = chapterAverageWordCount > 0
                    ? sceneWordCount / chapterAverageWordCount : 0,
                ParagraphCount = paragraphs.Count,
                AverageParagraphLength = paragraphs.Count > 0
                    ? (double)sentences.Count / paragraphs.Count : 0
            };
        }

        public List<NlpNote> DetectIssues(PacingMetrics metrics,
            string sceneId, string sceneTitle, string chapterTitle)
        {
            var notes = new List<NlpNote>();

            // Flag scenes > 3x or < 0.2x chapter average word count
            if (metrics.ChapterAverageWordCount > 0)
            {
                if (metrics.SceneToChapterRatio > 3.0)
                {
                    notes.Add(new NlpNote
                    {
                        Severity = NlpNoteSeverity.Warning,
                        Category = NlpNoteCategory.DevelopmentalEditor,
                        SceneId = sceneId,
                        SceneTitle = sceneTitle,
                        ChapterTitle = chapterTitle,
                        Message = $"'{sceneTitle}' is {metrics.SceneWordCount:N0} words — " +
                                  $"{metrics.SceneToChapterRatio:F1}× the chapter average of {metrics.ChapterAverageWordCount:N0}. " +
                                  "A scene this long against its surroundings disrupts the chapter's rhythm. " +
                                  "Consider splitting at a natural tension point to give readers a breath."
                    });
                }
                else if (metrics.SceneToChapterRatio < 0.2 && metrics.SceneWordCount > 0)
                {
                    notes.Add(new NlpNote
                    {
                        Severity = NlpNoteSeverity.Info,
                        Category = NlpNoteCategory.DevelopmentalEditor,
                        SceneId = sceneId,
                        SceneTitle = sceneTitle,
                        ChapterTitle = chapterTitle,
                        Message = $"'{sceneTitle}' is only {metrics.SceneWordCount:N0} words against a chapter average " +
                                  $"of {metrics.ChapterAverageWordCount:N0}. Very short scenes can deliver sharp, " +
                                  "impactful moments — but if this one feels incomplete, " +
                                  "consider merging it with an adjacent scene."
                    });
                }
            }

            // Flag long narration streaks (>15 sentences without dialogue)
            if (metrics.LongestNarrationStreak > 15)
            {
                notes.Add(new NlpNote
                {
                    Severity = NlpNoteSeverity.Warning,
                    Category = NlpNoteCategory.DevelopmentalEditor,
                    SceneId = sceneId,
                    SceneTitle = sceneTitle,
                    ChapterTitle = chapterTitle,
                    Message = $"'{sceneTitle}' has a run of {metrics.LongestNarrationStreak} consecutive narration sentences " +
                              "without a single line of dialogue. Pure narration at this length can feel airless — " +
                              "even a brief exchange or piece of direct speech can ground readers and reset the rhythm."
                });
            }

            // Flag very low dialogue density when other scenes in the chapter are dialogue-heavy
            // (This is a standalone check — cross-scene comparison happens in NlpAnalysisService)

            return notes;
        }
    }
}
