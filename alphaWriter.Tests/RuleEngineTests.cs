using System.Diagnostics;
using alphaWriter.Models;
using alphaWriter.Models.Analysis;
using alphaWriter.Services.Nlp;
using alphaWriter.Services.Nlp.Parsing;
using alphaWriter.Services.Nlp.Rules;
using Xunit;

namespace alphaWriter.Tests;

/// <summary>
/// Engine plumbing: anchoring, fingerprints, re-anchoring after edits, incremental
/// reuse, the live tier, and persistence.
/// </summary>
public class RuleEngineTests
{
    private const string SampleScene =
        "<p>She walked quickly and quietly to the gate. Maya felt angry. The dog barked.</p>" +
        "<p>He was walking behind her, walking slowly. The walk was long.</p>" +
        "<p>“Stop,” she said. Maya felt afraid of the dark road ahead.</p>";

    private static (SceneDocument Doc, List<NlpNote> Notes) Analyze(string html, Book? book = null)
    {
        var doc = SceneDocumentBuilder.FromHtml(html);
        var ctx = new SceneRuleContext
        {
            Document = doc,
            SceneId = "s1",
            SceneTitle = "Scene",
            ChapterTitle = "Ch1",
            Book = book,
            Style = new StyleAnalyzer().Analyze(doc.Sentences.Select(s => s.Text).ToList())
        };
        return (doc, ProseRuleEngine.CreateDefault().EvaluateScene(ctx));
    }

    // ── Anchors & spans ──────────────────────────────────────────────────────

    [Fact]
    public void EverySpan_MatchesItsEvidence_AndItsAnchor()
    {
        var (doc, notes) = Analyze(SampleScene);
        Assert.NotEmpty(notes);

        foreach (var n in notes.Where(n => n.HasSpan))
        {
            Assert.Equal(n.Evidence, doc.Text.Substring(n.Start!.Value, n.Length!.Value));
            Assert.Equal(n.Evidence, n.AnchorText.Substring(n.AnchorOffset!.Value, n.Length.Value));
            foreach (var extra in n.ExtraSpans)
                Assert.Equal(doc.Text.Substring(extra.Start, extra.Length),
                    extra.AnchorText.Substring(extra.AnchorOffset, extra.Length));
        }
    }

    [Fact]
    public void EveryNote_HasRuleIdAndFingerprint()
    {
        var (_, notes) = Analyze(SampleScene);
        Assert.All(notes, n =>
        {
            Assert.NotNull(RuleCatalog.Find(n.RuleId));
            Assert.False(string.IsNullOrEmpty(n.Fingerprint));
        });
    }

    [Fact]
    public void Fingerprint_SurvivesEditsElsewhereInTheScene()
    {
        var (_, before) = Analyze(SampleScene);
        var (_, after) = Analyze("<p>A brand new opening paragraph.</p>" + SampleScene);

        var tell = before.First(n => n.RuleId == "style.show-dont-tell");
        Assert.Contains(after, n => n.Fingerprint == tell.Fingerprint);
    }

    [Fact]
    public void Reanchor_FollowsTheSentenceAfterInsertion()
    {
        var (_, notes) = Analyze(SampleScene);
        var tell = notes.First(n => n.RuleId == "style.show-dont-tell");
        int oldStart = tell.Start!.Value;

        var edited = SceneDocumentBuilder.FromHtml("<p>Inserted first.</p>" + SampleScene);
        Assert.True(NoteAnchoring.Reanchor(tell, edited));
        Assert.Equal(oldStart + "Inserted first.\n".Length, tell.Start);
        Assert.Equal(tell.Evidence, edited.Text.Substring(tell.Start!.Value, tell.Length!.Value));
    }

    [Fact]
    public void Reanchor_FailsWhenTheSentenceWasRewritten()
    {
        var (_, notes) = Analyze(SampleScene);
        var tell = notes.First(n => n.RuleId == "style.show-dont-tell" && n.Evidence == "felt angry");

        var edited = SceneDocumentBuilder.FromHtml(SampleScene.Replace("Maya felt angry.", "Maya's jaw tightened."));
        Assert.False(NoteAnchoring.Reanchor(tell, edited));
    }

    // ── Style sheet ──────────────────────────────────────────────────────────

    [Fact]
    public void StyleSheet_RuleCanBeDisabledPerScene()
    {
        var sheet = new EditorialStyleSheet();
        sheet.DisableRuleForScene("style.echo", "s1");

        Assert.False(sheet.IsRuleEnabled("style.echo", "s1"));
        Assert.True(sheet.IsRuleEnabled("style.echo", "s2"));
        Assert.True(sheet.IsRuleEnabled("style.adverbs", "s1"));
    }

    [Fact]
    public void StyleSheet_PersistsWithBook()
    {
        var book = new Book { Title = "B" };
        book.StyleSheet.Genre = "Literary";
        book.StyleSheet.DismissedFingerprints.Add("abc");
        book.StyleSheet.DisabledRuleIds.Add("style.echo");

        var json = System.Text.Json.JsonSerializer.Serialize(book);
        var back = System.Text.Json.JsonSerializer.Deserialize<Book>(json)!;

        Assert.Equal("Literary", back.StyleSheet.Genre);
        Assert.Contains("abc", back.StyleSheet.DismissedFingerprints);
        Assert.False(back.StyleSheet.IsRuleEnabled("style.echo"));
    }

    [Fact]
    public void StyleSheet_MissingFromOldBookJson_DefaultsCleanly()
    {
        var back = System.Text.Json.JsonSerializer.Deserialize<Book>("""{"Title":"Old"}""")!;
        Assert.NotNull(back.StyleSheet);
        Assert.Equal(EditorialStyleSheet.DefaultGenre, back.StyleSheet.Genre);
    }

    // ── Service: live tier & incremental reuse ───────────────────────────────

    private static Book BookWith(params string[] sceneHtml)
    {
        var book = new Book { Title = "B" };
        var chapter = new Chapter { Title = "Ch1" };
        for (int i = 0; i < sceneHtml.Length; i++)
            chapter.Scenes.Add(new Scene { Title = $"Scene {i + 1}", Status = SceneStatus.Draft, Content = sceneHtml[i] });
        book.Chapters.Add(chapter);
        return book;
    }

    [Fact]
    public void AnalyzeSceneLive_ReturnsLexicalNotesAndCoverage()
    {
        var service = new NlpAnalysisService(new StyleAnalyzer(), new PacingAnalyzer());
        var book = BookWith(SampleScene);
        var chapter = book.Chapters[0];

        var live = service.AnalyzeSceneLive(chapter.Scenes[0], chapter, book);

        Assert.Contains(live.Result.Notes, n => n.RuleId == "style.show-dont-tell");
        Assert.Contains("style.echo", live.CoveredRuleIds);
        Assert.DoesNotContain("style.passive", live.CoveredRuleIds); // no tagger loaded
    }

    [Fact]
    public void AnalyzeSceneLive_IsFastOnAFullLengthScene()
    {
        var paragraph = "<p>She walked quickly to the old gate, and the wind moved through the trees. " +
                        "Maya felt angry, but she said nothing. “Where are you going?” he asked softly. " +
                        "The lantern swung, throwing light across the wet stones of the courtyard.</p>";
        var html = string.Concat(Enumerable.Repeat(paragraph, 70)); // ~3,000 words
        var service = new NlpAnalysisService(new StyleAnalyzer(), new PacingAnalyzer());
        var book = BookWith(html);
        var chapter = book.Chapters[0];
        Assert.InRange(chapter.Scenes[0].WordCount, 2800, 3600);

        service.AnalyzeSceneLive(chapter.Scenes[0], chapter, book); // warm-up (JIT, regex)
        var sw = Stopwatch.StartNew();
        service.AnalyzeSceneLive(chapter.Scenes[0], chapter, book);
        sw.Stop();

        Assert.True(sw.ElapsedMilliseconds < 150, $"Live analysis took {sw.ElapsedMilliseconds} ms");
    }

    [Fact]
    public async Task AnalyzeBook_ReusesUnchangedScenes()
    {
        var service = new NlpAnalysisService(new StyleAnalyzer(), new PacingAnalyzer());
        var book = BookWith(SampleScene, "<p>A quiet second scene. Nothing much happens here.</p>");

        var first = await service.AnalyzeBookAsync(book);
        var messages = new List<string>();
        var second = await service.AnalyzeBookAsync(book, new SyncProgress(messages), default, first.Results);

        Assert.Contains(messages, m => m.Contains("2 unchanged scenes reused"));
        Assert.Equal(
            first.Notes.Select(n => n.Fingerprint).OrderBy(f => f),
            second.Notes.Select(n => n.Fingerprint).OrderBy(f => f));
    }

    [Fact]
    public async Task AnalyzeBook_ReanalyzesChangedScenes()
    {
        var service = new NlpAnalysisService(new StyleAnalyzer(), new PacingAnalyzer());
        var book = BookWith(SampleScene, "<p>A quiet second scene.</p>");
        var first = await service.AnalyzeBookAsync(book);

        book.Chapters[0].Scenes[1].Content = "<p>She felt nervous. A quiet second scene.</p>";
        var messages = new List<string>();
        var second = await service.AnalyzeBookAsync(book, new SyncProgress(messages), default, first.Results);

        Assert.Contains(messages, m => m.Contains("1 unchanged scene reused"));
        Assert.Contains(second.Notes, n => n.SceneTitle == "Scene 2" && n.RuleId == "style.show-dont-tell");
    }

    [Fact]
    public async Task AnalyzeBook_NotesAreAllIdentified()
    {
        var service = new NlpAnalysisService(new StyleAnalyzer(), new PacingAnalyzer());
        var (notes, _, _, _) = await service.AnalyzeBookAsync(BookWith(SampleScene, SampleScene, "<p>Short.</p>"));
        Assert.All(notes, n =>
        {
            Assert.False(string.IsNullOrEmpty(n.RuleId));
            Assert.False(string.IsNullOrEmpty(n.Fingerprint));
        });
    }

    // ── Persistence ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Cache_RoundTripsEmotionsAndSpans()
    {
        var dir = Path.Combine(Path.GetTempPath(), "aw-cache-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            var cache = new NlpCacheService(dir);
            var data = new PersistedAnalysisData
            {
                Results =
                [
                    new SceneAnalysisResult
                    {
                        SceneId = "s1",
                        Sentences = [new SentenceAnalysis { Text = "Hi.", Emotions = [(EmotionLabel.Joy, 0.8f)] }]
                    }
                ],
                Notes = [new NlpNote { RuleId = "style.echo", Start = 3, Length = 4, Evidence = "walk", Fingerprint = "f" }]
            };

            await cache.SaveAnalysisResultsAsync("b1", data);
            var back = cache.LoadAnalysisResults("b1")!;

            Assert.Equal((EmotionLabel.Joy, 0.8f), back.Results[0].Sentences[0].Emotions[0]);
            Assert.Equal(3, back.Notes[0].Start);
            Assert.Equal("style.echo", back.Notes[0].RuleId);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Cache_DiscardsOlderFormat()
    {
        var dir = Path.Combine(Path.GetTempPath(), "aw-cache-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            Directory.CreateDirectory(Path.Combine(dir, "b1"));
            File.WriteAllText(Path.Combine(dir, "b1", "analysis-results.json"),
                """{"AnalyzedAtUtc":"2026-01-01T00:00:00Z","Results":[],"Notes":[]}""");
            Assert.Null(new NlpCacheService(dir).LoadAnalysisResults("b1"));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    /// <summary>IProgress that records synchronously (Progress&lt;T&gt; posts asynchronously).</summary>
    private sealed class SyncProgress(List<string> sink) : IProgress<string>
    {
        public void Report(string value) => sink.Add(value);
    }
}
