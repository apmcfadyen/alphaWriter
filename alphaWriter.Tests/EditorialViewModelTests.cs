using System.Reflection;
using System.Text.Json;
using alphaWriter.Models;
using alphaWriter.Models.Analysis;
using alphaWriter.Services;
using alphaWriter.Services.Nlp;
using alphaWriter.ViewModels;
using Moq;
using Xunit;

namespace alphaWriter.Tests;

/// <summary>
/// View-model side of the editorial suite: visibility filtering, note actions,
/// merging live results, and the payload sent to the editor's highlight layer.
/// Backing fields are set by reflection to avoid MAUI Preferences side effects.
/// </summary>
public class EditorialViewModelTests
{
    private static WriterViewModel CreateViewModel(Book book, List<NlpNote> notes)
    {
        var bookService = new Mock<IBookService>();
        bookService.Setup(s => s.SaveBooksAsync(It.IsAny<List<Book>>())).Returns(Task.CompletedTask);

        var vm = new WriterViewModel(bookService.Object, new Mock<IImageService>().Object,
            new Mock<INlpAnalysisService>().Object, new Mock<INlpModelManager>().Object,
            new Mock<INerService>().Object, new Mock<IPosTaggingService>().Object,
            new Mock<ILocationHeuristicService>().Object, new Mock<INlpCacheService>().Object);

        SetField(vm, "selectedBook", book);
        SetField(vm, "_allNotes", notes);
        return vm;
    }

    private static void SetField(WriterViewModel vm, string name, object? value)
    {
        var field = typeof(WriterViewModel).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(field);
        field!.SetValue(vm, value);
    }

    private static List<NlpNote> GetNotes(WriterViewModel vm) =>
        (List<NlpNote>)typeof(WriterViewModel)
            .GetField("_allNotes", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(vm)!;

    private static NlpNote Note(string ruleId, string fp, string evidence = "", string sceneId = "s1",
        string anchor = "", int? offset = null) => new()
    {
        RuleId = ruleId,
        Fingerprint = fp,
        Evidence = evidence,
        SceneId = sceneId,
        AnchorText = anchor,
        AnchorOffset = offset,
        Start = offset,
        Length = offset.HasValue ? evidence.Length : null,
        Category = NlpNoteCategory.CopyEditor,
        Message = "msg"
    };

    // ── Visibility ───────────────────────────────────────────────────────────

    [Fact]
    public void IsNoteVisible_RespectsStyleSheet()
    {
        var book = new Book();
        book.StyleSheet.DisabledRuleIds.Add("style.passive");
        book.StyleSheet.DismissedFingerprints.Add("gone");
        book.StyleSheet.AllowedWords.Add("aether");
        book.StyleSheet.DisableRuleForScene("style.echo", "s1");
        var vm = CreateViewModel(book, []);

        Assert.True(vm.IsNoteVisible(Note("style.adverbs", "a")));
        Assert.False(vm.IsNoteVisible(Note("style.passive", "b")));
        Assert.False(vm.IsNoteVisible(Note("style.adverbs", "gone")));
        Assert.False(vm.IsNoteVisible(Note("style.echo", "c", "aether", sceneId: "s2")));
        Assert.False(vm.IsNoteVisible(Note("style.echo", "d", "walk", sceneId: "s1")));
        Assert.True(vm.IsNoteVisible(Note("style.echo", "e", "walk", sceneId: "s2")));
    }

    [Fact]
    public void ApplyNoteAction_Dismiss_HidesNoteAndRecordsFingerprint()
    {
        var book = new Book();
        var note = Note("style.adverbs", "fp1");
        var vm = CreateViewModel(book, [note]);

        vm.ApplyNoteAction("dismiss", "fp1", "style.adverbs", "s1", null);

        Assert.Contains("fp1", book.StyleSheet.DismissedFingerprints);
        Assert.Empty(vm.NlpNotes);
        Assert.Equal(1, vm.DismissedCount);
    }

    [Fact]
    public void ApplyNoteAction_AllowWord_AndDisableRule()
    {
        var book = new Book();
        var vm = CreateViewModel(book, []);

        vm.ApplyNoteAction("allow-word", null, null, null, "Kaelen");
        vm.ApplyNoteAction("disable-rule", null, "style.echo", null, null);
        vm.ApplyNoteAction("disable-scene", null, "style.adverbs", "s9", null);

        Assert.True(book.StyleSheet.IsWordAllowed("kaelen"));
        Assert.False(book.StyleSheet.IsRuleEnabled("style.echo"));
        Assert.False(book.StyleSheet.IsRuleEnabled("style.adverbs", "s9"));
    }

    [Fact]
    public void RestoreDismissed_BringsNotesBack()
    {
        var book = new Book();
        book.StyleSheet.DismissedFingerprints.Add("fp1");
        var vm = CreateViewModel(book, [Note("style.adverbs", "fp1")]);

        vm.RestoreDismissedCommand.Execute(null);

        Assert.Empty(book.StyleSheet.DismissedFingerprints);
        Assert.Single(vm.NlpNotes);
    }

    [Fact]
    public void RuleToggle_UpdatesStyleSheet()
    {
        var book = new Book();
        var vm = CreateViewModel(book, []);
        vm.RefreshEditorialSettings();

        var echo = vm.RuleSettings.Single(r => r.Id == "style.echo");
        echo.IsEnabled = false;
        Assert.Contains("style.echo", book.StyleSheet.DisabledRuleIds);

        echo.IsEnabled = true;
        Assert.DoesNotContain("style.echo", book.StyleSheet.DisabledRuleIds);
    }

    // ── Live merge ───────────────────────────────────────────────────────────

    [Fact]
    public void ApplyLiveAnalysis_ReplacesCoveredRules_AndReanchorsTheRest()
    {
        const string oldText = "She was seen. He left.";
        var passive = Note("style.passive", "p", "was seen", anchor: "She was seen.", offset: 4);
        var oldEcho = Note("style.echo", "old-echo");
        var otherScene = Note("style.echo", "x", sceneId: "s2");
        var vm = CreateViewModel(new Book(), [passive, oldEcho, otherScene]);

        var service = new NlpAnalysisService(new StyleAnalyzer(), new PacingAnalyzer());
        var book = new Book();
        var chapter = new Chapter { Title = "Ch" };
        var scene = new Scene { Id = "s1", Title = "S", Status = SceneStatus.Draft,
                                Content = "<p>A new first line.</p><p>" + oldText + "</p>" };
        chapter.Scenes.Add(scene);
        book.Chapters.Add(chapter);
        var live = service.AnalyzeSceneLive(scene, chapter, book);

        vm.ApplyLiveAnalysis("s1", live);
        var notes = GetNotes(vm);

        Assert.DoesNotContain(notes, n => n.Fingerprint == "old-echo");   // lexical rule re-ran
        Assert.Contains(notes, n => n.Fingerprint == "x");                // other scene untouched
        var moved = Assert.Single(notes, n => n.Fingerprint == "p");      // POS note kept...
        Assert.Equal("was seen", live.Document.Text.Substring(moved.Start!.Value, moved.Length!.Value)); // ...and moved
    }

    [Fact]
    public void ApplyLiveAnalysis_DropsNotesWhoseSentenceWasRewritten()
    {
        var passive = Note("style.passive", "p", "was seen", anchor: "She was seen.", offset: 4);
        var vm = CreateViewModel(new Book(), [passive]);

        var service = new NlpAnalysisService(new StyleAnalyzer(), new PacingAnalyzer());
        var book = new Book();
        var chapter = new Chapter { Title = "Ch" };
        var scene = new Scene { Id = "s1", Title = "S", Status = SceneStatus.Draft, Content = "<p>Tom saw her.</p>" };
        chapter.Scenes.Add(scene);
        book.Chapters.Add(chapter);

        vm.ApplyLiveAnalysis("s1", service.AnalyzeSceneLive(scene, chapter, book));
        Assert.DoesNotContain(GetNotes(vm), n => n.Fingerprint == "p");
    }

    // ── Annotation payload ───────────────────────────────────────────────────

    [Fact]
    public void BuildAnnotationsJson_IncludesOnlyVisibleAnchoredNotesForTheScene()
    {
        var book = new Book();
        book.StyleSheet.DismissedFingerprints.Add("hidden");
        var echo = Note("style.echo", "e", "walked", anchor: "She walked home.", offset: 4);
        echo.ExtraSpans.Add(new TextSpan(30, 7) { AnchorText = "He was walking.", AnchorOffset = 7 });
        var vm = CreateViewModel(book,
        [
            echo,
            Note("style.adverbs", "hidden", "softly", anchor: "She spoke softly.", offset: 10),
            Note("style.adverb-density", "scene-level"),                        // no anchor
            Note("style.echo", "elsewhere", "x", sceneId: "s2", anchor: "X.", offset: 0)
        ]);

        using var doc = JsonDocument.Parse(vm.BuildAnnotationsJson("s1"));
        var notes = doc.RootElement.GetProperty("notes");
        var spans = doc.RootElement.GetProperty("spans");

        Assert.Equal(1, notes.GetArrayLength());
        Assert.Equal("Word echoes", notes[0].GetProperty("title").GetString());
        Assert.Equal(2, spans.GetArrayLength());
        Assert.True(spans[0].GetProperty("primary").GetBoolean());
        Assert.Equal(4, spans[0].GetProperty("offset").GetInt32());
        Assert.Equal(7, spans[1].GetProperty("offset").GetInt32());
    }
}
