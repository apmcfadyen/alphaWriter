using alphaWriter.Models;
using alphaWriter.Models.Analysis;
using alphaWriter.Services;
using alphaWriter.Services.Nlp;
using alphaWriter.ViewModels;
using Moq;
using System.Collections.ObjectModel;
using System.Reflection;
using System.Text.Json;
using Xunit;

namespace alphaWriter.Tests;

/// <summary>
/// Tests for WriterViewModel business logic that does not require a running
/// MAUI host.
///
/// Setting SelectedBook or SelectedChapter via their public property setters
/// triggers Preferences.Default.Set, which may require platform initialization.
/// We therefore use reflection to inject values directly into the backing fields
/// so that only the logic under test runs.
/// </summary>
public class ViewModelTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static WriterViewModel CreateViewModel(out Mock<IBookService> bookServiceMock)
    {
        bookServiceMock = new Mock<IBookService>();
        bookServiceMock
            .Setup(s => s.SaveBooksAsync(It.IsAny<List<Book>>()))
            .Returns(Task.CompletedTask);

        var imageServiceMock = new Mock<IImageService>();
        var nlpServiceMock = new Mock<INlpAnalysisService>();
        var modelManagerMock = new Mock<INlpModelManager>();
        var nerServiceMock = new Mock<INerService>();
        var posTaggingMock = new Mock<IPosTaggingService>();
        var locationHeuristicMock = new Mock<ILocationHeuristicService>();
        var nlpCacheServiceMock = new Mock<INlpCacheService>();
        return new WriterViewModel(bookServiceMock.Object, imageServiceMock.Object,
            nlpServiceMock.Object, modelManagerMock.Object, nerServiceMock.Object,
            posTaggingMock.Object, locationHeuristicMock.Object, nlpCacheServiceMock.Object);
    }

    /// <summary>
    /// Directly sets a backing field on <paramref name="vm"/> by name,
    /// bypassing property setters (and their MAUI/Preferences side-effects).
    /// </summary>
    private static void SetField(WriterViewModel vm, string fieldName, object? value)
    {
        var field = typeof(WriterViewModel).GetField(
            fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(field); // Fail fast if the field name changed
        field!.SetValue(vm, value);
    }

    // ── GetEntityManifestJson ─────────────────────────────────────────────────

    [Fact]
    public void GetEntityManifestJson_NullBook_ReturnsEmptyArray()
    {
        var vm = CreateViewModel(out _);
        // selectedBook field is null by default
        var json = vm.GetEntityManifestJson();
        Assert.Equal("[]", json);
    }

    [Fact]
    public void GetEntityManifestJson_BookWithNoEntities_ReturnsEmptyArray()
    {
        var vm = CreateViewModel(out _);
        var book = new Book();
        SetField(vm, "selectedBook", book);

        var json = vm.GetEntityManifestJson();
        Assert.Equal("[]", json);
    }

    [Fact]
    public void GetEntityManifestJson_BookWithCharacter_IncludesCharacter()
    {
        var vm = CreateViewModel(out _);
        var book = new Book();
        book.Characters.Add(new Character { Name = "Alice", Aka = string.Empty });
        SetField(vm, "selectedBook", book);

        var json = vm.GetEntityManifestJson();
        var doc = JsonDocument.Parse(json);
        var entities = doc.RootElement.EnumerateArray().ToList();

        Assert.Single(entities);
        Assert.Equal("character", entities[0].GetProperty("type").GetString());
        Assert.Equal("Alice", entities[0].GetProperty("name").GetString());
    }

    [Fact]
    public void GetEntityManifestJson_BookWithLocation_IncludesLocation()
    {
        var vm = CreateViewModel(out _);
        var book = new Book();
        book.Locations.Add(new Location { Name = "Wonderland" });
        SetField(vm, "selectedBook", book);

        var json = vm.GetEntityManifestJson();
        var doc = JsonDocument.Parse(json);
        var entities = doc.RootElement.EnumerateArray().ToList();

        Assert.Single(entities);
        Assert.Equal("location", entities[0].GetProperty("type").GetString());
        Assert.Equal("Wonderland", entities[0].GetProperty("name").GetString());
    }

    [Fact]
    public void GetEntityManifestJson_BookWithItem_IncludesItem()
    {
        var vm = CreateViewModel(out _);
        var book = new Book();
        book.Items.Add(new Item { Name = "Sword" });
        SetField(vm, "selectedBook", book);

        var json = vm.GetEntityManifestJson();
        var doc = JsonDocument.Parse(json);
        var entities = doc.RootElement.EnumerateArray().ToList();

        Assert.Single(entities);
        Assert.Equal("item", entities[0].GetProperty("type").GetString());
        Assert.Equal("Sword", entities[0].GetProperty("name").GetString());
    }

    [Fact]
    public void GetEntityManifestJson_AllEntityTypes_IncludesAll()
    {
        var vm = CreateViewModel(out _);
        var book = new Book();
        book.Characters.Add(new Character { Name = "Bob" });
        book.Locations.Add(new Location { Name = "Paris" });
        book.Items.Add(new Item { Name = "Key" });
        SetField(vm, "selectedBook", book);

        var json = vm.GetEntityManifestJson();
        var doc = JsonDocument.Parse(json);
        var entities = doc.RootElement.EnumerateArray().ToList();

        Assert.Equal(3, entities.Count);
        var types = entities.Select(e => e.GetProperty("type").GetString()).ToHashSet();
        Assert.Contains("character", types);
        Assert.Contains("location", types);
        Assert.Contains("item", types);
    }

    [Fact]
    public void GetEntityManifestJson_AkaField_ParsedToAliasesArray()
    {
        var vm = CreateViewModel(out _);
        var book = new Book();
        book.Characters.Add(new Character { Name = "John Smith", Aka = "John, Johnny, Mr. Smith" });
        SetField(vm, "selectedBook", book);

        var json = vm.GetEntityManifestJson();
        var doc = JsonDocument.Parse(json);
        var entity = doc.RootElement.EnumerateArray().First();
        var aliases = entity.GetProperty("aliases").EnumerateArray()
            .Select(a => a.GetString())
            .ToList();

        Assert.Equal(3, aliases.Count);
        Assert.Contains("John", aliases);
        Assert.Contains("Johnny", aliases);
        Assert.Contains("Mr. Smith", aliases);
    }

    [Fact]
    public void GetEntityManifestJson_EmptyAka_AliasesArrayIsEmpty()
    {
        var vm = CreateViewModel(out _);
        var book = new Book();
        book.Characters.Add(new Character { Name = "Alice", Aka = string.Empty });
        SetField(vm, "selectedBook", book);

        var json = vm.GetEntityManifestJson();
        var doc = JsonDocument.Parse(json);
        var entity = doc.RootElement.EnumerateArray().First();
        var aliases = entity.GetProperty("aliases").EnumerateArray().ToList();
        Assert.Empty(aliases);
    }

    [Fact]
    public void GetEntityManifestJson_AkaWithSpaces_TrimmedAliases()
    {
        var vm = CreateViewModel(out _);
        var book = new Book();
        book.Characters.Add(new Character { Name = "Eve", Aka = "  Evie , E " });
        SetField(vm, "selectedBook", book);

        var json = vm.GetEntityManifestJson();
        var doc = JsonDocument.Parse(json);
        var entity = doc.RootElement.EnumerateArray().First();
        var aliases = entity.GetProperty("aliases").EnumerateArray()
            .Select(a => a.GetString())
            .ToList();

        Assert.Contains("Evie", aliases);
        Assert.Contains("E", aliases);
        // No extra whitespace
        Assert.DoesNotContain("  Evie ", aliases);
    }

    [Fact]
    public void GetEntityManifestJson_ResultIsValidJson()
    {
        var vm = CreateViewModel(out _);
        var book = new Book();
        book.Characters.Add(new Character { Name = "Test \"Quoted\" Name", Aka = "alias" });
        SetField(vm, "selectedBook", book);

        var json = vm.GetEntityManifestJson();
        // Should not throw
        var doc = JsonDocument.Parse(json);
        Assert.Equal(JsonValueKind.Array, doc.RootElement.ValueKind);
    }

    // ── MoveBook ──────────────────────────────────────────────────────────────

    [Fact]
    public void MoveBook_MovesBookToLowerIndex()
    {
        var vm = CreateViewModel(out _);
        var b1 = new Book { Title = "Book1" };
        var b2 = new Book { Title = "Book2" };
        var b3 = new Book { Title = "Book3" };
        vm.Books = new ObservableCollection<Book>([b1, b2, b3]);

        // Move b3 to index 0 (before b1)
        vm.MoveBook(b3, 0);

        Assert.Equal(b3, vm.Books[0]);
        Assert.Equal(b1, vm.Books[1]);
        Assert.Equal(b2, vm.Books[2]);
    }

    [Fact]
    public void MoveBook_MovesBookToHigherIndex()
    {
        var vm = CreateViewModel(out _);
        var b1 = new Book { Title = "Book1" };
        var b2 = new Book { Title = "Book2" };
        var b3 = new Book { Title = "Book3" };
        vm.Books = new ObservableCollection<Book>([b1, b2, b3]);

        // Move b1 to index 2 (after b2, before b3's old position)
        vm.MoveBook(b1, 2);

        Assert.Equal(b2, vm.Books[0]);
        Assert.Equal(b1, vm.Books[1]);
        Assert.Equal(b3, vm.Books[2]);
    }

    [Fact]
    public void MoveBook_BookNotInList_NoChange()
    {
        var vm = CreateViewModel(out _);
        var b1 = new Book { Title = "Book1" };
        var b2 = new Book { Title = "Book2" };
        var outsider = new Book { Title = "Outsider" };
        vm.Books = new ObservableCollection<Book>([b1, b2]);

        vm.MoveBook(outsider, 0); // should be a no-op

        Assert.Equal(b1, vm.Books[0]);
        Assert.Equal(b2, vm.Books[1]);
    }

    [Fact]
    public void MoveBook_SamePosition_NoChange()
    {
        var vm = CreateViewModel(out _);
        var b1 = new Book { Title = "Book1" };
        var b2 = new Book { Title = "Book2" };
        vm.Books = new ObservableCollection<Book>([b1, b2]);

        vm.MoveBook(b1, 0); // already at 0

        Assert.Equal(b1, vm.Books[0]);
        Assert.Equal(b2, vm.Books[1]);
    }

    // ── MoveChapter ───────────────────────────────────────────────────────────

    [Fact]
    public void MoveChapter_NullSelectedBook_NoChange()
    {
        var vm = CreateViewModel(out _);
        // selectedBook is null → MoveChapter should be a no-op
        var ch = new Chapter { Title = "Ch1" };
        vm.MoveChapter(ch, 0); // should not throw
    }

    [Fact]
    public void MoveChapter_MovesChapterToLowerIndex()
    {
        var vm = CreateViewModel(out _);
        var book = new Book();
        var ch1 = new Chapter { Title = "Ch1" };
        var ch2 = new Chapter { Title = "Ch2" };
        var ch3 = new Chapter { Title = "Ch3" };
        book.Chapters.Add(ch1);
        book.Chapters.Add(ch2);
        book.Chapters.Add(ch3);
        SetField(vm, "selectedBook", book);

        vm.MoveChapter(ch3, 0);

        Assert.Equal(ch3, book.Chapters[0]);
        Assert.Equal(ch1, book.Chapters[1]);
        Assert.Equal(ch2, book.Chapters[2]);
    }

    [Fact]
    public void MoveChapter_MovesChapterToHigherIndex()
    {
        var vm = CreateViewModel(out _);
        var book = new Book();
        var ch1 = new Chapter { Title = "Ch1" };
        var ch2 = new Chapter { Title = "Ch2" };
        var ch3 = new Chapter { Title = "Ch3" };
        book.Chapters.Add(ch1);
        book.Chapters.Add(ch2);
        book.Chapters.Add(ch3);
        SetField(vm, "selectedBook", book);

        vm.MoveChapter(ch1, 3); // move ch1 to after ch3

        Assert.Equal(ch2, book.Chapters[0]);
        Assert.Equal(ch3, book.Chapters[1]);
        Assert.Equal(ch1, book.Chapters[2]);
    }

    // ── MoveScene ─────────────────────────────────────────────────────────────

    [Fact]
    public void MoveScene_NullSelectedChapter_NoChange()
    {
        var vm = CreateViewModel(out _);
        // selectedChapter is null → MoveScene should be a no-op
        var scene = new Scene { Title = "Scene1" };
        vm.MoveScene(scene, 0); // should not throw
    }

    [Fact]
    public void MoveScene_MovesSceneToLowerIndex()
    {
        var vm = CreateViewModel(out _);
        var chapter = new Chapter();
        var s1 = new Scene { Title = "Scene1" };
        var s2 = new Scene { Title = "Scene2" };
        var s3 = new Scene { Title = "Scene3" };
        chapter.Scenes.Add(s1);
        chapter.Scenes.Add(s2);
        chapter.Scenes.Add(s3);
        SetField(vm, "selectedChapter", chapter);

        vm.MoveScene(s3, 0);

        Assert.Equal(s3, chapter.Scenes[0]);
        Assert.Equal(s1, chapter.Scenes[1]);
        Assert.Equal(s2, chapter.Scenes[2]);
    }

    [Fact]
    public void MoveScene_MovesSceneToHigherIndex()
    {
        var vm = CreateViewModel(out _);
        var chapter = new Chapter();
        var s1 = new Scene { Title = "Scene1" };
        var s2 = new Scene { Title = "Scene2" };
        var s3 = new Scene { Title = "Scene3" };
        chapter.Scenes.Add(s1);
        chapter.Scenes.Add(s2);
        chapter.Scenes.Add(s3);
        SetField(vm, "selectedChapter", chapter);

        vm.MoveScene(s1, 3); // move s1 to last

        Assert.Equal(s2, chapter.Scenes[0]);
        Assert.Equal(s3, chapter.Scenes[1]);
        Assert.Equal(s1, chapter.Scenes[2]);
    }

    [Fact]
    public void MoveScene_SceneNotInChapter_NoChange()
    {
        var vm = CreateViewModel(out _);
        var chapter = new Chapter();
        var s1 = new Scene { Title = "Scene1" };
        var s2 = new Scene { Title = "Scene2" };
        var outsider = new Scene { Title = "Outsider" };
        chapter.Scenes.Add(s1);
        chapter.Scenes.Add(s2);
        SetField(vm, "selectedChapter", chapter);

        vm.MoveScene(outsider, 0); // no-op

        Assert.Equal(s1, chapter.Scenes[0]);
        Assert.Equal(s2, chapter.Scenes[1]);
    }

    // ── IsElementEditorVisible / IsSceneEditorVisible ─────────────────────────

    [Fact]
    public void IsElementEditorVisible_NoEntitySelected_ReturnsFalse()
    {
        var vm = CreateViewModel(out _);
        Assert.False(vm.IsElementEditorVisible);
    }

    [Fact]
    public void IsSceneEditorVisible_NoEntityOrBookInfo_ReturnsTrue()
    {
        var vm = CreateViewModel(out _);
        Assert.True(vm.IsSceneEditorVisible);
    }

    // ── Sidebar mode flags ────────────────────────────────────────────────────

    [Fact]
    public void SidebarMode_Default_IsChapters()
    {
        var vm = CreateViewModel(out _);
        Assert.True(vm.IsChaptersMode);
        Assert.False(vm.IsCharactersMode);
        Assert.False(vm.IsLocationsMode);
        Assert.False(vm.IsItemsMode);
    }

    // ── Note filtering ───────────────────────────────────────────────────────

    private static void SetupAnalysisResults(WriterViewModel vm)
    {
        var notes = new List<NlpNote>
        {
            new() { Severity = NlpNoteSeverity.Issue, Category = NlpNoteCategory.CopyEditor, Message = "Copy editor issue", ChapterTitle = "Ch1", SceneTitle = "S1" },
            new() { Severity = NlpNoteSeverity.Warning, Category = NlpNoteCategory.DevelopmentalEditor, Message = "Dev editor warning", ChapterTitle = "Ch1", SceneTitle = "S1" },
            new() { Severity = NlpNoteSeverity.Info, Category = NlpNoteCategory.LineEditor, Message = "Line editor info", ChapterTitle = "Ch1", SceneTitle = "S2" },
            new() { Severity = NlpNoteSeverity.Warning, Category = NlpNoteCategory.LineEditor, Message = "Line editor warning", ChapterTitle = "Ch2", SceneTitle = "S3" },
            new() { Severity = NlpNoteSeverity.Info, Category = NlpNoteCategory.CopyEditor, Message = "Copy editor info", ChapterTitle = "Ch2", SceneTitle = "S4" },
        };

        // Set the private _allNotes and _analysisResults fields
        var allNotesField = typeof(WriterViewModel).GetField("_allNotes", BindingFlags.NonPublic | BindingFlags.Instance);
        allNotesField!.SetValue(vm, notes);

        var resultsField = typeof(WriterViewModel).GetField("_analysisResults", BindingFlags.NonPublic | BindingFlags.Instance);
        resultsField!.SetValue(vm, new List<SceneAnalysisResult> { new() { SceneId = "s1", SceneTitle = "S1" } });
    }

    [Fact]
    public void NoteFilter_AllCategories_ShowsAllNotes()
    {
        var vm = CreateViewModel(out _);
        SetupAnalysisResults(vm);

        // Toggle to a non-default value first so the change handler fires when set back
        vm.SelectedCategoryFilter = "Copy Editor";
        vm.SelectedCategoryFilter = "All";

        Assert.Equal(5, vm.NlpNotes.Count);
    }

    [Fact]
    public void NoteFilter_ByCategoryStyle_FiltersCorrectly()
    {
        var vm = CreateViewModel(out _);
        SetupAnalysisResults(vm);

        vm.SelectedCategoryFilter = "Copy Editor";

        Assert.Equal(2, vm.NlpNotes.Count);
        Assert.All(vm.NlpNotes, n => Assert.Equal(NlpNoteCategory.CopyEditor, n.Category));
    }

    [Fact]
    public void NoteFilter_BySeverityWarning_FiltersCorrectly()
    {
        var vm = CreateViewModel(out _);
        SetupAnalysisResults(vm);

        vm.SelectedSeverityFilter = "Warning";

        Assert.Equal(2, vm.NlpNotes.Count);
        Assert.All(vm.NlpNotes, n => Assert.Equal(NlpNoteSeverity.Warning, n.Severity));
    }

    [Fact]
    public void NoteFilter_ByCategoryAndSeverity_CombinesFilters()
    {
        var vm = CreateViewModel(out _);
        SetupAnalysisResults(vm);

        vm.SelectedCategoryFilter = "Copy Editor";
        vm.SelectedSeverityFilter = "Issue";

        Assert.Single(vm.NlpNotes);
        Assert.Equal("Copy editor issue", vm.NlpNotes[0].Message);
    }

    [Fact]
    public void NoteFilter_SortsByIssueSeverityFirst()
    {
        var vm = CreateViewModel(out _);
        SetupAnalysisResults(vm);

        // Trigger filter by toggling category
        vm.SelectedCategoryFilter = "Copy Editor";
        vm.SelectedCategoryFilter = "All";

        // Issue should come first, then Warning, then Info
        Assert.Equal(5, vm.NlpNotes.Count);
        Assert.Equal(NlpNoteSeverity.Issue, vm.NlpNotes[0].Severity);
        Assert.True(vm.NlpNotes.Take(1).All(n => n.Severity == NlpNoteSeverity.Issue));
        Assert.True(vm.NlpNotes.Skip(1).Take(2).All(n => n.Severity == NlpNoteSeverity.Warning));
        Assert.True(vm.NlpNotes.Skip(3).All(n => n.Severity == NlpNoteSeverity.Info));
    }

    [Fact]
    public void ClearAnalysis_ResetsAllAnalysisState()
    {
        var vm = CreateViewModel(out _);
        SetupAnalysisResults(vm);

        // Trigger filter to populate NlpNotes
        vm.SelectedCategoryFilter = "Copy Editor";
        vm.SelectedCategoryFilter = "All";
        Assert.True(vm.NlpNotes.Count > 0);
        Assert.True(vm.HasAnalysisResults);

        // Clear
        vm.ClearAnalysisCommand.Execute(null);

        Assert.Empty(vm.NlpNotes);
        Assert.False(vm.HasAnalysisResults);
        Assert.Equal("All", vm.SelectedCategoryFilter);
        Assert.Equal("All", vm.SelectedSeverityFilter);
        Assert.Equal("", vm.AnalysisProgress);
    }
}
