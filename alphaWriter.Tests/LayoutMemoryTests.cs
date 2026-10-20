using System.Reflection;
using alphaWriter.Models;
using alphaWriter.Services;
using alphaWriter.Services.Nlp;
using alphaWriter.ViewModels;
using Moq;
using Xunit;

namespace alphaWriter.Tests;

/// <summary>
/// The app reopens where the author left off: workspace, open scene per book, and which
/// chapters were folded away. Uses an in-memory store instead of platform preferences.
/// </summary>
public class LayoutMemoryTests
{
    private static (WriterViewModel Vm, MemoryUiStateStore Store) Create(Book book, MemoryUiStateStore? store = null)
    {
        var bookService = new Mock<IBookService>();
        bookService.Setup(s => s.SaveBooksAsync(It.IsAny<List<Book>>())).Returns(Task.CompletedTask);
        var vm = new WriterViewModel(bookService.Object, new Mock<IImageService>().Object,
            new Mock<INlpAnalysisService>().Object, new Mock<INlpModelManager>().Object,
            new Mock<INerService>().Object, new Mock<IPosTaggingService>().Object,
            new Mock<ILocationHeuristicService>().Object, new Mock<INlpCacheService>().Object);
        store ??= new MemoryUiStateStore();
        vm.UiStore = store;

        vm.Books.Add(book);
        typeof(WriterViewModel).GetField("selectedBook", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(vm, book);
        Invoke(vm, "AttachOutline", book);
        return (vm, store);
    }

    private static void Invoke(WriterViewModel vm, string method, params object?[] args) =>
        typeof(WriterViewModel).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(vm, args);

    private static Book SampleBook()
    {
        var book = new Book { Title = "B" };
        var one = new Chapter { Title = "One" };
        one.Scenes.Add(new Scene { Title = "1a" });
        var two = new Chapter { Title = "Two" };
        two.Scenes.Add(new Scene { Title = "2a" });
        two.Scenes.Add(new Scene { Title = "2b" });
        book.Chapters.Add(one);
        book.Chapters.Add(two);
        return book;
    }

    [Fact]
    public void Workspace_IsRememberedAndRestored()
    {
        var book = SampleBook();
        var (vm, store) = Create(book);
        vm.SetWorkspaceCommand.Execute("Revise");
        Assert.Equal("Revise", store.Get("workspace"));

        var (fresh, _) = Create(book, store);
        Invoke(fresh, "RestoreWorkspace");
        Assert.Equal(Workspace.Revise, fresh.ActiveWorkspace);
    }

    [Fact]
    public void LastScene_IsRememberedPerBook()
    {
        var book = SampleBook();
        var (vm, store) = Create(book);
        vm.SelectedChapter = book.Chapters[1];
        vm.SelectedScene = book.Chapters[1].Scenes[1];

        var (fresh, _) = Create(book, store);
        Invoke(fresh, "RestoreLastScene", book);

        Assert.Equal(book.Chapters[1], fresh.SelectedChapter);
        Assert.Equal(book.Chapters[1].Scenes[1], fresh.SelectedScene);
    }

    [Fact]
    public void LastScene_DeletedSinceLastTime_IsIgnored()
    {
        var book = SampleBook();
        var store = new MemoryUiStateStore();
        store.Set($"lastScene.{book.Id}", "no-such-scene");
        var (vm, _) = Create(book, store);

        Invoke(vm, "RestoreLastScene", book);
        Assert.Null(vm.SelectedScene);
    }

    [Fact]
    public void CollapsedChapters_AreRememberedPerBook()
    {
        var book = SampleBook();
        var (vm, store) = Create(book);
        vm.ToggleChapterExpandedCommand.Execute(vm.OutlineRows.OfType<ChapterOutlineRow>().Last());

        var (fresh, _) = Create(book, store);
        var two = fresh.OutlineRows.OfType<ChapterOutlineRow>().Last();
        Assert.False(two.IsExpanded);
        Assert.DoesNotContain(fresh.OutlineRows, r => r is SceneOutlineRow s && s.Chapter == book.Chapters[1]);
    }

    [Theory]
    [InlineData("1", Workspace.Write)]
    [InlineData("2", Workspace.StoryBible)]
    [InlineData("3", Workspace.Revise)]
    [InlineData("4", Workspace.Insights)]
    [InlineData("5", Workspace.Book)]
    public void Shortcuts_SwitchWorkspaces(string key, Workspace expected)
    {
        var (vm, _) = Create(SampleBook());
        Assert.True(vm.HandleShortcut(key));
        Assert.Equal(expected, vm.ActiveWorkspace);
    }

    [Fact]
    public void Shortcut_TogglesSidebar_AndIgnoresUnknownKeys()
    {
        var (vm, _) = Create(SampleBook());
        Assert.True(vm.HandleShortcut("sidebar"));
        Assert.False(vm.IsSidebarVisible);
        Assert.False(vm.HandleShortcut("9"));
    }
}
