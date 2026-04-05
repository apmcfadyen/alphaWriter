using alphaWriter.Models.Analysis;
using alphaWriter.Services.Nlp;
using Xunit;

namespace alphaWriter.Tests;

public class StyleAnalyzerTests
{
    private readonly StyleAnalyzer _analyzer = new();

    [Fact]
    public void Analyze_EmptySentences_ReturnsDefaultProfile()
    {
        var result = _analyzer.Analyze([]);
        Assert.Equal(0, result.TotalSentences);
        Assert.Equal(0, result.TotalWords);
    }

    [Fact]
    public void Analyze_SingleSentence_ComputesCorrectMetrics()
    {
        var result = _analyzer.Analyze(["The quick brown fox jumps."]);
        Assert.Equal(1, result.TotalSentences);
        Assert.Equal(5, result.TotalWords);
        Assert.Equal(5.0, result.AverageSentenceLength);
        Assert.Equal(0, result.SentenceLengthStdDev); // single sentence = no deviation
    }

    [Fact]
    public void Analyze_MultipleSentences_ComputesAvgAndStdDev()
    {
        // 3 words, 5 words, 7 words => avg = 5, stddev > 0
        var result = _analyzer.Analyze([
            "One two three.",
            "One two three four five.",
            "One two three four five six seven."
        ]);
        Assert.Equal(3, result.TotalSentences);
        Assert.Equal(15, result.TotalWords);
        Assert.Equal(5.0, result.AverageSentenceLength, 1);
        Assert.True(result.SentenceLengthStdDev > 0);
    }

    [Fact]
    public void Analyze_VocabularyRichness_UniqueWordsRatio()
    {
        // All unique words
        var unique = _analyzer.Analyze(["Alpha beta gamma."]);
        Assert.True(unique.VocabularyRichness > 0.9);

        // Repeated words
        var repetitive = _analyzer.Analyze(["The the the the."]);
        Assert.True(repetitive.VocabularyRichness < 0.5);
    }

    [Fact]
    public void Analyze_DialogueRatio_DetectsQuotedSentences()
    {
        var result = _analyzer.Analyze([
            "\u201CHello!\u201D she said.",
            "The room fell silent.",
            "\"Go away,\" he muttered."
        ]);
        // 2 out of 3 are dialogue
        Assert.True(result.DialogueRatio > 0.5);
    }

    [Fact]
    public void Analyze_ContractionRate_DetectsContractions()
    {
        var result = _analyzer.Analyze([
            "She didn't know.",
            "He couldn't believe it.",
            "The sun was bright."
        ]);
        // 2 out of 3 have contractions
        Assert.True(result.ContractionRate > 0.5);
    }

    // ── DetectAnomalies ─────────────────────────────────────────────────────

    [Fact]
    public void DetectAnomalies_SimilarProfiles_NoNotes()
    {
        var scene = new StyleProfile { AverageSentenceLength = 10, SentenceLengthStdDev = 3, VocabularyRichness = 0.5, TotalSentences = 20 };
        var chapter = new StyleProfile { AverageSentenceLength = 10, SentenceLengthStdDev = 3, VocabularyRichness = 0.5, TotalSentences = 100 };
        var notes = _analyzer.DetectAnomalies(scene, chapter, "s1", "Test Scene", "Test Chapter");
        Assert.Empty(notes);
    }

    [Fact]
    public void DetectAnomalies_ExtremeSentenceLength_FlagsWarning()
    {
        var scene = new StyleProfile { AverageSentenceLength = 25, SentenceLengthStdDev = 2, VocabularyRichness = 0.5, TotalSentences = 20 };
        var chapter = new StyleProfile { AverageSentenceLength = 10, SentenceLengthStdDev = 3, VocabularyRichness = 0.5, TotalSentences = 100 };
        var notes = _analyzer.DetectAnomalies(scene, chapter, "s1", "Long Scene", "Ch1");
        Assert.Contains(notes, n => n.Category == NlpNoteCategory.CopyEditor && n.Severity == NlpNoteSeverity.Warning);
    }

    [Fact]
    public void DetectAnomalies_VocabularyDeviation_FlagsInfo()
    {
        var scene = new StyleProfile { AverageSentenceLength = 10, SentenceLengthStdDev = 3, VocabularyRichness = 0.8, TotalSentences = 20 };
        var chapter = new StyleProfile { AverageSentenceLength = 10, SentenceLengthStdDev = 3, VocabularyRichness = 0.4, TotalSentences = 100 };
        var notes = _analyzer.DetectAnomalies(scene, chapter, "s1", "Rich Scene", "Ch1");
        Assert.Contains(notes, n => n.Category == NlpNoteCategory.DevelopmentalEditor && n.Severity == NlpNoteSeverity.Info);
    }

    [Fact]
    public void DetectAnomalies_TooFewSentences_ReturnsEmpty()
    {
        var scene = new StyleProfile { TotalSentences = 1 };
        var chapter = new StyleProfile { TotalSentences = 1 };
        var notes = _analyzer.DetectAnomalies(scene, chapter, "s1", "Short", "Ch1");
        Assert.Empty(notes);
    }
}
