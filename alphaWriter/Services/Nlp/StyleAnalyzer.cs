using System;
using System.Collections.Generic;
using System.Linq;
using alphaWriter.Models.Analysis;

namespace alphaWriter.Services.Nlp
{
    public class StyleAnalyzer : IStyleAnalyzer
    {
        public StyleProfile Analyze(IReadOnlyList<string> sentences)
        {
            if (sentences.Count == 0)
                return new StyleProfile();

            var wordCounts = new List<int>(sentences.Count);
            int totalWords = 0;
            int totalWordLength = 0;
            int contractionCount = 0;
            int dialogueCount = 0;

            foreach (var sentence in sentences)
            {
                var words = sentence.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
                wordCounts.Add(words.Length);
                totalWords += words.Length;
                totalWordLength += words.Sum(w => w.Length);

                if (NlpTextExtractor.HasContraction(sentence))
                    contractionCount++;
                if (NlpTextExtractor.IsDialogue(sentence))
                    dialogueCount++;
            }

            double avgLength = totalWords > 0 ? (double)totalWords / sentences.Count : 0;
            double variance = wordCounts.Count > 1
                ? wordCounts.Sum(wc => Math.Pow(wc - avgLength, 2)) / (wordCounts.Count - 1)
                : 0;

            // Type-token ratio: unique words / total words
            var allWords = sentences
                .SelectMany(s => s.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries))
                .Select(w => w.ToLowerInvariant().Trim('.', ',', '!', '?', ';', ':', '"', '\'', '\u201C', '\u201D'))
                .Where(w => w.Length > 0)
                .ToList();

            double vocabRichness = allWords.Count > 0
                ? (double)allWords.Distinct().Count() / allWords.Count
                : 0;

            return new StyleProfile
            {
                AverageSentenceLength = avgLength,
                SentenceLengthStdDev = Math.Sqrt(variance),
                VocabularyRichness = vocabRichness,
                AverageWordLength = totalWords > 0 ? (double)totalWordLength / totalWords : 0,
                ContractionRate = sentences.Count > 0 ? (double)contractionCount / sentences.Count : 0,
                TotalSentences = sentences.Count,
                TotalWords = totalWords,
                DialogueRatio = sentences.Count > 0 ? (double)dialogueCount / sentences.Count : 0
            };
        }

        public List<NlpNote> DetectAnomalies(StyleProfile sceneProfile, StyleProfile chapterProfile,
            string sceneId, string sceneTitle, string chapterTitle)
        {
            var notes = new List<NlpNote>();

            if (chapterProfile.TotalSentences < 2 || sceneProfile.TotalSentences < 2)
                return notes;

            // Flag if scene's average sentence length deviates > 1.5 sigma from chapter
            if (chapterProfile.SentenceLengthStdDev > 0)
            {
                double zScore = Math.Abs(sceneProfile.AverageSentenceLength - chapterProfile.AverageSentenceLength)
                    / chapterProfile.SentenceLengthStdDev;
                if (zScore > 1.5)
                {
                    string direction = sceneProfile.AverageSentenceLength > chapterProfile.AverageSentenceLength
                        ? "longer" : "shorter";
                    notes.Add(new NlpNote
                    {
                        Severity = NlpNoteSeverity.Warning,
                        Category = NlpNoteCategory.Style,
                        SceneId = sceneId,
                        SceneTitle = sceneTitle,
                        ChapterTitle = chapterTitle,
                        Message = $"Sentences in '{sceneTitle}' average {sceneProfile.AverageSentenceLength:F1} words — " +
                                  $"significantly {direction} than the chapter average of {chapterProfile.AverageSentenceLength:F1}. " +
                                  "This may feel stylistically inconsistent."
                    });
                }
            }

            // Flag vocabulary richness deviation
            if (chapterProfile.VocabularyRichness > 0)
            {
                double diff = Math.Abs(sceneProfile.VocabularyRichness - chapterProfile.VocabularyRichness);
                if (diff > 0.15) // 15% deviation in type-token ratio
                {
                    string direction = sceneProfile.VocabularyRichness > chapterProfile.VocabularyRichness
                        ? "more varied" : "more repetitive";
                    notes.Add(new NlpNote
                    {
                        Severity = NlpNoteSeverity.Info,
                        Category = NlpNoteCategory.Style,
                        SceneId = sceneId,
                        SceneTitle = sceneTitle,
                        ChapterTitle = chapterTitle,
                        Message = $"The vocabulary in '{sceneTitle}' is {direction} than the rest of " +
                                  $"'{chapterTitle}' (TTR: {sceneProfile.VocabularyRichness:P0} vs {chapterProfile.VocabularyRichness:P0})."
                    });
                }
            }

            return notes;
        }
    }
}
