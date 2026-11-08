using System.Text.Json;
using alphaWriter.Models;
using alphaWriter.Services;
using Xunit;

namespace alphaWriter.Tests;

/// <summary>Books stored one folder per book and one file per scene, and the move from books.json.</summary>
public sealed class BookStorageTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "aw-storage-" + Guid.NewGuid().ToString("N"));

    public BookStorageTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    private static Book SampleBook(string title = "Salt")
    {
        var book = new Book { Title = title, Author = "A. Writer" };
        book.Characters.Add(new Character { Name = "Maya", Pronouns = "she/her" });
        var chapter = new Chapter { Title = "One" };
        chapter.Scenes.Add(new Scene { Title = "Dock", Status = SceneStatus.Draft, Content = "<p>“Fog,” she said.</p>" });
        chapter.Scenes.Add(new Scene { Title = "Ledger", Status = SceneStatus.FirstEdit, Content = "<p>Tomas read it twice.</p>" });
        book.Chapters.Add(chapter);
        return book;
    }

    private string BookDir(Book b) => Path.Combine(_root, "books", b.Id);
    private string ScenePath(Book b, Scene s) => Path.Combine(BookDir(b), "scenes", s.Id + ".html");
    private static string Json(Book b) => JsonSerializer.Serialize(b);

    [Fact]
    public async Task SaveAndLoad_RoundTripsEverything()
    {
        var book = SampleBook();
        await new BookService(_root).SaveBooksAsync([book]);

        Assert.True(File.Exists(Path.Combine(BookDir(book), "book.json")));
        Assert.Equal("<p>“Fog,” she said.</p>", File.ReadAllText(ScenePath(book, book.Chapters[0].Scenes[0])));
        Assert.DoesNotContain("Fog", File.ReadAllText(Path.Combine(BookDir(book), "book.json")));   // text lives in scene files

        var loaded = Assert.Single(await new BookService(_root).LoadBooksAsync());
        Assert.Equal(Json(book), Json(loaded));
    }

    [Fact]
    public async Task Save_OnlyRewritesChangedFiles()
    {
        var book = SampleBook();
        var service = new BookService(_root);
        await service.SaveBooksAsync([book]);
        var dock = ScenePath(book, book.Chapters[0].Scenes[0]);
        var ledger = ScenePath(book, book.Chapters[0].Scenes[1]);
        var old = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(dock, old);
        File.SetLastWriteTimeUtc(ledger, old);

        book.Chapters[0].Scenes[1].Content = "<p>Tomas read it three times.</p>";
        await service.SaveBooksAsync([book]);

        Assert.Equal(old, File.GetLastWriteTimeUtc(dock));        // untouched
        Assert.NotEqual(old, File.GetLastWriteTimeUtc(ledger));   // rewritten
    }

    [Fact]
    public async Task Save_RemovesDeletedScenesAndBooks()
    {
        var keep = SampleBook("Keep");
        var drop = SampleBook("Drop");
        var service = new BookService(_root);
        await service.SaveBooksAsync([keep, drop]);

        var removedScene = keep.Chapters[0].Scenes[1];
        keep.Chapters[0].Scenes.Remove(removedScene);
        await service.SaveBooksAsync([keep]);

        Assert.False(File.Exists(ScenePath(keep, removedScene)));
        Assert.False(Directory.Exists(BookDir(drop)));
        Assert.True(Directory.Exists(BookDir(keep)));
    }

    [Fact]
    public async Task UnreadableBookFolder_IsSkippedAndNeverDeleted()
    {
        var good = SampleBook();
        await new BookService(_root).SaveBooksAsync([good]);
        var broken = Path.Combine(_root, "books", "broken-book");
        Directory.CreateDirectory(broken);
        File.WriteAllText(Path.Combine(broken, "book.json"), "{ not json");

        var service = new BookService(_root);
        var loaded = await service.LoadBooksAsync();
        await service.SaveBooksAsync(loaded);

        Assert.Single(loaded);
        Assert.True(File.Exists(Path.Combine(broken, "book.json")));
    }

    [Fact]
    public async Task Load_KeepsTheSavedBookOrder()
    {
        var b = SampleBook("Zebra");
        var a = SampleBook("Apple");
        await new BookService(_root).SaveBooksAsync([b, a]);
        var loaded = await new BookService(_root).LoadBooksAsync();
        Assert.Equal(["Zebra", "Apple"], loaded.Select(x => x.Title));
    }

    // ── Migration ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Migration_SplitsBooksJsonAndMovesPictures()
    {
        var one = SampleBook("One");
        var two = SampleBook("Two");
        var picture = Path.Combine("images", one.Id, "maya.png");
        Directory.CreateDirectory(Path.Combine(_root, "images", one.Id));
        File.WriteAllBytes(Path.Combine(_root, picture), [1, 2, 3]);
        one.Characters[0].ImagePath = picture;
        File.WriteAllText(Path.Combine(_root, "books.json"), JsonSerializer.Serialize(new List<Book> { one, two }));

        var loaded = await new BookService(_root).LoadBooksAsync();

        Assert.Equal(["One", "Two"], loaded.Select(b => b.Title));
        Assert.False(File.Exists(Path.Combine(_root, "books.json")));
        Assert.True(File.Exists(Path.Combine(_root, "books.json.migrated")));
        Assert.Equal("<p>Tomas read it twice.</p>", loaded[0].Chapters[0].Scenes[1].Content);

        var newPicture = loaded[0].Characters[0].ImagePath;
        Assert.Equal(Path.Combine("books", one.Id, "images", "maya.png"), newPicture);
        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(Path.Combine(_root, newPicture)));
        Assert.False(Directory.Exists(Path.Combine(_root, "migrating")));
    }

    [Fact]
    public async Task Migration_IsOneTime()
    {
        File.WriteAllText(Path.Combine(_root, "books.json"), JsonSerializer.Serialize(new List<Book> { SampleBook() }));
        var first = await new BookService(_root).LoadBooksAsync();
        var second = await new BookService(_root).LoadBooksAsync();
        Assert.Equal(Json(first[0]), Json(second[0]));
        Assert.Single(Directory.GetDirectories(Path.Combine(_root, "books")));
    }

    [Fact]
    public async Task Migration_UnreadableLegacyFile_IsLeftAlone()
    {
        File.WriteAllText(Path.Combine(_root, "books.json"), "{ broken");
        var loaded = await new BookService(_root).LoadBooksAsync();
        Assert.Empty(loaded);
        Assert.True(File.Exists(Path.Combine(_root, "books.json")));
        Assert.False(Directory.Exists(Path.Combine(_root, "books")));
    }

    [Fact]
    public async Task EmptyLibrary_StartsInTheNewLayout()
    {
        var service = new BookService(_root);
        Assert.Empty(await service.LoadBooksAsync());
        await service.SaveBooksAsync([SampleBook()]);
        Assert.Single(Directory.GetDirectories(Path.Combine(_root, "books")));
        Assert.False(File.Exists(Path.Combine(_root, "books.json")));
    }
}
