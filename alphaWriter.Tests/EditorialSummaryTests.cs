using alphaWriter.Models;
using alphaWriter.Models.Analysis;
using alphaWriter.Services.Nlp;
using alphaWriter.Services.Nlp.Rules;
using alphaWriter.ViewModels;
using Xunit;

namespace alphaWriter.Tests;

/// <summary>Phase 3: scene report cards, genre guidelines and the exported summary.</summary>
public class EditorialSummaryTests
{
    private static SceneAnalysisResult Result(string id, string chapter, int words, int sentences,
        double dialogue = 0.3, double ease = 70, double tension = 1) => new()
    {
        SceneId = id, SceneTitle = $"Scene {id}", ChapterTitle = chapter,
        Style = new StyleProfile { TotalWords = words, TotalSentences = sentences, DialogueRatio = dialogue, ReadabilityScore = ease },
        Tension = new TensionSignals { Words = words, DangerRate = tension }
    };

    // ── Genre guidelines ─────────────────────────────────────────────────────

    [Fact]
    public void Metrics_AreWeightedAcrossScenes()
    {
        var results = new List<SceneAnalysisResult>
        {
            Result("a", "One", 3000, 200, dialogue: 0.5),
            Result("b", "One", 1000, 50, dialogue: 0.1),
            Result("c", "Two", 2000, 150, dialogue: 0.2),
        };
        var m = BookMetrics.Compute(new Book(), results);
        Assert.Equal(6.0, m["book-words"]);
        Assert.Equal(3000, m["chapter-words"]);
        Assert.Equal(6000.0 / 400, m["sentence-words"], 3);
        Assert.Equal((100 + 5 + 30) / 400.0, m["dialogue-share"], 3);
    }

    [Fact]
    public void Compare_GivesVerdictsAgainstTheGenre()
    {
        // 3 chapters of ~1,000 words with 25-word sentences: a short book of long sentences
        var results = Enumerable.Range(0, 3).Select(i => Result($"s{i}", $"Ch{i}", 1000, 40)).ToList();
        var rows = GenreBenchmarks.Compare("Thriller", new Book(), results).ToDictionary(r => r.Benchmark.Id);

        Assert.Equal(BenchmarkVerdict.Below, rows["book-words"].Verdict);
        Assert.Equal(BenchmarkVerdict.Below, rows["chapter-words"].Verdict);
        Assert.Equal(BenchmarkVerdict.Above, rows["sentence-words"].Verdict);
        Assert.Equal(BenchmarkVerdict.Within, rows["dialogue-share"].Verdict);
        Assert.Equal("9–15", rows["sentence-words"].RangeText);
        Assert.Equal("30%", rows["dialogue-share"].ValueText);
    }

    [Fact]
    public void Compare_UnknownGenreFallsBackToGeneral() =>
        Assert.Equal(GenreBenchmarks.For("General").Select(b => b.Max), GenreBenchmarks.For("Westerns").Select(b => b.Max));

    [Fact]
    public void Compare_NothingAnalyzed_IsEmpty() =>
        Assert.Empty(GenreBenchmarks.Compare("General", new Book(), []));

    // ── Scene report card ────────────────────────────────────────────────────

    [Fact]
    public void Card_MergesRunsOfBeatsAndSummarizes()
    {
        var r = Result("a", "One", 500, 30);
        r.ParagraphBeats = ["Dialogue", "Dialogue", "Action", "Dialogue", "Exposition"];
        var card = SceneReportCard.Build(r, [], []);

        Assert.Equal(["Dialogue", "Action", "Dialogue", "Exposition"], card.Beats.Select(b => b.Label));
        Assert.Equal(2, card.Beats[0].Weight);
        Assert.Equal("3 dialogue, 1 action, 1 exposition", card.BeatSummary);
        Assert.Equal("No notes", card.NoteSummary);
        Assert.False(card.HasTension);   // no arc
    }

    [Fact]
    public void Card_DescribesTheSenses()
    {
        var r = Result("a", "One", 500, 30);
        r.Senses = new SensoryProfile { Counts = [3, 0, 2, 0, 0, 15], Concreteness = 3.4 };
        var card = SceneReportCard.Build(r, [], []);
        Assert.Equal("Mostly sight; some sound and touch · concreteness 3.4/5", card.SenseSummary);
        Assert.Equal(3, card.Senses.Count);
    }

    [Fact]
    public void Card_PlacesTensionWithinTheBook()
    {
        var results = Enumerable.Range(0, 6).Select(i => Result($"s{i}", "One", 500, 30, tension: i)).ToList();
        var arc = TensionArc.Compute(results);
        Assert.Contains("most tense", SceneReportCard.Build(results[5], arc, []).TensionSummary);
        Assert.Contains("quietest", SceneReportCard.Build(results[0], arc, []).TensionSummary);
        Assert.Contains("mid-range", SceneReportCard.Build(results[3], arc, []).TensionSummary);
    }

    [Fact]
    public void Card_CountsNotesByEditor()
    {
        var r = Result("a", "One", 500, 30);
        var notes = new[]
        {
            new NlpNote { SceneId = "a", Category = NlpNoteCategory.LineEditor },
            new NlpNote { SceneId = "a", Category = NlpNoteCategory.LineEditor },
            new NlpNote { SceneId = "a", Category = NlpNoteCategory.CopyEditor },
            new NlpNote { SceneId = "other", Category = NlpNoteCategory.CopyEditor },
        };
        Assert.Equal("1 copy · 2 line", SceneReportCard.Build(r, [], notes).NoteSummary);
    }

    // ── Exported summary ─────────────────────────────────────────────────────

    [Fact]
    public void Summary_HasTheMainSectionsAndEscapesText()
    {
        var book = new Book { Title = "Salt & <Smoke>" };
        book.AnalysisHistory.Add(new AnalysisSnapshot { Date = new DateTime(2026, 10, 1), Words = 4000, Total = 9, ByRule = { ["style.show-dont-tell"] = 4 } });
        book.AnalysisHistory.Add(new AnalysisSnapshot { Date = new DateTime(2026, 10, 9), Words = 4100, Total = 5, ByRule = { ["style.show-dont-tell"] = 2 } });
        var results = Enumerable.Range(0, 4).Select(i => Result($"s{i}", $"Chapter {i / 2 + 1}", 1000, 70, tension: i % 3)).ToList();
        var notes = new List<NlpNote>
        {
            new() { RuleId = "style.show-dont-tell", SceneId = "s0", Category = NlpNoteCategory.LineEditor, Message = "named" },
            new() { RuleId = "style.show-dont-tell", SceneId = "s1", Category = NlpNoteCategory.LineEditor, Message = "named" },
            new() { RuleId = "structure.tension-sag", SceneId = "s2", SceneTitle = "Scene s2", ChapterTitle = "Chapter 2",
                    Category = NlpNoteCategory.DevelopmentalEditor, Message = "Quiet <middle>" },
        };

        var html = EditorialSummaryWriter.BuildHtml(book, results, notes, new DateTime(2026, 10, 20));

        Assert.Contains("Salt &amp; &lt;Smoke&gt;", html);
        Assert.DoesNotContain("<Smoke>", html);
        Assert.Contains("<h2>Progress</h2>", html);
        Assert.Contains("Named emotions", html);
        Assert.Contains("(was 4)", html.Replace("<span class=\"muted\">", ""));
        Assert.Contains("Quiet &lt;middle&gt;", html);
        Assert.Contains("<h2>Tension across the book</h2>", html);
        Assert.Contains("Against General guidelines", html);
        Assert.Contains("Chapter by chapter", html);
        Assert.Contains("20 October 2026", html);
    }

    [Theory]
    [InlineData("My: Book?", "My- Book- - editorial summary 2026-10-20.html")]
    [InlineData("", "Book - editorial summary 2026-10-20.html")]
    public void SummaryFileName_IsSafe(string title, string expected) =>
        Assert.Equal(expected, WriterViewModel.SummaryFileName(title, new DateTime(2026, 10, 20)));
}
