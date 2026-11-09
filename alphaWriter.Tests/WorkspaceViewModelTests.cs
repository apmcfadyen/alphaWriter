using System.Reflection;
using alphaWriter.Models;
using alphaWriter.Models.Analysis;
using alphaWriter.Services;
using alphaWriter.Services.Nlp;
using alphaWriter.ViewModels;
using Moq;
using Xunit;

namespace alphaWriter.Tests;

/// <summary>
/// Workspaces (what the main area shows), the Write outline, and status-bar state.
/// SelectedBook is injected by reflection to avoid the Preferences call in its setter.
/// </summary>
public class WorkspaceViewModelTests
{
    private static WriterViewModel CreateViewModel(Book? book = null)
    {
        var bookService = new Mock<IBookService>();
        bookService.Setup(s => s.SaveBooksAsync(It.IsAny<List<Book>>())).Returns(Task.CompletedTask);

        var vm = new WriterViewModel(bookService.Object, new Mock<IImageService>().Object,
            new Mock<INlpAnalysisService>().Object, new Mock<INlpModelManager>().Object,
            new Mock<INerService>().Object, new Mock<IPosTaggingService>().Object,
            new Mock<ILocationHeuristicService>().Object, new Mock<INlpCacheService>().Object);

        if (book is not null)
        {
            vm.Books.Add(book);
            typeof(WriterViewModel).GetField("selectedBook", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(vm, book);
            typeof(WriterViewModel).GetMethod("AttachOutline", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(vm, [book]);
        }
        return vm;
    }

    private static (WriterViewModel Vm, List<string> Loads) ReportsViewModel()
    {
        var vm = CreateViewModel(SampleBook());
        typeof(WriterViewModel).GetField("_analysisResults", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(vm, new List<SceneAnalysisResult> { new() });
        var loads = new List<string>();
        vm.ReportsPanelShown += () => loads.Add(vm.ActiveReportType);
        return (vm, loads);
    }

    [Fact]
    public void Reports_SwitchingBetweenThem_ReloadsTheWebView()
    {
        var (vm, loads) = ReportsViewModel();
        vm.OpenInsightCommand.Execute("report:Entities");
        vm.OpenInsightCommand.Execute("report:NLP");
        vm.OpenInsightCommand.Execute("report:Entities");

        Assert.Equal(["Entities", "NLP", "Entities"], loads);
        Assert.Equal("Insights — story map", vm.ReportPanelTitle);
    }

    [Fact]
    public void Reports_OpeningFromElsewhere_LoadsOnce()
    {
        var (vm, loads) = ReportsViewModel();
        vm.OpenInsightCommand.Execute("Frequency");
        loads.Clear();

        vm.OpenInsightCommand.Execute("report:NLP");   // from a statistics view in Insights
        Assert.Equal(["NLP"], loads);

        vm.OpenInsightCommand.Execute("report:NLP");   // already showing: nothing to reload
        Assert.Equal(["NLP"], loads);
    }

    [Fact]
    public void Reports_ChangingTypeWhileHidden_DoesNotLoad()
    {
        var (vm, loads) = ReportsViewModel();
        vm.ActiveReportType = "NLP";   // Write workspace: no report on screen
        Assert.Empty(loads);
    }

    private static Book SampleBook()
    {
        var book = new Book { Title = "B" };
        var ch1 = new Chapter { Title = "One" };
        ch1.Scenes.Add(new Scene { Title = "1a" });
        ch1.Scenes.Add(new Scene { Title = "1b" });
        var ch2 = new Chapter { Title = "Two" };
        ch2.Scenes.Add(new Scene { Title = "2a" });
        book.Chapters.Add(ch1);
        book.Chapters.Add(ch2);
        book.Characters.Add(new Character { Name = "Maya" });
        return book;
    }

    // ── Workspaces ───────────────────────────────────────────────────────────

    [Fact]
    public void Default_IsWriteWithEditor()
    {
        var vm = CreateViewModel();
        Assert.Equal(Workspace.Write, vm.ActiveWorkspace);
        Assert.True(vm.IsSceneEditorVisible);
        Assert.True(vm.IsSidebarHostVisible);
        Assert.False(vm.IsStatsPanelVisible);
    }

    [Fact]
    public void ExactlyOneMainPanelIsVisiblePerWorkspace()
    {
        var vm = CreateViewModel(SampleBook());
        foreach (var ws in Enum.GetValues<Workspace>())
        {
            vm.SetWorkspaceCommand.Execute(ws.ToString());
            int visible = new[]
            {
                vm.IsSceneEditorVisible, vm.IsBookInfoVisible, vm.IsStatsPanelVisible,
                vm.IsReportsPanelVisible, vm.IsElementEditorVisible, vm.IsStoryBibleEmpty
            }.Count(v => v);
            Assert.True(visible == 1, $"{ws}: {visible} main panels visible");
        }
    }

    [Fact]
    public void BookWorkspace_ShowsBookInfo_WithoutSidebar()
    {
        var vm = CreateViewModel(SampleBook());
        vm.ShowBookInfoCommand.Execute(null);

        Assert.Equal(Workspace.Book, vm.ActiveWorkspace);
        Assert.True(vm.IsBookInfoVisible);
        Assert.False(vm.IsSidebarHostVisible);
    }

    [Fact]
    public void ShowStatistics_AndReports_OpenInsights()
    {
        var vm = CreateViewModel(SampleBook());

        vm.ShowStatisticsCommand.Execute(null);
        Assert.Equal(Workspace.Insights, vm.ActiveWorkspace);
        Assert.True(vm.IsStatsPanelVisible);

        vm.ShowReportsCommand.Execute(null);
        Assert.True(vm.IsReportsPanelVisible);
        Assert.False(vm.IsStatsPanelVisible);
        Assert.Equal("report:Entities", vm.ActiveInsight);
    }

    [Fact]
    public void Revise_ShowsTheEditorAndItsNotes_NotStatistics()
    {
        var vm = CreateViewModel(SampleBook());
        vm.SetWorkspaceCommand.Execute("Revise");

        Assert.True(vm.IsSceneEditorVisible);
        Assert.False(vm.IsStatsPanelVisible);
        Assert.True(vm.IsSidebarHostVisible);   // the notes list
        Assert.False(vm.IsInspectorVisible);
    }

    [Fact]
    public void Insights_RemembersItsOwnTab()
    {
        var vm = CreateViewModel(SampleBook());
        vm.OpenInsightCommand.Execute("DailyProgress");

        vm.SetWorkspaceCommand.Execute("Revise");
        vm.SetWorkspaceCommand.Execute("Insights");

        Assert.Equal("DailyProgress", vm.ActiveStatsTab);
    }

    [Fact]
    public void StoryBibleSelection_KeepsTheOpenScene()
    {
        var book = SampleBook();
        var vm = CreateViewModel(book);
        vm.SelectedChapter = book.Chapters[0];
        vm.SelectedScene = book.Chapters[0].Scenes[1];

        vm.SelectedCharacter = book.Characters[0];

        Assert.Equal(Workspace.StoryBible, vm.ActiveWorkspace);
        Assert.True(vm.IsElementEditorVisible);
        Assert.Equal(book.Chapters[0].Scenes[1], vm.SelectedScene);   // still there when returning to Write

        vm.SetWorkspaceCommand.Execute("Write");
        Assert.True(vm.IsSceneEditorVisible);
        Assert.False(vm.IsEditorEmpty);
    }

    [Fact]
    public void OpeningAScene_FromAnotherWorkspace_ShowsTheEditor()
    {
        var book = SampleBook();
        var vm = CreateViewModel(book);
        vm.SetWorkspaceCommand.Execute("Insights");

        vm.SelectedChapter = book.Chapters[1];
        vm.SelectedScene = book.Chapters[1].Scenes[0];

        Assert.Equal(Workspace.Write, vm.ActiveWorkspace);
    }

    [Fact]
    public void NavigateToNote_StaysInRevise()
    {
        var book = SampleBook();
        var vm = CreateViewModel(book);
        vm.SetWorkspaceCommand.Execute("Revise");

        var scene = book.Chapters[1].Scenes[0];
        vm.NavigateToNoteCommand.Execute(new NlpNote { SceneId = scene.Id });

        Assert.Equal(Workspace.Revise, vm.ActiveWorkspace);
        Assert.Equal(scene, vm.SelectedScene);
    }

    [Fact]
    public void NavigateToNote_FromInsights_GoesToWrite()
    {
        var book = SampleBook();
        var vm = CreateViewModel(book);
        vm.SetWorkspaceCommand.Execute("Insights");

        vm.NavigateToNoteCommand.Execute(new NlpNote { SceneId = book.Chapters[0].Scenes[0].Id });

        Assert.Equal(Workspace.Write, vm.ActiveWorkspace);
    }

    // ── Revise notes ─────────────────────────────────────────────────────────

    private static WriterViewModel WithNotes(Book book, params NlpNote[] notes)
    {
        var vm = CreateViewModel(book);
        typeof(WriterViewModel).GetField("_allNotes", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(vm, notes.ToList());
        typeof(WriterViewModel).GetMethod("ApplyNoteFilters", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(vm, null);
        return vm;
    }

    [Fact]
    public void ReviseGroups_FollowBookOrder_AndTextOrderWithinAScene()
    {
        var book = SampleBook();
        var s1a = book.Chapters[0].Scenes[0];
        var s2a = book.Chapters[1].Scenes[0];
        var vm = WithNotes(book,
            new NlpNote { SceneId = s2a.Id, SentenceIndex = 0, Message = "late" },
            new NlpNote { SceneId = s1a.Id, SentenceIndex = 5, Message = "second" },
            new NlpNote { SceneId = s1a.Id, SentenceIndex = 1, Message = "first" },
            new NlpNote { SceneId = "", ChapterTitle = "One", Message = "book-level" });
        vm.ReviseScopeIsScene = false;

        var groups = vm.ReviseNoteGroups;
        Assert.Equal(["Whole book & chapters", "One › 1a", "Two › 2a"], groups.Select(g => g.Title).ToArray());
        Assert.Equal(["first", "second"], groups[1].Select(n => n.Message).ToArray());
        Assert.Equal("4 notes in the book", vm.ReviseSummary);
    }

    [Fact]
    public void ReviseScope_ThisScene_ShowsOnlyTheOpenScene()
    {
        var book = SampleBook();
        var s1a = book.Chapters[0].Scenes[0];
        var vm = WithNotes(book,
            new NlpNote { SceneId = s1a.Id, Message = "here" },
            new NlpNote { SceneId = book.Chapters[1].Scenes[0].Id, Message = "elsewhere" });
        vm.SelectedChapter = book.Chapters[0];
        vm.SelectedScene = s1a;

        vm.SetReviseScopeCommand.Execute("scene");
        Assert.Equal(["here"], vm.ReviseNoteGroups.SelectMany(g => g).Select(n => n.Message).ToArray());
        Assert.Equal("1 note in this scene", vm.ReviseSummary);

        vm.SetReviseScopeCommand.Execute("book");
        Assert.Equal(2, vm.ReviseNoteGroups.Sum(g => g.Count));
    }

    [Fact]
    public void Revise_EmptyEditor_PointsToTheNotes()
    {
        var vm = CreateViewModel(SampleBook());
        vm.SetWorkspaceCommand.Execute("Revise");

        Assert.Equal("Pick a note", vm.EditorEmptyTitle);
        Assert.False(vm.ShowAddSceneAction);
    }

    [Fact]
    public void Inspector_OnlyInWrite_WithAScene()
    {
        var book = SampleBook();
        var vm = CreateViewModel(book);
        vm.IsMetadataPanelVisible = true;
        Assert.False(vm.IsInspectorVisible);   // no scene open

        vm.SelectedChapter = book.Chapters[0];
        vm.SelectedScene = book.Chapters[0].Scenes[0];
        Assert.True(vm.IsInspectorVisible);

        vm.SetWorkspaceCommand.Execute("Insights");
        Assert.False(vm.IsInspectorVisible);
    }

    // ── Outline ──────────────────────────────────────────────────────────────

    [Fact]
    public void Outline_FlattensChaptersAndScenes()
    {
        var vm = CreateViewModel(SampleBook());
        Assert.Equal(["One", "1a", "1b", "Two", "2a"], vm.OutlineRows.Select(Label).ToArray());
        Assert.Equal(2, vm.OutlineRows.OfType<ChapterOutlineRow>().Last().Number);
    }

    [Fact]
    public void Outline_CollapseAndExpand()
    {
        var vm = CreateViewModel(SampleBook());
        var one = vm.OutlineRows.OfType<ChapterOutlineRow>().First();

        vm.ToggleChapterExpandedCommand.Execute(one);
        Assert.Equal(["One", "Two", "2a"], vm.OutlineRows.Select(Label).ToArray());
        Assert.Equal("▸", one.ExpandGlyph);

        vm.ToggleChapterExpandedCommand.Execute(one);
        Assert.Equal(["One", "1a", "1b", "Two", "2a"], vm.OutlineRows.Select(Label).ToArray());
    }

    [Fact]
    public void AddChapter_UsesTheTitleEntered()
    {
        var book = SampleBook();
        var vm = CreateViewModel(book);
        vm.AskChapterTitle = () => Task.FromResult<string?>("  The Docks ");

        vm.AddChapterCommand.Execute(null);

        Assert.Equal("The Docks", book.Chapters[^1].Title);
        Assert.Equal(3, book.Chapters.Count);
        Assert.Same(book.Chapters[^1], vm.SelectedChapter);
    }

    [Theory]
    [InlineData(null)]     // cancelled
    [InlineData("")]
    [InlineData("   ")]
    public void AddChapter_WithoutATitle_CreatesNothing(string? answer)
    {
        var book = SampleBook();
        var vm = CreateViewModel(book);
        vm.AskChapterTitle = () => Task.FromResult(answer);

        vm.AddChapterCommand.Execute(null);

        Assert.Equal(2, book.Chapters.Count);
    }

    [Fact]
    public void Outline_HideAllThenExpandAll()
    {
        var vm = CreateViewModel(SampleBook());
        Assert.Equal("Hide all", vm.ToggleAllChaptersText);

        vm.ToggleAllChaptersCommand.Execute(null);
        Assert.Equal(["One", "Two"], vm.OutlineRows.Select(Label).ToArray());
        Assert.All(vm.OutlineRows.OfType<ChapterOutlineRow>(), c => Assert.False(c.IsExpanded));
        Assert.Equal("Expand all", vm.ToggleAllChaptersText);

        vm.ToggleAllChaptersCommand.Execute(null);
        Assert.Equal(["One", "1a", "1b", "Two", "2a"], vm.OutlineRows.Select(Label).ToArray());
        Assert.Equal("Hide all", vm.ToggleAllChaptersText);
    }

    [Fact]
    public void Outline_ToggleAll_FollowsSingleChapters()
    {
        var vm = CreateViewModel(SampleBook());
        var chapters = vm.OutlineRows.OfType<ChapterOutlineRow>().ToList();

        vm.ToggleChapterExpandedCommand.Execute(chapters[0]);   // one still open: offers to hide
        Assert.Equal("Hide all", vm.ToggleAllChaptersText);
        vm.ToggleChapterExpandedCommand.Execute(chapters[1]);   // all folded by hand: offers to expand
        Assert.Equal("Expand all", vm.ToggleAllChaptersText);
    }

    [Fact]
    public void Outline_ToggleAll_HiddenUntilAChapterHasScenes()
    {
        var book = new Book { Title = "B" };
        book.Chapters.Add(new Chapter { Title = "Empty" });
        var vm = CreateViewModel(book);
        Assert.False(vm.CanToggleAllChapters);

        book.Chapters[0].Scenes.Add(new Scene { Title = "First" });
        Assert.True(vm.CanToggleAllChapters);
    }

    [Fact]
    public void Outline_FollowsStructuralChanges()
    {
        var book = SampleBook();
        var vm = CreateViewModel(book);

        book.Chapters[1].Scenes.Add(new Scene { Title = "2b" });
        book.Chapters.Add(new Chapter { Title = "Three" });

        Assert.Equal(["One", "1a", "1b", "Two", "2a", "2b", "Three"], vm.OutlineRows.Select(Label).ToArray());
    }

    [Fact]
    public void Outline_HighlightsTheOpenScene()
    {
        var book = SampleBook();
        var vm = CreateViewModel(book);
        var row = vm.OutlineRows.OfType<SceneOutlineRow>().Single(r => r.Scene.Title == "2a");

        vm.OpenOutlineRowCommand.Execute(row);

        Assert.Equal(book.Chapters[1], vm.SelectedChapter);
        Assert.Equal(row.Scene, vm.SelectedScene);
        Assert.Single(vm.OutlineRows, r => r.IsSelected);
        Assert.True(vm.OutlineRows.OfType<SceneOutlineRow>().Single(r => r.Scene.Title == "2a").IsSelected);
    }

    [Fact]
    public void MoveSceneToChapter_MovesAcrossChapters_AndKeepsItOpen()
    {
        var book = SampleBook();
        var vm = CreateViewModel(book);
        var scene = book.Chapters[0].Scenes[0];
        vm.SelectedChapter = book.Chapters[0];
        vm.SelectedScene = scene;

        vm.MoveSceneToChapter(scene, book.Chapters[1], 0);

        Assert.DoesNotContain(scene, book.Chapters[0].Scenes);
        Assert.Equal(scene, book.Chapters[1].Scenes[0]);
        Assert.Equal(book.Chapters[1], vm.SelectedChapter);
        Assert.Equal(scene, vm.SelectedScene);
        Assert.Equal(["One", "1b", "Two", "1a", "2a"], vm.OutlineRows.Select(Label).ToArray());
    }

    [Fact]
    public void DeleteScene_WorksForScenesOutsideTheOpenChapter()
    {
        var book = SampleBook();
        var vm = CreateViewModel(book);
        vm.SelectedChapter = book.Chapters[0];

        vm.DeleteSceneCommand.Execute(book.Chapters[1].Scenes[0]);

        Assert.Empty(book.Chapters[1].Scenes);
    }

    // ── Status bar ───────────────────────────────────────────────────────────

    [Fact]
    public async Task SaveState_ReportsSaved()
    {
        var vm = CreateViewModel(SampleBook());
        await (Task)typeof(WriterViewModel)
            .GetMethod("TriggerSaveAsync", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(vm, null)!;
        Assert.Equal("All changes saved", vm.SaveStateText);
    }

    private static string Label(OutlineRow r) => r switch
    {
        ChapterOutlineRow c => c.Chapter.Title,
        SceneOutlineRow s => s.Scene.Title,
        _ => "?"
    };
}
