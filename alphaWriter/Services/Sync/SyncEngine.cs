using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace alphaWriter.Services.Sync
{
    /// <summary>What a sync did.</summary>
    public sealed class SyncReport
    {
        public int Uploaded { get; set; }
        public int Downloaded { get; set; }
        public int Deleted { get; set; }

        /// <summary>Books whose files on this device changed, so the app must reload them.</summary>
        public HashSet<string> ChangedLocalBooks { get; } = new(StringComparer.Ordinal);

        /// <summary>Plain-language notes: conflicted copies made, settings kept from this device.</summary>
        public List<string> Notes { get; } = [];

        /// <summary>Another device was syncing; nothing was done.</summary>
        public bool Busy { get; set; }

        public bool ChangedAnything => Uploaded + Downloaded + Deleted > 0;
    }

    /// <summary>The sync folder can't be trusted (e.g. it looks empty after syncing before).</summary>
    public sealed class SyncFolderException(string message) : Exception(message);

    /// <summary>
    /// Two-way sync of the library's book folders between this device and a shared folder.
    /// Each file is compared three ways: this device's copy, the shared copy, and what both
    /// were at the last sync (the "base", kept per device). A side that changed wins; when
    /// both changed, book.json is merged (<see cref="JsonMerge"/>) and a scene keeps this
    /// device's text while the other version becomes a "conflicted copy" scene beside it, so
    /// no writing is ever lost.
    /// </summary>
    public sealed class SyncEngine
    {
        public const string BooksPrefix = "books";
        public const string TombstonePrefix = "deleted-books";
        public const string MarkerFile = "alphawriter-library.json";
        public const string LockFile = "sync.lock";
        private static readonly TimeSpan LockTimeout = TimeSpan.FromMinutes(2);

        private readonly ISyncStore _local;
        private readonly ISyncStore _remote;
        private readonly string _stateDir;
        private readonly string _device;
        private readonly Func<DateTime> _now;
        private SyncState _state = new();

        public SyncEngine(ISyncStore local, ISyncStore remote, string stateDir, string deviceName, Func<DateTime>? now = null)
        {
            _local = local;
            _remote = remote;
            _stateDir = stateDir;
            _device = string.IsNullOrWhiteSpace(deviceName) ? "another device" : deviceName.Trim();
            _now = now ?? (() => DateTime.Now);
        }

        // ── State kept on this device ────────────────────────────────────────

        private sealed class FileState
        {
            public string Hash { get; set; } = string.Empty;
            public long RemoteSize { get; set; }
            public DateTime RemoteModifiedUtc { get; set; }
        }

        private sealed class SyncState
        {
            public string Remote { get; set; } = string.Empty;
            public Dictionary<string, FileState> Files { get; set; } = new(StringComparer.Ordinal);
        }

        private string StatePath => Path.Combine(_stateDir, "state.json");
        private string BasePath(string bookId) => Path.Combine(_stateDir, "base", bookId + ".json");

        private void LoadState()
        {
            try
            {
                _state = File.Exists(StatePath)
                    ? JsonSerializer.Deserialize<SyncState>(File.ReadAllText(StatePath)) ?? new()
                    : new();
            }
            catch { _state = new(); }

            // Synced with a different folder before: start afresh (nothing is deleted on a first sync)
            if (!string.Equals(_state.Remote, _remote.Location, StringComparison.OrdinalIgnoreCase))
            {
                _state = new SyncState { Remote = _remote.Location };
                var baseDir = Path.Combine(_stateDir, "base");
                if (Directory.Exists(baseDir)) Directory.Delete(baseDir, recursive: true);
            }
        }

        private void SaveState()
        {
            Directory.CreateDirectory(_stateDir);
            File.WriteAllText(StatePath, JsonSerializer.Serialize(_state));
        }

        // ── Run ──────────────────────────────────────────────────────────────

        public async Task<SyncReport> RunAsync(CancellationToken ct = default)
        {
            var report = new SyncReport();
            LoadState();
            await CheckMarkerAsync(ct);
            if (!await TryLockAsync(ct)) { report.Busy = true; return report; }
            try
            {
                var local = Group(await _local.ListAsync(BooksPrefix, ct));
                var remote = Group(await _remote.ListAsync(BooksPrefix, ct));
                var tombstones = (await _remote.ListAsync(TombstonePrefix, ct))
                    .Select(e => e.Path.Split('/')[^1]).ToHashSet(StringComparer.Ordinal);
                var known = _state.Files.Keys.Select(BookIdOf).Where(id => id is not null).Select(id => id!);

                foreach (var bookId in local.Keys.Concat(remote.Keys).Concat(known).Distinct().ToList())
                {
                    ct.ThrowIfCancellationRequested();
                    local.TryGetValue(bookId, out var l);
                    remote.TryGetValue(bookId, out var r);
                    await SyncBookAsync(bookId, l ?? [], r ?? [], tombstones.Contains(bookId), report, ct);
                }
                SaveState();
            }
            finally
            {
                await _remote.DeleteAsync(LockFile, ct);
            }
            return report;
        }

        /// <summary>
        /// The shared folder carries a marker. If it's missing after syncing with this folder
        /// before, the path or share is wrong: syncing against it would look like every book
        /// was deleted, so stop instead.
        /// </summary>
        private async Task CheckMarkerAsync(CancellationToken ct)
        {
            if (await _remote.ReadAsync(MarkerFile, ct) is not null) return;
            if (_state.Files.Count > 0)
                throw new SyncFolderException(
                    "The sync folder looks empty or has moved, so nothing was synced. Check that the share is connected and the folder is right.");
            await _remote.WriteAsync(MarkerFile, Encoding.UTF8.GetBytes(
                JsonSerializer.Serialize(new { app = "alphaWriter", format = 1, createdBy = _device, created = _now() })), ct);
        }

        private async Task<bool> TryLockAsync(CancellationToken ct)
        {
            if (await _remote.ReadAsync(LockFile, ct) is { } existing)
            {
                try
                {
                    var held = JsonSerializer.Deserialize<LockInfo>(existing);
                    if (held is not null && held.Device != _device && _now() - held.Time < LockTimeout) return false;
                }
                catch { /* unreadable lock: treat as stale */ }
            }
            await _remote.WriteAsync(LockFile, JsonSerializer.SerializeToUtf8Bytes(new LockInfo(_device, _now())), ct);
            return true;
        }

        private sealed record LockInfo(string Device, DateTime Time);

        // ── Books ────────────────────────────────────────────────────────────

        private static string? BookIdOf(string path)
        {
            var parts = path.Split('/');
            return parts.Length >= 3 && parts[0] == BooksPrefix ? parts[1] : null;
        }

        private static Dictionary<string, Dictionary<string, SyncEntry>> Group(IEnumerable<SyncEntry> entries) =>
            entries.Where(e => BookIdOf(e.Path) is not null)
                .GroupBy(e => BookIdOf(e.Path)!)
                .ToDictionary(g => g.Key, g => g.ToDictionary(e => e.Path, StringComparer.Ordinal), StringComparer.Ordinal);

        private async Task SyncBookAsync(string bookId, Dictionary<string, SyncEntry> local, Dictionary<string, SyncEntry> remote,
            bool tombstoned, SyncReport report, CancellationToken ct)
        {
            var prefix = $"{BooksPrefix}/{bookId}/";
            var baseFiles = _state.Files.Where(kv => kv.Key.StartsWith(prefix, StringComparison.Ordinal)).ToDictionary(kv => kv.Key, kv => kv.Value);

            // Deleted on this device since the last sync, and untouched elsewhere: delete everywhere
            if (local.Count == 0 && baseFiles.Count > 0 && remote.Count > 0)
            {
                if (await RemoteUnchangedAsync(remote, baseFiles, ct))
                {
                    foreach (var path in remote.Keys) { await _remote.DeleteAsync(path, ct); report.Deleted++; }
                    await _remote.WriteAsync($"{TombstonePrefix}/{bookId}", Encoding.UTF8.GetBytes($"{_device} {_now():O}"), ct);
                    Forget(prefix, bookId);
                    return;
                }
                Forget(prefix, bookId);   // changed elsewhere meanwhile: bring it back
                baseFiles.Clear();
            }

            if (remote.Count == 0 && local.Count > 0)
            {
                // Deleted on another device, and not edited here since: delete here too
                if (tombstoned && baseFiles.Count > 0 && await LocalUnchangedAsync(local, baseFiles, ct))
                {
                    foreach (var path in local.Keys) { await _local.DeleteAsync(path, ct); report.Deleted++; }
                    report.ChangedLocalBooks.Add(bookId);
                    Forget(prefix, bookId);
                    return;
                }
                // Otherwise the shared copy is missing (or was deleted while edited here): upload it all
                if (tombstoned) await _remote.DeleteAsync($"{TombstonePrefix}/{bookId}", ct);
                Forget(prefix, bookId);
                baseFiles.Clear();
            }

            if (local.Count == 0 && remote.Count == 0) { Forget(prefix, bookId); return; }
            if (local.Count > 0 && tombstoned) await _remote.DeleteAsync($"{TombstonePrefix}/{bookId}", ct);

            var conflictedScenes = await ReconcileFilesAsync(bookId, local, remote, baseFiles, report, ct);
            if (conflictedScenes.Count > 0)
            {
                // Add the other device's versions as scenes, then sync the updated book once more
                await AddConflictedScenesAsync(bookId, conflictedScenes, report, ct);
                var l2 = Group(await _local.ListAsync($"{BooksPrefix}/{bookId}", ct)).GetValueOrDefault(bookId) ?? [];
                var r2 = Group(await _remote.ListAsync($"{BooksPrefix}/{bookId}", ct)).GetValueOrDefault(bookId) ?? [];
                var b2 = _state.Files.Where(kv => kv.Key.StartsWith(prefix, StringComparison.Ordinal)).ToDictionary(kv => kv.Key, kv => kv.Value);
                await ReconcileFilesAsync(bookId, l2, r2, b2, report, ct);
            }
        }

        private void Forget(string prefix, string bookId)
        {
            foreach (var key in _state.Files.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).ToList())
                _state.Files.Remove(key);
            if (File.Exists(BasePath(bookId))) File.Delete(BasePath(bookId));
        }

        private async Task<bool> RemoteUnchangedAsync(Dictionary<string, SyncEntry> remote, Dictionary<string, FileState> baseFiles, CancellationToken ct)
        {
            if (!remote.Keys.ToHashSet().SetEquals(baseFiles.Keys)) return false;
            foreach (var (path, entry) in remote)
                if ((await RemoteHashAsync(entry, ct)).Hash != baseFiles[path].Hash) return false;
            return true;
        }

        private async Task<bool> LocalUnchangedAsync(Dictionary<string, SyncEntry> local, Dictionary<string, FileState> baseFiles, CancellationToken ct)
        {
            if (!local.Keys.ToHashSet().SetEquals(baseFiles.Keys)) return false;
            foreach (var path in local.Keys)
                if (Hash(await _local.ReadAsync(path, ct)) != baseFiles[path].Hash) return false;
            return true;
        }

        // ── Files ────────────────────────────────────────────────────────────

        /// <summary>Reconciles one book's files; returns scenes edited on both sides (scene id → the other device's text).</summary>
        private async Task<Dictionary<string, byte[]>> ReconcileFilesAsync(string bookId,
            Dictionary<string, SyncEntry> local, Dictionary<string, SyncEntry> remote, Dictionary<string, FileState> baseFiles,
            SyncReport report, CancellationToken ct)
        {
            var conflicts = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            var paths = local.Keys.Concat(remote.Keys).Concat(baseFiles.Keys).Distinct(StringComparer.Ordinal).ToList();

            foreach (var path in paths)
            {
                ct.ThrowIfCancellationRequested();
                byte[]? lData = local.ContainsKey(path) ? await _local.ReadAsync(path, ct) : null;
                var lHash = lData is null ? null : Hash(lData);
                var (rHash, rData) = remote.TryGetValue(path, out var rEntry) ? await RemoteHashAsync(rEntry, ct) : (null, null);
                var bHash = baseFiles.TryGetValue(path, out var b) ? b.Hash : null;

                if (lHash == rHash)
                {
                    Record(path, lHash, rEntry);
                    if (lHash is not null && IsBookFile(path)) SaveBase(bookId, lData!);
                }
                else if (lHash == bHash)
                {
                    // Changed (or deleted) only on the other device: bring it here
                    rData ??= rHash is null ? null : await _remote.ReadAsync(path, ct);
                    if (rData is null) { await _local.DeleteAsync(path, ct); report.Deleted++; Record(path, null, null); }
                    else { await _local.WriteAsync(path, rData, ct); report.Downloaded++; Record(path, rHash, rEntry); if (IsBookFile(path)) SaveBase(bookId, rData); }
                    report.ChangedLocalBooks.Add(bookId);
                }
                else if (rHash == bHash)
                {
                    // Changed (or deleted) only here: send it
                    if (lData is null) { await _remote.DeleteAsync(path, ct); report.Deleted++; Record(path, null, null); }
                    else { var written = await _remote.WriteAsync(path, lData, ct); report.Uploaded++; Record(path, lHash, written); if (IsBookFile(path)) SaveBase(bookId, lData); }
                }
                else
                {
                    rData ??= rHash is null ? null : await _remote.ReadAsync(path, ct);
                    await ResolveConflictAsync(bookId, path, lData, rData, conflicts, report, ct);
                }
            }
            return conflicts;
        }

        private async Task ResolveConflictAsync(string bookId, string path, byte[]? lData, byte[]? rData,
            Dictionary<string, byte[]> conflicts, SyncReport report, CancellationToken ct)
        {
            // Deleted on one side, edited on the other: keep the edit
            if (lData is null || rData is null)
            {
                var keep = (lData ?? rData)!;
                if (lData is null) { await _local.WriteAsync(path, keep, ct); report.Downloaded++; report.ChangedLocalBooks.Add(bookId); }
                var written = await _remote.WriteAsync(path, keep, ct);
                if (rData is null) report.Uploaded++;
                Record(path, Hash(keep), written);
                if (IsBookFile(path)) SaveBase(bookId, keep);
                return;
            }

            byte[] result;
            if (IsBookFile(path))
            {
                var noted = new List<string>();
                var merged = JsonMerge.Merge(LoadBase(bookId), JsonNode.Parse(lData), JsonNode.Parse(rData), noted);
                result = Encoding.UTF8.GetBytes(merged!.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
                if (noted.Count > 0)
                    report.Notes.Add($"{noted.Count} book setting{(noted.Count == 1 ? " was" : "s were")} changed on two devices; this device's value was kept.");
            }
            else
            {
                // A scene (or picture) changed on both: this device's version stays; a scene's other version is kept beside it
                result = lData;
                if (path.Contains("/scenes/", StringComparison.Ordinal))
                    conflicts[Path.GetFileNameWithoutExtension(path)] = rData;
            }

            await _local.WriteAsync(path, result, ct);
            var remoteEntry = await _remote.WriteAsync(path, result, ct);
            report.Uploaded++;
            if (!result.AsSpan().SequenceEqual(lData)) { report.Downloaded++; report.ChangedLocalBooks.Add(bookId); }
            Record(path, Hash(result), remoteEntry);
            if (IsBookFile(path)) SaveBase(bookId, result);
        }

        /// <summary>Inserts each conflicted version as a new scene right after the original, in this device's book.json.</summary>
        private async Task AddConflictedScenesAsync(string bookId, Dictionary<string, byte[]> conflicts, SyncReport report, CancellationToken ct)
        {
            var bookPath = $"{BooksPrefix}/{bookId}/book.json";
            if (await _local.ReadAsync(bookPath, ct) is not { } bookData || JsonNode.Parse(bookData) is not JsonObject book) return;
            var stamp = _now().ToString("d MMM yyyy, HH:mm", System.Globalization.CultureInfo.InvariantCulture);

            foreach (var (sceneId, otherText) in conflicts)
            {
                foreach (var chapter in book["Chapters"]?.AsArray() ?? [])
                {
                    if (chapter?["Scenes"] is not JsonArray scenes) continue;
                    var index = scenes.Select((s, i) => (s, i)).FirstOrDefault(x => x.s?["Id"]?.GetValue<string>() == sceneId).i;
                    if (scenes.Count == 0 || scenes[index]?["Id"]?.GetValue<string>() != sceneId) continue;

                    var copy = scenes[index]!.DeepClone().AsObject();
                    var newId = Guid.NewGuid().ToString();
                    copy["Id"] = newId;
                    copy["Title"] = $"{copy["Title"]?.GetValue<string>()} (conflicted copy, {stamp})";
                    scenes.Insert(index + 1, copy);
                    await _local.WriteAsync($"{BooksPrefix}/{bookId}/scenes/{newId}.html", otherText, ct);
                    report.Notes.Add($"“{scenes[index]?["Title"]?.GetValue<string>()}” was edited on two devices. The other version is kept as a conflicted copy right after it.");
                    break;
                }
            }
            await _local.WriteAsync(bookPath, Encoding.UTF8.GetBytes(book.ToJsonString(new JsonSerializerOptions { WriteIndented = true })), ct);
            report.ChangedLocalBooks.Add(bookId);
        }

        private static bool IsBookFile(string path) => path.EndsWith("/book.json", StringComparison.Ordinal);

        private void SaveBase(string bookId, byte[] data)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(BasePath(bookId))!);
            File.WriteAllBytes(BasePath(bookId), data);
        }

        private JsonNode? LoadBase(string bookId)
        {
            try { return File.Exists(BasePath(bookId)) ? JsonNode.Parse(File.ReadAllBytes(BasePath(bookId))) : null; }
            catch { return null; }
        }

        private void Record(string path, string? hash, SyncEntry? remote)
        {
            if (hash is null) { _state.Files.Remove(path); return; }
            _state.Files[path] = new FileState
            {
                Hash = hash,
                RemoteSize = remote?.Size ?? 0,
                RemoteModifiedUtc = remote?.ModifiedUtc ?? default
            };
        }

        /// <summary>The shared file's hash, reusing the last one when its size and time haven't changed.</summary>
        private async Task<(string? Hash, byte[]? Data)> RemoteHashAsync(SyncEntry entry, CancellationToken ct)
        {
            if (_state.Files.TryGetValue(entry.Path, out var known)
                && known.RemoteSize == entry.Size && known.RemoteModifiedUtc == entry.ModifiedUtc && entry.ModifiedUtc != default)
                return (known.Hash, null);
            var data = await _remote.ReadAsync(entry.Path, ct);
            return (data is null ? null : Hash(data), data);
        }

        internal static string? Hash(byte[]? data) => data is null ? null : Convert.ToHexString(SHA256.HashData(data));
    }
}
