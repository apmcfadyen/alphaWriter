using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using alphaWriter.Models;
using alphaWriter.Services;
using alphaWriter.Services.Nlp;
using alphaWriter.Services.Sync;
using alphaWriter.ViewModels;
using Moq;
using Xunit;

namespace alphaWriter.Tests;

/// <summary>An in-memory folder, standing in for a device's library or the shared folder.</summary>
internal sealed class MemoryStore(string location) : ISyncStore
{
    public Dictionary<string, (byte[] Data, DateTime Modified)> Files { get; } = new(StringComparer.Ordinal);
    private int _tick;

    public string Location => location;

    public Task<IReadOnlyList<SyncEntry>> ListAsync(string prefix, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<SyncEntry>>(Files
            .Where(f => f.Key.StartsWith(prefix + "/", StringComparison.Ordinal))
            .Select(f => new SyncEntry(f.Key, f.Value.Data.Length, f.Value.Modified)).ToList());

    public Task<byte[]?> ReadAsync(string path, CancellationToken ct = default) =>
        Task.FromResult(Files.TryGetValue(path, out var f) ? f.Data : null);

    public Task<SyncEntry> WriteAsync(string path, byte[] data, CancellationToken ct = default)
    {
        var modified = new DateTime(2026, 1, 1).AddSeconds(++_tick);
        Files[path] = (data, modified);
        return Task.FromResult(new SyncEntry(path, data.Length, modified));
    }

    public Task DeleteAsync(string path, CancellationToken ct = default)
    {
        Files.Remove(path);
        return Task.CompletedTask;
    }

    public string? Text(string path) => Files.TryGetValue(path, out var f) ? Encoding.UTF8.GetString(f.Data) : null;
    public void Put(string path, string text) => WriteAsync(path, Encoding.UTF8.GetBytes(text));
}

/// <summary>Phase 3: syncing book folders through a shared folder, and merging book.json.</summary>
public sealed class SyncTests : IDisposable
{
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "aw-sync-" + Guid.NewGuid().ToString("N"));
    private readonly MemoryStore _share = new(@"\\nas\writing\alphaWriter");
    private readonly MemoryStore _desk = new("desk");
    private readonly MemoryStore _tablet = new("tablet");

    public void Dispose()
    {
        try { Directory.Delete(_tmp, recursive: true); } catch { }
    }

    private SyncEngine Engine(MemoryStore device, string name) =>
        new(device, _share, Path.Combine(_tmp, name), name, () => new DateTime(2026, 11, 2, 10, 30, 0));

    private Task<SyncReport> Sync(MemoryStore device) =>
        Engine(device, device == _desk ? "Desk" : "Tablet").RunAsync();

    private static string Book(params (string Id, string Title)[] scenes) => JsonSerializer.Serialize(new
    {
        Id = "b1", Title = "Salt",
        Chapters = new[] { new { Id = "c1", Title = "One", Scenes = scenes.Select(s => new { s.Id, s.Title }).ToArray() } },
        Characters = Array.Empty<object>()
    });

    private const string BookPath = "books/b1/book.json";
    private static string Scene(string id) => $"books/b1/scenes/{id}.html";

    private async Task StartWithSharedBook()
    {
        _desk.Put(BookPath, Book(("s1", "Dock"), ("s2", "Ledger")));
        _desk.Put(Scene("s1"), "<p>Fog.</p>");
        _desk.Put(Scene("s2"), "<p>Ink.</p>");
        await Sync(_desk);     // upload
        await Sync(_tablet);   // download
    }

    // ── Basics ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task FirstSync_UploadsThenAnotherDeviceDownloads()
    {
        await StartWithSharedBook();
        Assert.Equal("<p>Fog.</p>", _share.Text(Scene("s1")));
        Assert.Equal("<p>Ink.</p>", _tablet.Text(Scene("s2")));
        Assert.NotNull(_share.Text(SyncEngine.MarkerFile));
        Assert.Null(_share.Text(SyncEngine.LockFile));   // released
    }

    [Fact]
    public async Task EditsToDifferentScenes_BothSurvive()
    {
        await StartWithSharedBook();
        _desk.Put(Scene("s1"), "<p>Fog, thicker.</p>");
        _tablet.Put(Scene("s2"), "<p>Ink, spilled.</p>");

        await Sync(_desk);
        var report = await Sync(_tablet);
        await Sync(_desk);

        Assert.Contains("b1", report.ChangedLocalBooks);
        foreach (var device in new[] { _desk, _tablet, _share })
        {
            Assert.Equal("<p>Fog, thicker.</p>", device.Text(Scene("s1")));
            Assert.Equal("<p>Ink, spilled.</p>", device.Text(Scene("s2")));
        }
    }

    [Fact]
    public async Task NothingChanged_DoesNothing()
    {
        await StartWithSharedBook();
        var report = await Sync(_desk);
        Assert.False(report.ChangedAnything);
        Assert.Empty(report.ChangedLocalBooks);
    }

    [Fact]
    public async Task DeletedScene_IsDeletedEverywhere()
    {
        await StartWithSharedBook();
        _desk.Files.Remove(Scene("s2"));
        _desk.Put(BookPath, Book(("s1", "Dock")));
        await Sync(_desk);
        await Sync(_tablet);
        Assert.Null(_share.Text(Scene("s2")));
        Assert.Null(_tablet.Text(Scene("s2")));
    }

    // ── Conflicts ────────────────────────────────────────────────────────────

    [Fact]
    public async Task SameSceneEditedOnBoth_KeepsBothVersions()
    {
        await StartWithSharedBook();
        _desk.Put(Scene("s1"), "<p>Desk version.</p>");
        _tablet.Put(Scene("s1"), "<p>Tablet version.</p>");

        await Sync(_desk);
        var report = await Sync(_tablet);
        await Sync(_desk);

        // The tablet keeps its own text; the desk's version becomes a conflicted copy beside it
        Assert.Equal("<p>Tablet version.</p>", _tablet.Text(Scene("s1")));
        var book = JsonNode.Parse(_tablet.Text(BookPath)!)!;
        var scenes = book["Chapters"]![0]!["Scenes"]!.AsArray();
        Assert.Equal(3, scenes.Count);
        var copy = scenes[1]!;
        Assert.StartsWith("Dock (conflicted copy, 2 Nov 2026", copy["Title"]!.GetValue<string>());
        var copyPath = Scene(copy["Id"]!.GetValue<string>());
        Assert.Equal("<p>Desk version.</p>", _tablet.Text(copyPath));
        Assert.Single(report.Notes, n => n.Contains("conflicted copy"));

        // ...and the desk ends up with the same: nothing lost anywhere
        Assert.Equal("<p>Tablet version.</p>", _desk.Text(Scene("s1")));
        Assert.Equal("<p>Desk version.</p>", _desk.Text(copyPath));
        Assert.Equal(_tablet.Text(BookPath), _desk.Text(BookPath));
    }

    [Fact]
    public async Task SceneAddedOnEachDevice_BothAppearInTheBook()
    {
        await StartWithSharedBook();
        _desk.Put(BookPath, Book(("s1", "Dock"), ("s2", "Ledger"), ("d1", "Desk scene")));
        _desk.Put(Scene("d1"), "<p>From the desk.</p>");
        _tablet.Put(BookPath, Book(("s1", "Dock"), ("t1", "Tablet scene"), ("s2", "Ledger")));
        _tablet.Put(Scene("t1"), "<p>From the tablet.</p>");

        await Sync(_desk);
        await Sync(_tablet);
        await Sync(_desk);

        var titles = JsonNode.Parse(_desk.Text(BookPath)!)!["Chapters"]![0]!["Scenes"]!.AsArray()
            .Select(s => s!["Title"]!.GetValue<string>()).ToList();
        Assert.Equal(["Dock", "Tablet scene", "Ledger", "Desk scene"], titles);
        Assert.Equal("<p>From the tablet.</p>", _desk.Text(Scene("t1")));
        Assert.Equal("<p>From the desk.</p>", _tablet.Text(Scene("d1")));
    }

    [Fact]
    public async Task DeletedOnOneDevice_EditedOnTheOther_KeepsTheEdit()
    {
        await StartWithSharedBook();
        _desk.Files.Remove(Scene("s2"));
        _tablet.Put(Scene("s2"), "<p>Ink, rewritten.</p>");
        await Sync(_desk);
        await Sync(_tablet);
        await Sync(_desk);
        Assert.Equal("<p>Ink, rewritten.</p>", _desk.Text(Scene("s2")));
    }

    // ── Whole books ──────────────────────────────────────────────────────────

    [Fact]
    public async Task DeletedBook_IsDeletedOnOtherDevices()
    {
        await StartWithSharedBook();
        foreach (var path in _desk.Files.Keys.Where(k => k.StartsWith("books/b1/")).ToList()) _desk.Files.Remove(path);

        await Sync(_desk);
        var report = await Sync(_tablet);

        Assert.DoesNotContain(_share.Files.Keys, k => k.StartsWith("books/b1/"));
        Assert.DoesNotContain(_tablet.Files.Keys, k => k.StartsWith("books/b1/"));
        Assert.Contains("b1", report.ChangedLocalBooks);
    }

    [Fact]
    public async Task DeletedBook_EditedElsewhereMeanwhile_ComesBack()
    {
        await StartWithSharedBook();
        foreach (var path in _desk.Files.Keys.Where(k => k.StartsWith("books/b1/")).ToList()) _desk.Files.Remove(path);
        await Sync(_desk);                                   // deleted on the share
        _tablet.Put(Scene("s1"), "<p>Kept writing.</p>");   // but the tablet was still editing
        await Sync(_tablet);

        Assert.Equal("<p>Kept writing.</p>", _share.Text(Scene("s1")));
        Assert.Empty(_share.Files.Keys.Where(k => k.StartsWith(SyncEngine.TombstonePrefix)));
    }

    // ── Safety ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task ShareLooksEmpty_SyncStopsAndDeletesNothing()
    {
        await StartWithSharedBook();
        _share.Files.Clear();   // wrong path, or the share didn't mount

        await Assert.ThrowsAsync<SyncFolderException>(() => Sync(_desk));
        Assert.Equal("<p>Fog.</p>", _desk.Text(Scene("s1")));
    }

    [Fact]
    public async Task AnotherDeviceSyncing_WaitsItsTurn()
    {
        await StartWithSharedBook();
        _share.Put(SyncEngine.LockFile, JsonSerializer.Serialize(new { Device = "Tablet", Time = new DateTime(2026, 11, 2, 10, 29, 30) }));
        _desk.Put(Scene("s1"), "<p>Changed.</p>");

        var report = await Sync(_desk);
        Assert.True(report.Busy);
        Assert.Equal("<p>Fog.</p>", _share.Text(Scene("s1")));
    }

    [Fact]
    public async Task StaleLock_IsTakenOver()
    {
        await StartWithSharedBook();
        _share.Put(SyncEngine.LockFile, JsonSerializer.Serialize(new { Device = "Tablet", Time = new DateTime(2026, 11, 2, 9, 0, 0) }));
        _desk.Put(Scene("s1"), "<p>Changed.</p>");
        var report = await Sync(_desk);
        Assert.False(report.Busy);
        Assert.Equal("<p>Changed.</p>", _share.Text(Scene("s1")));
    }

    [Fact]
    public async Task NewSyncFolder_StartsAfresh_WithoutDeleting()
    {
        await StartWithSharedBook();
        var other = new MemoryStore(@"\\nas\other");
        var engine = new SyncEngine(_desk, other, Path.Combine(_tmp, "Desk"), "Desk");
        await engine.RunAsync();
        Assert.Equal("<p>Fog.</p>", other.Text(Scene("s1")));   // uploaded, not treated as deleted
        Assert.Equal("<p>Fog.</p>", _desk.Text(Scene("s1")));
    }

    // ── Real folders, with the app's own storage ─────────────────────────────

    [Fact]
    public async Task FolderStores_SyncTwoLibraries()
    {
        var deskRoot = Path.Combine(_tmp, "desk-lib");
        var tabletRoot = Path.Combine(_tmp, "tablet-lib");
        var shareRoot = Path.Combine(_tmp, "share");
        var book = new Book { Title = "Salt" };
        var chapter = new Chapter { Title = "One" };
        chapter.Scenes.Add(new Scene { Title = "Dock", Status = SceneStatus.Draft, Content = "<p>“Fog,” she said.</p>" });
        book.Chapters.Add(chapter);
        await new BookService(deskRoot).SaveBooksAsync([book]);

        var share = new FolderSyncStore(shareRoot);
        await new SyncEngine(new FolderSyncStore(deskRoot), share, Path.Combine(deskRoot, "sync"), "Desk").RunAsync();
        await new SyncEngine(new FolderSyncStore(tabletRoot), share, Path.Combine(tabletRoot, "sync"), "Tablet").RunAsync();

        var onTablet = Assert.Single(await new BookService(tabletRoot).LoadBooksAsync());
        Assert.Equal("<p>“Fog,” she said.</p>", onTablet.Chapters[0].Scenes[0].Content);
        Assert.False(Directory.Exists(Path.Combine(shareRoot, "sync")));   // device state stays on the device
    }

    // ── JSON merge ───────────────────────────────────────────────────────────

    private static JsonNode? Merge(string b, string l, string r, List<string>? conflicts = null) =>
        JsonMerge.Merge(JsonNode.Parse(b), JsonNode.Parse(l), JsonNode.Parse(r), conflicts ?? []);

    [Fact]
    public void Merge_TakesEachSidesOwnChanges()
    {
        var m = Merge("""{"Title":"A","Author":"X"}""", """{"Title":"B","Author":"X"}""", """{"Title":"A","Author":"Y"}""");
        Assert.Equal("B", m!["Title"]!.GetValue<string>());
        Assert.Equal("Y", m["Author"]!.GetValue<string>());
    }

    [Fact]
    public void Merge_SameFieldChangedTwice_KeepsLocalAndReports()
    {
        var conflicts = new List<string>();
        var m = Merge("""{"Title":"A"}""", """{"Title":"Local"}""", """{"Title":"Remote"}""", conflicts);
        Assert.Equal("Local", m!["Title"]!.GetValue<string>());
        Assert.Equal(["$.Title"], conflicts);
    }

    [Fact]
    public void Merge_StoryBibleEditsOnBothSides_MergeByCharacter()
    {
        const string b = """{"Characters":[{"Id":"m","Name":"Maya","Notes":""},{"Id":"t","Name":"Tomas","Notes":""}]}""";
        const string l = """{"Characters":[{"Id":"m","Name":"Maya","Notes":"brave"},{"Id":"t","Name":"Tomas","Notes":""}]}""";
        const string r = """{"Characters":[{"Id":"m","Name":"Maya","Notes":""},{"Id":"t","Name":"Tom","Notes":""},{"Id":"e","Name":"Erik","Notes":""}]}""";
        var chars = Merge(b, l, r)!["Characters"]!.AsArray();
        Assert.Equal(["Maya", "Tom", "Erik"], chars.Select(c => c!["Name"]!.GetValue<string>()));
        Assert.Equal("brave", chars[0]!["Notes"]!.GetValue<string>());
    }

    [Fact]
    public void Merge_ReorderedChapters_KeepTheNewOrder()
    {
        const string b = """{"Chapters":[{"Id":"1"},{"Id":"2"},{"Id":"3"}]}""";
        const string l = """{"Chapters":[{"Id":"3"},{"Id":"1"},{"Id":"2"}]}""";
        const string r = """{"Chapters":[{"Id":"1"},{"Id":"2"},{"Id":"3"},{"Id":"4"}]}""";
        var ids = Merge(b, l, r)!["Chapters"]!.AsArray().Select(c => c!["Id"]!.GetValue<string>());
        Assert.Equal(["3", "1", "2", "4"], ids);
    }

    [Fact]
    public void Merge_DeletionAppliesUnlessEditedElsewhere()
    {
        const string b = """{"Items":[{"Id":"a","N":1},{"Id":"b","N":1}]}""";
        const string l = """{"Items":[{"Id":"b","N":1}]}""";                    // a deleted here
        const string r = """{"Items":[{"Id":"a","N":1},{"Id":"b","N":2}]}""";   // b edited there
        var items = Merge(b, l, r)!["Items"]!.AsArray();
        Assert.Equal(["b"], items.Select(i => i!["Id"]!.GetValue<string>()));
        Assert.Equal(2, items[0]!["N"]!.GetValue<int>());

        const string l2 = """{"Items":[{"Id":"a","N":1}]}""";                   // b deleted here...
        var kept = Merge(b, l2, r)!["Items"]!.AsArray();                         // ...but edited there: kept
        Assert.Equal(["a", "b"], kept.Select(i => i!["Id"]!.GetValue<string>()));
    }

    [Fact]
    public void Merge_ValueListsMergeAsSets()
    {
        var m = Merge("""{"Tags":["x","y"]}""", """{"Tags":["x","y","z"]}""", """{"Tags":["y","w"]}""");
        Assert.Equal(["y", "z", "w"], m!["Tags"]!.AsArray().Select(t => t!.GetValue<string>()));
    }

    [Fact]
    public void Merge_NoCommonVersion_UnionsEverything()
    {
        var m = JsonMerge.Merge(null, JsonNode.Parse("""{"Chapters":[{"Id":"1"}]}"""), JsonNode.Parse("""{"Chapters":[{"Id":"2"}]}"""), []);
        Assert.Equal(2, m!["Chapters"]!.AsArray().Count);
    }

    // ── App side ─────────────────────────────────────────────────────────────

    private static (WriterViewModel Vm, Book Book) AppWithBook()
    {
        var book = new Book { Title = "Salt" };
        var chapter = new Chapter { Title = "One" };
        chapter.Scenes.Add(new Scene { Title = "Dock", Status = SceneStatus.Draft, Content = "<p>Fog.</p>" });
        book.Chapters.Add(chapter);
        var other = new Book { Title = "Other" };
        var vm = new WriterViewModel(new Mock<IBookService>().Object, new Mock<IImageService>().Object,
            new Mock<INlpAnalysisService>().Object, new Mock<INlpModelManager>().Object,
            new Mock<INerService>().Object, new Mock<IPosTaggingService>().Object,
            new Mock<ILocationHeuristicService>().Object, new Mock<INlpCacheService>().Object);
        vm.Books = new System.Collections.ObjectModel.ObservableCollection<Book>([book, other]);
        vm.SelectedBook = book;
        vm.SelectedChapter = chapter;
        vm.SelectedScene = chapter.Scenes[0];
        return (vm, book);
    }

    private static Book Copy(Book b) => JsonSerializer.Deserialize<Book>(JsonSerializer.Serialize(b))!;

    [Fact]
    public void Apply_SwapsChangedBooksAndKeepsYourPlace()
    {
        var (vm, book) = AppWithBook();
        var fresh = Copy(book);
        fresh.Chapters[0].Scenes[0].Content = "<p>Fog, from the tablet.</p>";
        var added = new Book { Title = "New on tablet" };

        vm.ApplySyncedBooks([fresh, vm.Books[1], added], [book.Id, added.Id], vm.SelectedScene, "<p>Fog.</p>");

        Assert.Same(fresh, vm.SelectedBook);
        Assert.Equal("<p>Fog, from the tablet.</p>", vm.SelectedScene!.Content);
        Assert.Equal(["Salt", "Other", "New on tablet"], vm.Books.Select(b => b.Title));
    }

    [Fact]
    public void Apply_KeepsWordsTypedDuringTheSync()
    {
        var (vm, book) = AppWithBook();
        var open = vm.SelectedScene!;
        open.Content = "<p>Fog. And more.</p>";   // typed while syncing
        vm.ApplySyncedBooks([Copy(book), vm.Books[1]], [book.Id], open, "<p>Fog.</p>");
        Assert.Equal("<p>Fog. And more.</p>", vm.SelectedScene!.Content);
    }

    [Fact]
    public void Apply_BookDeletedElsewhere_IsRemoved()
    {
        var (vm, book) = AppWithBook();
        var other = vm.Books[1];
        vm.ApplySyncedBooks([book], [other.Id], vm.SelectedScene, "<p>Fog.</p>");
        Assert.Equal(["Salt"], vm.Books.Select(b => b.Title));
        Assert.Same(book, vm.SelectedBook);   // untouched book stays selected as is
    }

    [Fact]
    public void Describe_SaysWhatHappened()
    {
        var report = new SyncReport { Downloaded = 2, Uploaded = 1 };
        Assert.Equal("2 files received, 1 sent.", WriterViewModel.Describe(report));
        Assert.Equal("Everything was already up to date.", WriterViewModel.Describe(new SyncReport()));
    }

    [Fact]
    public void Apply_FirstSyncOnANewDevice_OpensTheBook()
    {
        var (vm, book) = AppWithBook();
        vm.Books.Clear();
        typeof(WriterViewModel).GetField("selectedBook", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(vm, null);
        vm.ApplySyncedBooks([book], [book.Id], null, null);
        Assert.Same(book, vm.SelectedBook);
    }

    // ── SMB (Android) ────────────────────────────────────────────────────────

    [Theory]
    [InlineData(@"\nas\writing\alphaWriter", "nas", "writing", "alphaWriter")]
    [InlineData("//192.168.1.20/writing/books/alphaWriter", "192.168.1.20", "writing", @"books\alphaWriter")]
    [InlineData("smb://nas/writing", "nas", "writing", "")]
    [InlineData(@"  \nas\writing\  ", "nas", "writing", "")]
    public void Smb_ReadsShareAddresses(string address, string server, string share, string folder)
    {
        var parsed = SmbSettings.ParseAddress(address);
        Assert.Equal((server, share, folder), parsed);
    }

    [Theory]
    [InlineData(@"\nas")]
    [InlineData("")]
    [InlineData(null)]
    public void Smb_IncompleteAddress_IsRejected(string? address) => Assert.Null(SmbSettings.ParseAddress(address));

    [Fact]
    public void Smb_MapsSyncPathsIntoTheShareFolder()
    {
        var store = new SmbSyncStore(new SmbSettings("nas", "writing", @"books\alphaWriter", "ann", "pw"));
        Assert.Equal(@"books\alphaWriter\books\b1\scenes\s1.html", store.SharePath("books/b1/scenes/s1.html"));
        Assert.Equal(@"books\alphaWriter", store.SharePath(""));
        Assert.Equal("smb://nas/writing/books/alphaWriter", store.Location);
        Assert.Equal(@"books\b1\book.json", new SmbSyncStore(new SmbSettings("nas", "writing", "", "", "")).SharePath("books/b1/book.json"));
    }

    [Theory]
    [InlineData(@"HOME\ann", "HOME", "ann")]
    [InlineData("ann", "", "ann")]
    public void Smb_SplitsDomainFromUser(string user, string domain, string name) =>
        Assert.Equal((domain, name), new SmbSettings("nas", "s", "", user, "pw").Account);

    [Fact]
    public async Task Smb_UnknownServer_GivesAPlainMessage()
    {
        using var store = new SmbSyncStore(new SmbSettings("no-such-nas.invalid", "writing", "", "ann", "pw"));
        var ex = await Assert.ThrowsAsync<IOException>(() => store.ListAsync("books"));
        Assert.Contains("Couldn't reach no-such-nas.invalid", ex.Message);
    }

    [Fact]
    public void Settings_ServerModeNeedsAShareAddress()
    {
        var (vm, _) = AppWithBook();
        vm.SyncMode = "SMB server";
        vm.SmbAddress = "nas";
        var (store, problem) = vm.CreateRemoteStore();
        Assert.Null(store);
        Assert.Contains(@"\server\share\folder", problem);

        vm.SmbAddress = @"\nas\writing\alphaWriter";
        var (smb, _) = vm.CreateRemoteStore();
        Assert.IsType<SmbSyncStore>(smb);
        Assert.Equal("smb://nas/writing/alphaWriter", smb!.Location);
    }

    [Fact]
    public void Settings_FolderModeChecksTheFolderIsThere()
    {
        var (vm, _) = AppWithBook();
        vm.SyncMode = "Folder";
        vm.SyncFolder = Path.Combine(_tmp, "missing");
        Assert.StartsWith("Offline", vm.CreateRemoteStore().Problem);
        Directory.CreateDirectory(vm.SyncFolder);
        Assert.IsType<FolderSyncStore>(vm.CreateRemoteStore().Store);
    }
}
