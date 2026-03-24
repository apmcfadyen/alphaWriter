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
                        Category = NlpNoteCategory.Structure,
                        SceneId = sceneId,
                        SceneTitle = sceneTitle,
                        ChapterTitle = chapterTitle,
                        Message = $"'{sceneTitle}' is {metrics.SceneWordCount:N0} words — " +
                                  $"{metrics.SceneToChapterRatio:F1}x the chapter average of " +
                                  $"{metrics.ChapterAverageWordCount:N0}. Consider splitting into multiple scenes."
                    });
                }
                else if (metrics.SceneToChapterRatio < 0.2 && metrics.SceneWordCount > 0)
                {
                    notes.Add(new NlpNote
                    {
                        Severity = NlpNoteSeverity.Info,
                        Category = NlpNoteCategory.Structure,
                        SceneId = sceneId,
                        SceneTitle = sceneTitle,
                        ChapterTitle = chapterTitle,
                        Message = $"'{sceneTitle}' is only {metrics.SceneWordCount:N0} words — " +
                                  $"much shorter than the chapter average of {metrics.ChapterAverageWordCount:N0}. " +
                                  "Consider whether it could be merged with an adjacent scene."
                    });
                }
            }

            // Flag long narration streaks (>15 sentences without dialogue)
            if (metrics.LongestNarrationStreak > 15)
            {
                notes.Add(new NlpNote
                {
                    Severity = NlpNoteSeverity.Warning,
                    Category = NlpNoteCategory.Pacing,
                    SceneId = sceneId,
                    SceneTitle = sceneTitle,
                    ChapterTitle = chapterTitle,
                    Message = $"'{sceneTitle}' has a stretch of {metrics.LongestNarrationStreak} consecutive " +
                              "sentences without dialogue. This may slow pacing — consider breaking it up " +
                              "with dialogue or action."
                });
            }

            // Flag very low dialogue density when other scenes in the chapter are dialogue-heavy
            // (This is a standalone check — cross-scene comparison happens in NlpAnalysisService)

            return notes;
        }
    }
}
