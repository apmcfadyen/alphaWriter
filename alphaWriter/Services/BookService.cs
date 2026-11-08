using alphaWriter.Models;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace alphaWriter.Services
{
    /// <summary>
    /// Stores each book in its own folder, with one file per scene, so edits on different
    /// devices touch different files and a save only rewrites what changed:
    /// <code>
    /// books/&lt;bookId&gt;/book.json              metadata, chapters and scenes (no scene text),
    ///                                        Story Bible, style sheet, analysis history
    /// books/&lt;bookId&gt;/scenes/&lt;sceneId&gt;.html  one scene's text
    /// books/&lt;bookId&gt;/images/                character and location pictures
    /// </code>
    /// The single <c>books.json</c> used before is migrated on first load.
    /// </summary>
    public class BookService : IBookService
    {
        public const string BooksFolder = "books";
        public const string BookFile = "book.json";
        public const string ScenesFolder = "scenes";
        private const string LegacyFile = "books.json";
        private const string OrderFile = "library.json";

        private readonly string _root;
        private readonly SemaphoreSlim _gate = new(1, 1);

        /// <summary>Path → hash of what is on disk, so unchanged files are not rewritten.</summary>
        private readonly Dictionary<string, string> _written = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Books this service has loaded or saved: only these folders may ever be deleted.</summary>
        private readonly HashSet<string> _knownBookIds = new(StringComparer.Ordinal);

        private static readonly JsonSerializerOptions LegacyOptions = new() { WriteIndented = true };

        /// <summary>book.json leaves scene text out: it lives in the scene files.</summary>
        private static readonly JsonSerializerOptions BookOptions = new()
        {
            WriteIndented = true,
            TypeInfoResolver = new DefaultJsonTypeInfoResolver
            {
                Modifiers =
                {
                    info =>
                    {
                        if (info.Type != typeof(Scene)) return;
                        foreach (var property in info.Properties.Where(p => p.Name == nameof(Scene.Content)).ToList())
                            info.Properties.Remove(property);
                    }
                }
            }
        };

        public BookService() : this(AppPaths.DataDirectory) { }

        /// <summary>For tests: a library rooted somewhere other than the app data folder.</summary>
        public BookService(string root) => _root = root;

        private string BooksRoot => Path.Combine(_root, BooksFolder);

        public async Task<List<Book>> LoadBooksAsync()
        {
            await _gate.WaitAsync();
            try
            {
                return await LoadCoreAsync();
            }
            finally
            {
                _gate.Release();
            }
        }

        public async Task<List<Book>?> RunExclusiveAsync(Func<Task<bool>> work)
        {
            await _gate.WaitAsync();
            try
            {
                return await work() ? await LoadCoreAsync() : null;
            }
            finally
            {
                _gate.Release();
            }
        }

        private async Task<List<Book>> LoadCoreAsync()
        {
            if (File.Exists(Path.Combine(_root, LegacyFile)) && !Directory.Exists(BooksRoot))
                MigrateLegacyLibrary();

            // Migration couldn't be verified: keep using the old file untouched
            if (!Directory.Exists(BooksRoot))
                return await LoadLegacyAsync();

            var books = new List<Book>();
            _knownBookIds.Clear();
            _written.Clear();
            foreach (var dir in Directory.EnumerateDirectories(BooksRoot))
            {
                var book = TryLoadBook(dir);
                if (book is null) continue;   // unreadable: left alone, never deleted
                books.Add(book);
                _knownBookIds.Add(book.Id);
            }
            return OrderBooks(books);
        }

        public async Task SaveBooksAsync(List<Book> books)
        {
            await _gate.WaitAsync();
            try
            {
                if (!Directory.Exists(BooksRoot) && File.Exists(Path.Combine(_root, LegacyFile)))
                {
                    // Still on the old format (migration failed): save the way we loaded
                    await File.WriteAllTextAsync(Path.Combine(_root, LegacyFile) + ".tmp", JsonSerializer.Serialize(books, LegacyOptions));
                    File.Move(Path.Combine(_root, LegacyFile) + ".tmp", Path.Combine(_root, LegacyFile), overwrite: true);
                    return;
                }

                foreach (var book in books)
                {
                    WriteBook(book);
                    _knownBookIds.Add(book.Id);
                }

                // Books deleted in the app: remove their folders (only ones this service knows)
                var current = books.Select(b => b.Id).ToHashSet(StringComparer.Ordinal);
                foreach (var gone in _knownBookIds.Where(id => !current.Contains(id)).ToList())
                {
                    var dir = Path.Combine(BooksRoot, gone);
                    if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
                    _knownBookIds.Remove(gone);
                    foreach (var key in _written.Keys.Where(k => k.StartsWith(dir, StringComparison.OrdinalIgnoreCase)).ToList())
                        _written.Remove(key);
                }

                WriteIfChanged(Path.Combine(_root, OrderFile),
                    JsonSerializer.Serialize(new LibraryOrder(books.Select(b => b.Id).ToList()), LegacyOptions));
            }
            finally
            {
                _gate.Release();
            }
        }

        /// <summary>The folder holding a book's files (and its images).</summary>
        public static string BookFolder(string root, string bookId) => Path.Combine(root, BooksFolder, bookId);

        // ── Reading ──────────────────────────────────────────────────────────

        private Book? TryLoadBook(string dir)
        {
            var bookPath = Path.Combine(dir, BookFile);
            if (!File.Exists(bookPath)) return null;
            try
            {
                var json = File.ReadAllText(bookPath);
                var book = JsonSerializer.Deserialize<Book>(json, BookOptions);
                if (book is null) return null;
                _written[bookPath] = Hash(json);

                foreach (var scene in book.Chapters.SelectMany(c => c.Scenes))
                {
                    var scenePath = ScenePath(dir, scene.Id);
                    if (!File.Exists(scenePath)) continue;
                    var html = File.ReadAllText(scenePath);
                    scene.Content = html;
                    _written[scenePath] = Hash(html);
                }
                return book;
            }
            catch
            {
                return null;
            }
        }

        private async Task<List<Book>> LoadLegacyAsync()
        {
            var path = Path.Combine(_root, LegacyFile);
            if (!File.Exists(path)) return [];
            try
            {
                return JsonSerializer.Deserialize<List<Book>>(await File.ReadAllTextAsync(path), LegacyOptions) ?? [];
            }
            catch
            {
                return [];
            }
        }

        private sealed record LibraryOrder(List<string> BookIds);

        /// <summary>Books in the order last saved on this device; new ones (e.g. synced in) at the end.</summary>
        private List<Book> OrderBooks(List<Book> books)
        {
            var orderPath = Path.Combine(_root, OrderFile);
            List<string> order = [];
            try
            {
                if (File.Exists(orderPath))
                    order = JsonSerializer.Deserialize<LibraryOrder>(File.ReadAllText(orderPath))?.BookIds ?? [];
            }
            catch { /* order is a nicety */ }
            return books
                .OrderBy(b => order.IndexOf(b.Id) is var i and >= 0 ? i : int.MaxValue)
                .ThenBy(b => b.Title, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }

        // ── Writing ──────────────────────────────────────────────────────────

        private void WriteBook(Book book)
        {
            var dir = BookFolder(_root, book.Id);
            Directory.CreateDirectory(Path.Combine(dir, ScenesFolder));
            WriteIfChanged(Path.Combine(dir, BookFile), JsonSerializer.Serialize(book, BookOptions));

            var sceneIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var scene in book.Chapters.SelectMany(c => c.Scenes))
            {
                sceneIds.Add(scene.Id);
                WriteIfChanged(ScenePath(dir, scene.Id), scene.Content ?? string.Empty);
            }

            // Scenes deleted in the app
            foreach (var file in Directory.EnumerateFiles(Path.Combine(dir, ScenesFolder), "*.html"))
            {
                if (sceneIds.Contains(Path.GetFileNameWithoutExtension(file))) continue;
                File.Delete(file);
                _written.Remove(file);
            }
        }

        private static string ScenePath(string bookDir, string sceneId) =>
            Path.Combine(bookDir, ScenesFolder, sceneId + ".html");

        /// <summary>Writes through a temporary file, and only when the content differs from what was last written.</summary>
        private void WriteIfChanged(string path, string content)
        {
            var hash = Hash(content);
            if (_written.TryGetValue(path, out var known) && known == hash && File.Exists(path)) return;
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, content, new UTF8Encoding(false));
            File.Move(tmp, path, overwrite: true);
            _written[path] = hash;
        }

        internal static string Hash(string content) =>
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content)));

        // ── Migration from books.json ────────────────────────────────────────

        /// <summary>
        /// Splits books.json into book folders (pictures included) in a staging folder, checks
        /// that every book reads back identically, then switches over and keeps the old file as
        /// books.json.migrated. If anything doesn't match, nothing changes and the old file
        /// stays in use. The old images/ folder is left in place as a backup.
        /// </summary>
        private void MigrateLegacyLibrary()
        {
            var legacyPath = Path.Combine(_root, LegacyFile);
            List<Book> books;
            try
            {
                books = JsonSerializer.Deserialize<List<Book>>(File.ReadAllText(legacyPath), LegacyOptions) ?? [];
            }
            catch
            {
                return;   // unreadable: leave it for the legacy loader
            }

            var staging = Path.Combine(_root, "migrating");
            try
            {
                if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
                Directory.CreateDirectory(staging);
                foreach (var book in books)
                    CopyImagesIntoBook(book, staging);

                new BookService(staging).SaveBooksAsync(books).GetAwaiter().GetResult();
                var reloaded = new BookService(staging).LoadBooksAsync().GetAwaiter().GetResult();
                var expected = books.Select(b => JsonSerializer.Serialize(b, LegacyOptions)).Order();
                var actual = reloaded.Select(b => JsonSerializer.Serialize(b, LegacyOptions)).Order();
                if (!expected.SequenceEqual(actual)) return;

                Directory.Move(Path.Combine(staging, BooksFolder), BooksRoot);
                File.Move(Path.Combine(staging, OrderFile), Path.Combine(_root, OrderFile), overwrite: true);
                File.Move(legacyPath, legacyPath + ".migrated", overwrite: true);
            }
            catch
            {
                // Leave the old file in use; the next start tries again
            }
            finally
            {
                try { if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true); }
                catch { }
            }
        }

        /// <summary>
        /// Copies a book's pictures from images/&lt;bookId&gt;/ into its folder under
        /// <paramref name="stagingRoot"/>, and repoints them at where they'll live after the switch.
        /// </summary>
        private void CopyImagesIntoBook(Book book, string stagingRoot)
        {
            var newRel = Path.Combine(BooksFolder, book.Id, "images");

            string Repoint(string relativePath)
            {
                if (string.IsNullOrEmpty(relativePath)) return relativePath;
                var source = Path.Combine(_root, relativePath);
                if (!File.Exists(source)) return relativePath;
                var name = Path.GetFileName(relativePath);
                Directory.CreateDirectory(Path.Combine(stagingRoot, newRel));
                File.Copy(source, Path.Combine(stagingRoot, newRel, name), overwrite: true);
                return Path.Combine(newRel, name);
            }

            foreach (var c in book.Characters) c.ImagePath = Repoint(c.ImagePath);
            foreach (var l in book.Locations) l.ImagePath = Repoint(l.ImagePath);
        }
    }
}
