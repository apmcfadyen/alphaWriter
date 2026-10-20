using alphaWriter.Models;
using alphaWriter.Models.Analysis;
using alphaWriter.Services.Nlp;
using alphaWriter.Services.Nlp.Parsing;
using alphaWriter.Services.Nlp.Rules;
using Moq;
using Xunit;

namespace alphaWriter.Tests;

/// <summary>
/// Rule behaviour, focused on the false positives the Phase 0 accuracy pass fixes.
/// </summary>
public class ProseRuleTests
{
    private static List<NlpNote> Run(ISceneRule rule, string text, Book? book = null,
        EditorialStyleSheet? sheet = null, SceneDocument? doc = null)
    {
        doc ??= SceneDocumentBuilder.FromPlainText(text);
        var ctx = new SceneRuleContext
        {
            Document = doc,
            SceneId = "s1",
            SceneTitle = "Scene",
            ChapterTitle = "Ch1",
            Book = book,
            StyleSheet = sheet ?? new EditorialStyleSheet(),
            Style = new StyleAnalyzer().Analyze(doc.Sentences.Select(s => s.Text).ToList())
        };
        return new ProseRuleEngine([rule], []).EvaluateScene(ctx);
    }

    /// <summary>Builds a document whose tokens are POS-tagged from a word → tag map.</summary>
    private static SceneDocument Tagged(string text, Dictionary<string, string> tags)
    {
        var doc = SceneDocumentBuilder.FromPlainText(text);
        foreach (var t in doc.Tokens)
            t.Pos = tags.TryGetValue(t.Text, out var tag) ? tag : (t.IsWord ? "NOUN" : "PUNCT");
        doc.HasPos = true;
        return doc;
    }

    // ── Adverbs ──────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Her family only arrived early for the holy day.")]
    [InlineData("Then Emily and Kelly left the lonely, friendly town.")]
    [InlineData("“Quickly, quietly, go,” she said.")]
    public void Adverbs_IgnoresNonAdverbsNamesAndDialogue(string sentence)
    {
        Assert.Empty(Run(new AdverbClusterRule(), sentence));
    }

    [Fact]
    public void Adverbs_FlagsClusterWithSpansOnEachAdverb()
    {
        var notes = Run(new AdverbClusterRule(), "She walked quickly and quietly away.");

        var note = Assert.Single(notes);
        Assert.Equal("quickly", note.Evidence);
        Assert.Single(note.ExtraSpans);
        Assert.Equal("style.adverbs", note.RuleId);
    }

    [Fact]
    public void Adverbs_AllowedWordIsSkipped()
    {
        var sheet = new EditorialStyleSheet { AllowedWords = ["quickly"] };
        Assert.Empty(Run(new AdverbClusterRule(), "She walked quickly and quietly away.", sheet: sheet));
    }

    // ── Show, don't tell ─────────────────────────────────────────────────────

    [Fact]
    public void ShowDontTell_FlagsNamedEmotion_WithSpan()
    {
        var note = Assert.Single(Run(new ShowDontTellRule(), "She felt angry at the delay."));
        Assert.Equal("felt angry", note.Evidence);
    }

    [Theory]
    [InlineData("She didn't feel angry at all.")]          // negated
    [InlineData("She was never afraid of the dark.")]       // negated
    [InlineData("She was tired after the climb.")]          // physical state, not an emotion label
    [InlineData("The room seemed gloomy in the rain.")]     // non-person subject
    [InlineData("“I feel so angry,” she said.")]  // dialogue
    [InlineData("He felt, briefly, that anger was pointless.")] // clause break
    public void ShowDontTell_DoesNotFire(string sentence)
    {
        Assert.Empty(Run(new ShowDontTellRule(), sentence));
    }

    [Fact]
    public void ShowDontTell_NamedCharacterSubject_Fires()
    {
        var book = new Book();
        book.Characters.Add(new Character { Name = "Maya" });
        Assert.Single(Run(new ShowDontTellRule(), "Maya suddenly felt terrified.", book));
    }

    // ── Passive voice ────────────────────────────────────────────────────────

    [Fact]
    public void Passive_Progressive_IsNotPassive()
    {
        var doc = Tagged("She was running home.", new() { ["She"] = "PRON", ["was"] = "AUX", ["running"] = "VERB" });
        Assert.Empty(Run(new PassiveVoiceRule(), "", doc: doc));
    }

    [Fact]
    public void Passive_StatalParticiple_IsNotFlagged()
    {
        var doc = Tagged("He was supposed to wait.", new() { ["He"] = "PRON", ["was"] = "AUX", ["supposed"] = "VERB", ["to"] = "PART", ["wait"] = "VERB" });
        Assert.Empty(Run(new PassiveVoiceRule(), "", doc: doc));
    }

    [Fact]
    public void Passive_WithAgent_FlagsAndNamesAgent()
    {
        var doc = Tagged("The door was opened by Tom.", new() { ["was"] = "AUX", ["opened"] = "VERB", ["by"] = "ADP", ["Tom"] = "PROPN" });
        var note = Assert.Single(Run(new PassiveVoiceRule(), "", doc: doc));
        Assert.Equal("was opened", note.Evidence);
        Assert.Contains("Tom", note.Message);
    }

    [Fact]
    public void Passive_StatalParticipleWithAgent_IsFlagged()
    {
        var doc = Tagged("She was frightened by the noise.", new() { ["She"] = "PRON", ["was"] = "AUX", ["frightened"] = "VERB", ["by"] = "ADP" });
        Assert.Single(Run(new PassiveVoiceRule(), "", doc: doc));
    }

    [Fact]
    public void Passive_WithoutPos_DoesNotRunInEngine()
    {
        Assert.Empty(Run(new PassiveVoiceRule(), "The door was opened by Tom."));
    }

    // ── Echoes ───────────────────────────────────────────────────────────────

    [Fact]
    public void Echo_MatchesWordFamilies_AndUnderlinesEveryOccurrence()
    {
        var notes = Run(new EchoRule(),
            "She walked to the gate. The dog barked twice. He was walking behind. Walks like that tired her.");

        var note = Assert.Single(notes);
        Assert.Equal("walked", note.Evidence);
        Assert.Equal(2, note.ExtraSpans.Count);
    }

    [Fact]
    public void Echo_IgnoresCharacterNames()
    {
        var book = new Book();
        book.Characters.Add(new Character { Name = "Marcus", Aka = "Marco" });
        var notes = Run(new EchoRule(),
            "Marcus left. Marcus waited outside. Then Marcus returned home.", book);
        Assert.Empty(notes);
    }

    [Fact]
    public void Echo_IgnoresAllowedWords()
    {
        var sheet = new EditorialStyleSheet { AllowedWords = ["aether"] };
        var notes = Run(new EchoRule(),
            "The aether hummed. It was aether all the way down. She drank aether.", sheet: sheet);
        Assert.Empty(notes);
    }

    // ── Thresholds & genre presets ───────────────────────────────────────────

    [Fact]
    public void NarrationRun_RespectsGenrePreset()
    {
        // 12 narration sentences: fine for General (15), too long for Thriller (10)
        var text = string.Join(" ", Enumerable.Range(1, 12).Select(i => $"The wind moved the trees {i} times."));

        Assert.Empty(Run(new NarrationRunRule(), text));
        var thriller = new EditorialStyleSheet { Genre = "Thriller" };
        Assert.Single(Run(new NarrationRunRule(), text, sheet: thriller));
    }

    [Fact]
    public void Threshold_ExplicitOverrideBeatsGenre()
    {
        var sheet = new EditorialStyleSheet
        {
            Genre = "Thriller",
            Thresholds = { ["pacing.narration-run:maxRun"] = 40 }
        };
        Assert.Equal(40, sheet.GetThreshold("pacing.narration-run", "maxRun", 15));
        Assert.Equal(15, new EditorialStyleSheet().GetThreshold("pacing.narration-run", "maxRun", 15));
    }
}
