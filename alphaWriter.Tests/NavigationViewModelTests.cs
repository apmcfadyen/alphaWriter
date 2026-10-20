using System.Reflection;
using alphaWriter.Models;
using alphaWriter.Services;
using alphaWriter.Services.Nlp;
using alphaWriter.ViewModels;
using Moq;
using Xunit;

namespace alphaWriter.Tests;

/// <summary>
/// Orientation cues: the breadcrumb ("where am I") and the editor's empty state
/// ("what do I do next"). Backing fields are set by reflection to avoid MAUI
/// Preferences side effects in the property setters.
/// </summary>
public class NavigationViewModelTests
{
    private static WriterViewModel CreateViewModel(Book? book, Chapter? chapter = null, Scene? scene = null)
    {
        var vm = new WriterViewModel(new Mock<IBookService>().Object, new Mock<IImageService>().Object,
            new Mock<INlpAnalysisService>().Object, new Mock<INlpModelManager>().Object,
            new Mock<INerService>().Object, new Mock<IPosTaggingService>().Object,
            new Mock<ILocationHeuristicService>().Object, new Mock<INlpCacheService>().Object);

        Set(vm, "selectedBook", book);
        Set(vm, "selectedChapter", chapter);
        Set(vm, "selectedScene", scene);
        typeof(WriterViewModel)
            .GetMethod("UpdateBreadcrumb", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(vm, null);
        return vm;
    }

    private static void Set(WriterViewModel vm, string field, object? value) =>
        typeof(WriterViewModel).GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(vm, value);

    private static (Book, Chapter, Scene) Sample()
    {
        var book = new Book { Title = "The Long Winter of the Northern Reaches" };
        var chapter = new Chapter { Title = "The Docks" };
        var scene = new Scene { Title = "Fog" };
        chapter.Scenes.Add(scene);
        book.Chapters.Add(chapter);
        return (book, chapter, scene);
    }

    // ── Breadcrumb ───────────────────────────────────────────────────────────

    [Fact]
    public void Breadcrumb_LastItemIsCurrentAndNotALink()
    {
        var (book, chapter, scene) = Sample();
        var vm = CreateViewModel(book, chapter, scene);

        var crumbs = vm.BreadcrumbItems;
        Assert.Equal(["The Long Winter of the Northern Reaches", "The Docks", "Fog"], crumbs.Select(c => c.Label).ToArray());
        Assert.True(crumbs[0].IsLink);
        Assert.True(crumbs[1].IsLink);
        Assert.True(crumbs[2].IsCurrent);
    }

    [Fact]
    public void Breadcrumb_ChapterWithoutScene_ChapterIsCurrent()
    {
        var (book, chapter, _) = Sample();
        var vm = CreateViewModel(book, chapter);

        Assert.Equal(2, vm.BreadcrumbItems.Count);
        Assert.True(vm.BreadcrumbItems[^1].IsCurrent);   // no dangling "/" link at the end
    }

    [Fact]
    public void Breadcrumb_SeparatorsOnlyBetweenItems()
    {
        var (book, chapter, scene) = Sample();
        var crumbs = CreateViewModel(book, chapter, scene).BreadcrumbItems;

        Assert.False(crumbs[0].ShowSeparator);
        Assert.True(crumbs[1].ShowSeparator);
        Assert.True(crumbs[2].ShowSeparator);
    }

    // ── Editor empty state ───────────────────────────────────────────────────

    [Fact]
    public void EmptyState_NoBook_OffersCreateBook()
    {
        var vm = CreateViewModel(null);
        Assert.True(vm.IsEditorEmpty);
        Assert.Equal("No book open", vm.EditorEmptyTitle);
        Assert.True(vm.ShowCreateBookAction);
        Assert.False(vm.ShowAddChapterAction);
        Assert.False(vm.ShowAddSceneAction);
    }

    [Fact]
    public void EmptyState_BookWithoutChapters_OffersAddChapter()
    {
        var vm = CreateViewModel(new Book { Title = "B" });
        Assert.Equal("Start your outline", vm.EditorEmptyTitle);
        Assert.True(vm.ShowAddChapterAction);
        Assert.False(vm.ShowCreateBookAction);
    }

    [Fact]
    public void EmptyState_NothingChosen_PointsToTheOutline()
    {
        var (book, _, _) = Sample();
        var vm = CreateViewModel(book);
        Assert.Equal("Choose a scene", vm.EditorEmptyTitle);
        Assert.False(vm.ShowAddChapterAction);
        Assert.False(vm.ShowAddSceneAction);
    }

    [Fact]
    public void EmptyState_EmptyChapter_OffersAddScene()
    {
        var book = new Book();
        var chapter = new Chapter { Title = "Ch 1" };
        book.Chapters.Add(chapter);
        var vm = CreateViewModel(book, chapter);

        Assert.Contains("has no scenes yet", vm.EditorEmptyTitle);
        Assert.True(vm.ShowAddSceneAction);
    }

    [Fact]
    public void EmptyState_SceneOpen_IsNotEmpty()
    {
        var (book, chapter, scene) = Sample();
        Assert.False(CreateViewModel(book, chapter, scene).IsEditorEmpty);
    }
}
