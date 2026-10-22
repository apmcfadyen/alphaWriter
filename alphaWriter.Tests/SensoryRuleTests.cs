using alphaWriter.Models;
using alphaWriter.Models.Analysis;
using alphaWriter.Services.Nlp.Parsing;
using alphaWriter.Services.Nlp.Rules;
using Xunit;

namespace alphaWriter.Tests;

/// <summary>Phase 2 batch 2: senses, concreteness and the tension arc.</summary>
public class SensoryRuleTests
{
    private static readonly Lazy<SensoryLexicon> Bundled = new(() =>
    {
        using var reader = new StreamReader(Path.Combine(AppContext.BaseDirectory, "lexicons", "sensorimotor.tsv"));
        return SensoryLexicon.Parse(reader);
    });

    private static List<NlpNote> Run(ISceneRule rule, string text)
    {
        var doc = SceneDocumentBuilder.FromPlainText(text);
        var ctx = new SceneRuleContext { Document = doc, SceneId = "s1", Book = new Book(), Senses = Bundled.Value };
        return new ProseRuleEngine([rule], []).EvaluateScene(ctx);
    }

    private static string Repeat(string paragraph, int times) =>
        string.Join("\n", Enumerable.Repeat(paragraph, times));

    // ── Lexicon ──────────────────────────────────────────────────────────────

    [Fact]
    public void Lexicon_IsBundledAndLarge() => Assert.True(Bundled.Value.Count > 30000);

    [Theory]
    [InlineData("hammered")]
    [InlineData("whispers")]
    [InlineData("stopped")]
    [InlineData("Lanterns")]
    public void Lexicon_FindsInflectedForms(string word) => Assert.NotNull(Bundled.Value.Lookup(word));

    [Theory]
    [InlineData("whisper", Sense.Sound)]
    [InlineData("cinnamon", Sense.Taste)]
    [InlineData("velvet", Sense.Touch)]
    [InlineData("smoke", Sense.Smell)]
    [InlineData("lantern", Sense.Sight)]
    [InlineData("ache", Sense.Body)]
    public void Lexicon_WordsEvokeTheirSense(string word, Sense sense) =>
        Assert.Contains(sense, SensoryLexicon.SensesOf(Bundled.Value.Lookup(word)!));

    [Fact]
    public void Lexicon_KeyIsSeenMoreThanTouched() =>
        Assert.DoesNotContain(Sense.Touch, SensoryLexicon.SensesOf(Bundled.Value.Lookup("key")!));

    [Fact]
    public void Profile_CountsNarrationOnly()
    {
        var doc = SceneDocumentBuilder.FromPlainText("“The kettle shrieked,” she said. The bell rang.");
        var profile = SensoryAnalyzer.Profile(doc, Bundled.Value);
        Assert.Contains("bell", profile.Examples[(int)Sense.Sound]);
        Assert.DoesNotContain("kettle", profile.Examples[(int)Sense.Sound]);
    }

    // ── Sensory balance ──────────────────────────────────────────────────────

    private const string Visual =
        "The grey light lay across the white wall and the red door. She stood by the window and the pale sky " +
        "turned gold above the dark roofs, and a blue lamp glowed in the yellow glass of the tower.";

    private const string Rich =
        "The kettle shrieked on the stove and rain hammered the tin roof. The kitchen smelled of smoke and " +
        "oranges, the floor was gritty under her bare feet, and the tea tasted bitter and sweet at once.";

    [Fact]
    public void Balance_AllVisualScene_IsFlagged()
    {
        var note = Assert.Single(Run(new SensoryBalanceRule(), Repeat(Visual, 10)));
        Assert.Contains("almost all sight", note.Message);
        Assert.Contains("nothing heard, touched, smelled or tasted", note.Message);
    }

    [Fact]
    public void Balance_SceneWithOtherSenses_IsQuiet() =>
        Assert.Empty(Run(new SensoryBalanceRule(), Repeat(Visual + " " + Rich, 6)));

    [Fact]
    public void Balance_ShortScene_IsSkipped() =>
        Assert.Empty(Run(new SensoryBalanceRule(), Repeat(Visual, 3)));

    // ── Abstract passages ────────────────────────────────────────────────────

    private const string Abstract =
        "It was a matter of principle, she decided, and principles were the only thing that had ever really " +
        "mattered to her. The situation was complicated, of course, and there were considerations on every side, " +
        "but in the end the question was simply whether she believed in fairness or merely in convenience.";

    [Fact]
    public void Abstract_AbstractParagraph_IsFlaggedWithItsWords()
    {
        var note = Assert.Single(Run(new AbstractPassageRule(), Abstract));
        Assert.Contains("abstract paragraph", note.Message);
        Assert.NotEmpty(note.ExtraSpans);
    }

    [Fact]
    public void Abstract_ConcreteParagraph_IsQuiet() =>
        Assert.Empty(Run(new AbstractPassageRule(), Rich + " " + Visual));

    [Fact]
    public void Abstract_DialogueIsIgnored() =>
        Assert.Empty(Run(new AbstractPassageRule(), "“" + Abstract + "”"));

    // ── Tension arc ──────────────────────────────────────────────────────────

    /// <summary>One scene per tension level, in reading order.</summary>
    private static List<SceneAnalysisResult> Arc(params double[] levels) =>
        levels.Select((t, i) => new SceneAnalysisResult
        {
            SceneId = $"s{i}", SceneTitle = $"Scene {i + 1}", ChapterTitle = $"Chapter {i + 1}",
            Tension = new TensionSignals
            {
                Words = 1000, DangerRate = t, ShortSentenceShare = t / 10, ActionShare = t / 10, HeatedDialogue = t / 20
            }
        }).ToList();

    private static List<NlpNote> RunBook(IBookRule rule, List<SceneAnalysisResult> results) =>
        rule.Evaluate(new BookRuleContext { Results = results }).ToList();

    [Fact]
    public void Arc_ScoresRelativeToTheBook()
    {
        var arc = TensionArc.Compute(Arc(1, 2, 3, 4, 5));
        Assert.Equal(0, arc[0].Tension);
        Assert.Equal(100, arc[^1].Tension);
        Assert.All(arc, p => Assert.Null(p.Mood));   // no emotion model
    }

    [Fact]
    public void Arc_SkipsStubScenes()
    {
        var results = Arc(1, 2, 3);
        results[1].Tension.Words = 40;
        Assert.Equal(2, TensionArc.Compute(results).Count);
    }

    [Fact]
    public void Sag_QuietRunInTheMiddle_IsFlagged()
    {
        var note = Assert.Single(RunBook(new TensionSagRule(), Arc(5, 6, 5, 1, 1, 1, 1, 1, 6, 7, 8, 9)));
        Assert.Equal("s3", note.SceneId);
        Assert.Contains("5 scenes in a row", note.Message);
    }

    [Fact]
    public void Sag_RisingBook_IsQuiet() =>
        Assert.Empty(RunBook(new TensionSagRule(), Arc(2, 4, 3, 5, 4, 6, 5, 7, 6, 8, 9, 10)));

    [Fact]
    public void Climax_EarlyPeak_IsFlagged()
    {
        var note = Assert.Single(RunBook(new ClimaxPlacementRule(), Arc(3, 9, 10, 9, 4, 3, 4, 3, 4, 3, 4, 5)));
        Assert.Contains("most intense stretch", note.Message);
    }

    [Fact]
    public void Climax_LatePeak_IsQuiet() =>
        Assert.Empty(RunBook(new ClimaxPlacementRule(), Arc(2, 3, 2, 4, 3, 5, 4, 6, 7, 9, 10, 6)));

    [Fact]
    public void Signals_DangerAndShortSentences()
    {
        var calm = SceneDocumentBuilder.FromPlainText(
            "The afternoon was long and warm and the garden was full of bees and the slow sound of the river beyond the wall.");
        var tense = SceneDocumentBuilder.FromPlainText(
            "He ran. The gun fired. Blood on the stairs. She screamed and fled into the dark.");
        var a = TensionAnalyzer.Signals(calm, BeatClassifier.Classify(calm));
        var b = TensionAnalyzer.Signals(tense, BeatClassifier.Classify(tense));
        Assert.True(b.DangerRate > a.DangerRate);
        Assert.True(b.ShortSentenceShare > a.ShortSentenceShare);
    }
}
