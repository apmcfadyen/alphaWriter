using System;
using System.Collections.Generic;
using System.Linq;
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
            var notes = Rules.SceneLengthRule.CreateNotes(metrics, sceneTitle).ToList();

            if (metrics.LongestNarrationStreak > 15)
            {
                notes.Add(new NlpNote
                {
                    RuleId = Rules.RuleCatalog.NarrationRun.Id,
                    Severity = NlpNoteSeverity.Warning,
                    Category = NlpNoteCategory.DevelopmentalEditor,
                    Message = Rules.NarrationRunRule.Message(sceneTitle, metrics.LongestNarrationStreak)
                });
            }

            foreach (var note in notes)
            {
                note.SceneId = sceneId;
                note.SceneTitle = sceneTitle;
                note.ChapterTitle = chapterTitle;
            }
            return notes;
        }
    }
}
