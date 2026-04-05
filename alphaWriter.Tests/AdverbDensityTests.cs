using alphaWriter.Models.Analysis;
using alphaWriter.Services.Nlp;
using Xunit;

namespace alphaWriter.Tests;

public class AdverbDensityTests
{
    private readonly StyleAnalyzer _analyzer = new();

    // ── Per-sentence notes ────────────────────────────────────────────────────

    [Fact]
    public void DetectAdverbDensity_NoAdverbs_ReturnsEmpty()
    {
        var notes = _analyzer.DetectAdverbDensity(
            ["She ran to the door.", "He opened it slowly."],
            "s1", "Scene", "Ch1");

        // "slowly" is one -ly word — below per-sentence threshold of 2
        Assert.DoesNotContain(notes, n => n.SentenceIndex == 0);
    }

    [Fact]
    public void DetectAdverbDensity_TwoLyWordsInSentence_FlagsNote()
    {
        var notes = _analyzer.DetectAdverbDensity(
            ["She ran quickly and desperately toward the exit."],
            "s1", "Test Scene", "Ch1");

        var note = Assert.Single(notes, n => n.SentenceIndex == 0);
        Assert.Equal(NlpNoteCategory.CopyEditor, note.Category);
        Assert.Equal(NlpNoteSeverity.Info, note.Severity);
        Assert.Contains("2 adverbs", note.Message);
    }

    [Fact]
    public void DetectAdverbDensity_ThreeLyWordsInSentence_FlagsCount()
    {
        var notes = _analyzer.DetectAdverbDensity(
            ["He moved quickly, quietly, and deliberately through the shadows."],
            "s1", "Scene", "Ch1");

        Assert.Contains(notes, n => n.SentenceIndex == 0);
        var note = notes.First(n => n.SentenceIndex == 0);
        Assert.Contains("3 adverbs", note.Message);
    }

    [Fact]
    public void DetectAdverbDensity_OneLyWord_DoesNotFlagSentence()
    {
        var notes = _analyzer.DetectAdverbDensity(
            ["He walked slowly down the street."],
            "s1", "Scene", "Ch1");

        // Only one -ly word — per-sentence threshold is 2
        Assert.DoesNotContain(notes, n => n.SentenceIndex.HasValue);
    }

    [Fact]
    public void DetectAdverbDensity_ShortWordEndingLy_Ignored()
    {
        // "fly", "ply" — less than 5 chars — should not count
        var notes = _analyzer.DetectAdverbDensity(
            ["Watch it fly and ply the air."],
            "s1", "Scene", "Ch1");

        Assert.DoesNotContain(notes, n => n.SentenceIndex == 0);
    }

    // ── Scene-level density note ──────────────────────────────────────────────

    [Fact]
    public void DetectAdverbDensity_HighDensityScene_FlagsSceneNote()
    {
        // Build a scene where many words are -ly adverbs (well above 3%)
        var sentences = new List<string>();
        for (int i = 0; i < 10; i++)
            sentences.Add("She moved quickly and slowly through the very long corridor.");
        // Each sentence: "quickly" + "slowly" = 2 ly-words out of ~10 = ~20%

        var notes = _analyzer.DetectAdverbDensity(sentences, "s1", "Scene", "Ch1");

        // Should include a scene-level summary note (no SentenceIndex)
        Assert.Contains(notes, n => !n.SentenceIndex.HasValue && n.Message.Contains("adverb density"));
    }

    [Fact]
    public void DetectAdverbDensity_LowDensityLongScene_NoSceneNote()
    {
        // 60 words, only 1 -ly word → density < 3%
        var sentences = Enumerable.Range(0, 6)
            .Select(i => $"The soldier marched forward with discipline and purpose each day.")
            .ToList();
        sentences[0] = "The soldier marched carefully forward."; // one -ly word

        var notes = _analyzer.DetectAdverbDensity(sentences, "s1", "Scene", "Ch1");

        // No scene-level note
        Assert.DoesNotContain(notes, n => !n.SentenceIndex.HasValue);
    }

    [Fact]
    public void DetectAdverbDensity_EmptySentences_ReturnsEmpty()
    {
        var notes = _analyzer.DetectAdverbDensity([], "s1", "Scene", "Ch1");
        Assert.Empty(notes);
    }

    [Fact]
    public void DetectAdverbDensity_NoteIncludesExampleAdverbs()
    {
        var notes = _analyzer.DetectAdverbDensity(
            ["She ran quickly and desperately toward the exit."],
            "s1", "Scene", "Ch1");

        var sentenceNote = notes.FirstOrDefault(n => n.SentenceIndex == 0);
        Assert.NotNull(sentenceNote);
        // Message should quote at least one of the -ly words
        Assert.True(sentenceNote!.Message.Contains("quickly") ||
                    sentenceNote.Message.Contains("desperately"));
    }

    [Fact]
    public void DetectAdverbDensity_PunctuationAttachedToAdverb_StillCounted()
    {
        // "quickly," — comma attached to adverb
        var notes = _analyzer.DetectAdverbDensity(
            ["She moved quickly, deliberately, and forcefully."],
            "s1", "Scene", "Ch1");

        Assert.Contains(notes, n => n.SentenceIndex == 0);
    }
}
