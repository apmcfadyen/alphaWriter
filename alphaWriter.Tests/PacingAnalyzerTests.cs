using alphaWriter.Models.Analysis;
using alphaWriter.Services.Nlp;
using Xunit;

namespace alphaWriter.Tests;

public class PacingAnalyzerTests
{
    private readonly PacingAnalyzer _analyzer = new();

    [Fact]
    public void Analyze_EmptyInput_ReturnsZeroMetrics()
    {
        var result = _analyzer.Analyze([], [], 0, 0);
        Assert.Equal(0, result.DialogueDensity);
        Assert.Equal(0, result.LongestNarrationStreak);
    }

    [Fact]
    public void Analyze_AllDialogue_FullDensity()
    {
        var sentences = new List<string>
        {
            "\"Hello!\" she said.",
            "\"Hi there,\" he replied.",
            "\"How are you?\" she asked."
        };
        var result = _analyzer.Analyze(sentences, ["paragraph one"], 100, 200);
        Assert.Equal(1.0, result.DialogueDensity, 2);
        Assert.Equal(0, result.LongestNarrationStreak);
    }

    [Fact]
    public void Analyze_AllNarration_ZeroDensity()
    {
        var sentences = new List<string>
        {
            "The sun rose slowly.",
            "Birds sang in the trees.",
            "A gentle breeze blew."
        };
        var result = _analyzer.Analyze(sentences, ["paragraph"], 80, 200);
        Assert.Equal(0.0, result.DialogueDensity);
        Assert.Equal(3, result.LongestNarrationStreak);
    }

    [Fact]
    public void Analyze_MixedContent_TracksLongestStreak()
    {
        var sentences = new List<string>
        {
            "\"Start,\" she said.",    // dialogue
            "He walked away.",          // narration streak = 1
            "The door slammed.",        // narration streak = 2
            "Rain fell hard.",          // narration streak = 3
            "\"Stop!\" he cried.",      // dialogue breaks streak
            "Silence returned."         // narration streak = 1
        };
        var result = _analyzer.Analyze(sentences, ["p1"], 120, 200);
        Assert.Equal(3, result.LongestNarrationStreak);
    }

    [Fact]
    public void Analyze_SceneToChapterRatio_Computed()
    {
        var result = _analyzer.Analyze(["One sentence."], ["para"], 600, 200);
        Assert.Equal(3.0, result.SceneToChapterRatio, 1);
    }

    // ── DetectIssues ────────────────────────────────────────────────────────

    [Fact]
    public void DetectIssues_OversizedScene_FlagsWarning()
    {
        var metrics = new PacingMetrics
        {
            SceneWordCount = 4000,
            ChapterAverageWordCount = 1000,
            SceneToChapterRatio = 4.0,
            LongestNarrationStreak = 5
        };
        var notes = _analyzer.DetectIssues(metrics, "s1", "Long Scene", "Ch1");
        Assert.Contains(notes, n => n.Category == NlpNoteCategory.Structure);
    }

    [Fact]
    public void DetectIssues_TinyScene_FlagsInfo()
    {
        var metrics = new PacingMetrics
        {
            SceneWordCount = 30,
            ChapterAverageWordCount = 1000,
            SceneToChapterRatio = 0.03,
            LongestNarrationStreak = 2
        };
        var notes = _analyzer.DetectIssues(metrics, "s1", "Tiny Scene", "Ch1");
        Assert.Contains(notes, n => n.Category == NlpNoteCategory.Structure && n.Severity == NlpNoteSeverity.Info);
    }

    [Fact]
    public void DetectIssues_LongNarrationStreak_FlagsWarning()
    {
        var metrics = new PacingMetrics
        {
            SceneWordCount = 1000,
            ChapterAverageWordCount = 1000,
            SceneToChapterRatio = 1.0,
            LongestNarrationStreak = 20
        };
        var notes = _analyzer.DetectIssues(metrics, "s1", "Dense Scene", "Ch1");
        Assert.Contains(notes, n => n.Category == NlpNoteCategory.Pacing);
    }

    [Fact]
    public void DetectIssues_NormalScene_NoNotes()
    {
        var metrics = new PacingMetrics
        {
            SceneWordCount = 1000,
            ChapterAverageWordCount = 1000,
            SceneToChapterRatio = 1.0,
            LongestNarrationStreak = 5
        };
        var notes = _analyzer.DetectIssues(metrics, "s1", "Normal", "Ch1");
        Assert.Empty(notes);
    }
}
