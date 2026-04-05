using alphaWriter.Models.Analysis;
using alphaWriter.Services.Nlp;
using Moq;
using Xunit;

namespace alphaWriter.Tests;

/// <summary>
/// Tests passive voice detection via a mocked IPosTaggingService so tests
/// run without downloading the Catalyst model.
/// </summary>
public class PassiveVoiceTests
{
    private readonly StyleAnalyzer _analyzer = new();

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>Builds a mock POS service that returns the given tagged tokens per sentence.</summary>
    private static Mock<IPosTaggingService> MockPos(
        params IReadOnlyList<(string Value, string Pos)>[] perSentenceTags)
    {
        var mock = new Mock<IPosTaggingService>();
        mock.Setup(s => s.TagSentences(It.IsAny<IReadOnlyList<string>>()))
            .Returns(perSentenceTags);
        return mock;
    }

    // Token helpers
    private static (string Value, string Pos) Aux(string v) => (v, "AUX");
    private static (string Value, string Pos) Verb(string v) => (v, "VERB");
    private static (string Value, string Pos) Adj(string v) => (v, "ADJ");
    private static (string Value, string Pos) Adv(string v) => (v, "ADV");
    private static (string Value, string Pos) Noun(string v) => (v, "NOUN");

    // ── Basic passive constructions ───────────────────────────────────────────

    [Fact]
    public void DetectPassiveVoice_SimplePassive_FlagsNote()
    {
        // "The ball was kicked." → AUX(was) + VERB(kicked)
        var mock = MockPos(
            [Noun("The"), Noun("ball"), Aux("was"), Verb("kicked")]);

        var notes = _analyzer.DetectPassiveVoice(
            ["The ball was kicked."], mock.Object, "s1", "Scene", "Ch1");

        Assert.Single(notes);
        Assert.Equal(0, notes[0].SentenceIndex);
        Assert.Equal(NlpNoteCategory.CopyEditor, notes[0].Category);
        Assert.Equal(NlpNoteSeverity.Info, notes[0].Severity);
        Assert.Contains("passive voice", notes[0].Message);
    }

    [Fact]
    public void DetectPassiveVoice_WerePassive_FlagsNote()
    {
        // "Mistakes were made." → AUX(were) + VERB(made)
        var mock = MockPos(
            [Noun("Mistakes"), Aux("were"), Verb("made")]);

        var notes = _analyzer.DetectPassiveVoice(
            ["Mistakes were made."], mock.Object, "s1", "Scene", "Ch1");

        Assert.Single(notes);
    }

    [Fact]
    public void DetectPassiveVoice_PassiveWithAdverb_FlagsNote()
    {
        // "She was quickly defeated." → AUX(was) + ADV(quickly) + VERB(defeated)
        var mock = MockPos(
            [Noun("She"), Aux("was"), Adv("quickly"), Verb("defeated")]);

        var notes = _analyzer.DetectPassiveVoice(
            ["She was quickly defeated."], mock.Object, "s1", "Scene", "Ch1");

        Assert.Single(notes);
    }

    [Fact]
    public void DetectPassiveVoice_ContinuousPassive_FlagsNote()
    {
        // "He was being followed." → AUX(was) + AUX(being) + VERB(followed)
        var mock = MockPos(
            [Noun("He"), Aux("was"), Aux("being"), Verb("followed")]);

        var notes = _analyzer.DetectPassiveVoice(
            ["He was being followed."], mock.Object, "s1", "Scene", "Ch1");

        Assert.Single(notes);
    }

    // ── Active voice — should NOT be flagged ─────────────────────────────────

    [Fact]
    public void DetectPassiveVoice_ActiveVoice_NoNote()
    {
        // "She defeated her rival." → NOUN + VERB → no AUX+VERB pattern
        var mock = MockPos(
            [Noun("She"), Verb("defeated"), Noun("her"), Noun("rival")]);

        var notes = _analyzer.DetectPassiveVoice(
            ["She defeated her rival."], mock.Object, "s1", "Scene", "Ch1");

        Assert.Empty(notes);
    }

    // ── Predicate adjective — the key Catalyst advantage ─────────────────────

    [Fact]
    public void DetectPassiveVoice_PredicateAdjective_NotFlagged()
    {
        // "She was tired." → AUX(was) + ADJ(tired)
        // Catalyst correctly tags "tired" as ADJ here, so no passive note.
        var mock = MockPos(
            [Noun("She"), Aux("was"), Adj("tired")]);

        var notes = _analyzer.DetectPassiveVoice(
            ["She was tired."], mock.Object, "s1", "Scene", "Ch1");

        Assert.Empty(notes);
    }

    [Fact]
    public void DetectPassiveVoice_WasBroken_AsAdjective_NotFlagged()
    {
        // "The window was broken." — if tagger marks "broken" as ADJ (stative),
        // it should not be flagged.
        var mock = MockPos(
            [Noun("The"), Noun("window"), Aux("was"), Adj("broken")]);

        var notes = _analyzer.DetectPassiveVoice(
            ["The window was broken."], mock.Object, "s1", "Scene", "Ch1");

        Assert.Empty(notes);
    }

    [Fact]
    public void DetectPassiveVoice_WasBroken_AsVerb_IsFlagged()
    {
        // Same words, but tagger marks "broken" as VERB (passive event sense).
        var mock = MockPos(
            [Noun("The"), Noun("window"), Aux("was"), Verb("broken")]);

        var notes = _analyzer.DetectPassiveVoice(
            ["The window was broken."], mock.Object, "s1", "Scene", "Ch1");

        Assert.Single(notes);
    }

    // ── Multiple sentences ────────────────────────────────────────────────────

    [Fact]
    public void DetectPassiveVoice_MultipleSentences_FlagsCorrectIndices()
    {
        var mock = MockPos(
            // sentence 0: active
            [Noun("She"), Verb("ran")],
            // sentence 1: passive
            [Noun("He"), Aux("was"), Verb("told")],
            // sentence 2: active
            [Noun("They"), Verb("left")]);

        var notes = _analyzer.DetectPassiveVoice(
            ["She ran.", "He was told.", "They left."],
            mock.Object, "s1", "Scene", "Ch1");

        Assert.Single(notes);
        Assert.Equal(1, notes[0].SentenceIndex);
    }

    [Fact]
    public void DetectPassiveVoice_AllPassive_FlagsAll()
    {
        var mock = MockPos(
            [Aux("was"), Verb("seen")],
            [Aux("were"), Verb("told")],
            [Aux("is"), Verb("known")]);

        var notes = _analyzer.DetectPassiveVoice(
            ["It was seen.", "They were told.", "It is known."],
            mock.Object, "s1", "Scene", "Ch1");

        Assert.Equal(3, notes.Count);
        Assert.Equal([0, 1, 2], notes.Select(n => n.SentenceIndex).ToArray());
    }

    // ── Edge cases ────────────────────────────────────────────────────────────

    [Fact]
    public void DetectPassiveVoice_EmptySentences_ReturnsEmpty()
    {
        var mock = new Mock<IPosTaggingService>();
        mock.Setup(s => s.TagSentences(It.IsAny<IReadOnlyList<string>>()))
            .Returns([]);

        var notes = _analyzer.DetectPassiveVoice(
            [], mock.Object, "s1", "Scene", "Ch1");

        Assert.Empty(notes);
    }

    [Fact]
    public void DetectPassiveVoice_NoteMessageContainsSentenceText()
    {
        var mock = MockPos(
            [Aux("was"), Verb("defeated")]);

        var notes = _analyzer.DetectPassiveVoice(
            ["The champion was defeated."], mock.Object, "s1", "Scene", "Ch1");

        Assert.Single(notes);
        Assert.Contains("was defeated", notes[0].Message);
    }

    [Fact]
    public void DetectPassiveVoice_NoteContainsSentenceIndex()
    {
        var mock = MockPos(
            [Aux("was"), Verb("seen")]);

        var notes = _analyzer.DetectPassiveVoice(
            ["It was seen."], mock.Object, "s1", "Scene", "Ch1");

        Assert.NotNull(notes[0].SentenceIndex);
        Assert.Contains("Sentence 1", notes[0].Message);
    }

    // ── PosTaggingService unit tests (no model required) ─────────────────────

    [Fact]
    public void PosTaggingService_IsLoaded_BeforeLoad_ReturnsFalse()
    {
        var service = new PosTaggingService();
        Assert.False(service.IsLoaded);
    }

    [Fact]
    public void PosTaggingService_TagSentences_WithoutLoading_Throws()
    {
        var service = new PosTaggingService();
        Assert.Throws<InvalidOperationException>(
            () => service.TagSentences(["Hello world"]));
    }

    [Fact]
    public void PosTaggingService_UnloadModel_WhenNotLoaded_DoesNotThrow()
    {
        var service = new PosTaggingService();
        service.UnloadModel(); // should not throw
        Assert.False(service.IsLoaded);
    }

    [Fact]
    public void PosTaggingService_Dispose_DoesNotThrow()
    {
        var service = new PosTaggingService();
        service.Dispose();
        Assert.False(service.IsLoaded);
    }
}
