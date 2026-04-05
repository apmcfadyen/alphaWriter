using alphaWriter.Models;
using alphaWriter.Models.Analysis;
using alphaWriter.Services.Nlp;
using Xunit;

namespace alphaWriter.Tests;

public class GenerateBookNotesTests
{
    private static Book CreateTestBook() => new() { Title = "Test Book" };

    private static SceneAnalysisResult MakeResult(string sceneTitle, string chapter,
        int totalWords, int totalSentences, double dialogueRatio = 0.3,
        double contractionRate = 0.3)
    {
        var sentences = new List<SentenceAnalysis>();
        for (int i = 0; i < totalSentences; i++)
        {
            sentences.Add(new SentenceAnalysis
            {
                Index = i,
                Text = $"Test sentence {i}.",
                WordCount = totalSentences > 0 ? totalWords / totalSentences : 0
            });
        }

        return new SceneAnalysisResult
        {
            SceneId = Guid.NewGuid().ToString(),
            SceneTitle = sceneTitle,
            ChapterTitle = chapter,
            Style = new StyleProfile
            {
                TotalWords = totalWords,
                TotalSentences = totalSentences,
                AverageSentenceLength = totalSentences > 0 ? (double)totalWords / totalSentences : 0,
                DialogueRatio = dialogueRatio,
                ContractionRate = contractionRate
            },
            Sentences = sentences
        };
    }

    [Fact]
    public void GenerateBookNotes_TooFewResults_ReturnsEmpty()
    {
        var results = new List<SceneAnalysisResult>
        {
            MakeResult("Scene 1", "Ch1", 500, 30)
        };

        var notes = NlpAnalysisService.GenerateBookNotes(results, CreateTestBook());
        Assert.Empty(notes);
    }

    [Fact]
    public void GenerateBookNotes_ConsistentScenes_NoDialogueOrContractionNotes()
    {
        // All scenes have similar dialogue ratio and contraction rate
        var results = new List<SceneAnalysisResult>
        {
            MakeResult("Scene 1", "Ch1", 500, 30, 0.3, 0.3),
            MakeResult("Scene 2", "Ch1", 520, 32, 0.32, 0.28),
            MakeResult("Scene 3", "Ch2", 480, 28, 0.28, 0.32),
            MakeResult("Scene 4", "Ch2", 510, 31, 0.31, 0.29),
        };

        var notes = NlpAnalysisService.GenerateBookNotes(results, CreateTestBook());

        // No dialogue density or contraction anomalies expected
        Assert.DoesNotContain(notes, n => n.Category == NlpNoteCategory.DevelopmentalEditor &&
            n.Message.Contains("dialogue"));
        Assert.DoesNotContain(notes, n => n.Category == NlpNoteCategory.LineEditor &&
            n.Message.Contains("contraction"));
    }

    [Fact]
    public void GenerateBookNotes_DialogueDensityOutlier_FlagsNote()
    {
        var results = new List<SceneAnalysisResult>
        {
            MakeResult("Scene 1", "Ch1", 500, 30, 0.3, 0.3),
            MakeResult("Scene 2", "Ch1", 500, 30, 0.3, 0.3),
            MakeResult("Scene 3", "Ch2", 500, 30, 0.3, 0.3),
            MakeResult("Scene 4", "Ch2", 500, 30, 0.3, 0.3),
            MakeResult("Scene 5", "Ch3", 500, 30, 0.3, 0.3),
            // Extreme outlier: 95% dialogue
            MakeResult("All Talk", "Ch3", 500, 30, 0.95, 0.3),
        };

        var notes = NlpAnalysisService.GenerateBookNotes(results, CreateTestBook());

        Assert.Contains(notes, n =>
            n.Category == NlpNoteCategory.DevelopmentalEditor &&
            n.SceneTitle == "All Talk" &&
            n.Message.Contains("dialogue"));
    }

    [Fact]
    public void GenerateBookNotes_ContractionRateOutlier_FlagsNote()
    {
        var results = new List<SceneAnalysisResult>
        {
            MakeResult("Scene 1", "Ch1", 500, 30, 0.3, 0.8),
            MakeResult("Scene 2", "Ch1", 500, 30, 0.3, 0.8),
            MakeResult("Scene 3", "Ch2", 500, 30, 0.3, 0.8),
            MakeResult("Scene 4", "Ch2", 500, 30, 0.3, 0.75),
            MakeResult("Scene 5", "Ch3", 500, 30, 0.3, 0.82),
            // Extreme outlier: 0% contractions (very formal)
            MakeResult("Formal Scene", "Ch3", 500, 30, 0.3, 0.0),
        };

        var notes = NlpAnalysisService.GenerateBookNotes(results, CreateTestBook());

        Assert.Contains(notes, n =>
            n.Category == NlpNoteCategory.LineEditor &&
            n.SceneTitle == "Formal Scene" &&
            n.Message.Contains("contraction"));
    }

    [Fact]
    public void GenerateBookNotes_ChapterWordCountImbalance_FlagsNote()
    {
        // 8 consistent chapters + 1 extreme outlier for z > 2.0
        var results = new List<SceneAnalysisResult>
        {
            MakeResult("S1", "Ch1", 1000, 60),
            MakeResult("S2", "Ch2", 1000, 60),
            MakeResult("S3", "Ch3", 1000, 60),
            MakeResult("S4", "Ch4", 1000, 60),
            MakeResult("S5", "Ch5", 1000, 60),
            MakeResult("S6", "Ch6", 1000, 60),
            MakeResult("S7", "Ch7", 1000, 60),
            MakeResult("S8", "Ch8", 1000, 60),
            // Ch9 is 15x larger
            MakeResult("S9", "Ch9", 15000, 900),
        };

        var notes = NlpAnalysisService.GenerateBookNotes(results, CreateTestBook());

        Assert.Contains(notes, n =>
            n.Category == NlpNoteCategory.DevelopmentalEditor &&
            n.ChapterTitle == "Ch9" &&
            n.Message.Contains("longer"));
    }

    [Fact]
    public void GenerateBookNotes_LongOpeningSentence_FlagsNote()
    {
        var result = MakeResult("Verbose Opening", "Ch1", 300, 20);
        // Override first sentence to be very long
        result.Sentences[0] = new SentenceAnalysis
        {
            Index = 0,
            Text = string.Join(" ", Enumerable.Repeat("word", 40)) + ".",
            WordCount = 40
        };
        result.Style.AverageSentenceLength = 15;

        var results = new List<SceneAnalysisResult>
        {
            result,
            MakeResult("Normal Scene", "Ch1", 300, 20),
            MakeResult("Another Scene", "Ch2", 300, 20),
        };

        var notes = NlpAnalysisService.GenerateBookNotes(results, CreateTestBook());

        Assert.Contains(notes, n =>
            n.Category == NlpNoteCategory.DevelopmentalEditor &&
            n.SceneTitle == "Verbose Opening" &&
            n.Message.Contains("opens"));
    }

    [Fact]
    public void GenerateBookNotes_LongClosingSentence_FlagsNote()
    {
        var result = MakeResult("Verbose Close", "Ch1", 300, 20);
        // Override last sentence to be very long
        result.Sentences[^1] = new SentenceAnalysis
        {
            Index = result.Sentences.Count - 1,
            Text = string.Join(" ", Enumerable.Repeat("word", 40)) + ".",
            WordCount = 40
        };
        result.Style.AverageSentenceLength = 15;

        var results = new List<SceneAnalysisResult>
        {
            result,
            MakeResult("Normal Scene", "Ch1", 300, 20),
            MakeResult("Another Scene", "Ch2", 300, 20),
        };

        var notes = NlpAnalysisService.GenerateBookNotes(results, CreateTestBook());

        Assert.Contains(notes, n =>
            n.Category == NlpNoteCategory.DevelopmentalEditor &&
            n.SceneTitle == "Verbose Close" &&
            n.Message.Contains("closes"));
    }
}
