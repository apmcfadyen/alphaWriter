namespace alphaWriter.Services.Sync
{
    /// <summary>A file in a sync store: path relative to its root, with '/' separators.</summary>
    public sealed record SyncEntry(string Path, long Size, DateTime ModifiedUtc);

    /// <summary>
    /// Somewhere the library's files can be listed, read and written: this device's library
    /// folder, or the shared sync folder (an SMB share, a mapped drive, any folder).
    /// </summary>
    public interface ISyncStore
    {
        /// <summary>Identifies the store, e.g. its path. Sync state is kept per remote.</summary>
        string Location { get; }

        /// <summary>Every file under <paramref name="prefix"/> (recursively). Empty when the folder doesn't exist.</summary>
        Task<IReadOnlyList<SyncEntry>> ListAsync(string prefix, CancellationToken ct = default);

        /// <summary>A file's contents, or null when it doesn't exist.</summary>
        Task<byte[]?> ReadAsync(string path, CancellationToken ct = default);

        /// <summary>Writes a file in one step (temporary file, then rename) and returns its new listing.</summary>
        Task<SyncEntry> WriteAsync(string path, byte[] data, CancellationToken ct = default);

        Task DeleteAsync(string path, CancellationToken ct = default);
    }

    /// <summary>A folder on disk or on a network share (\\server\share\folder, a mapped drive).</summary>
    public sealed class FolderSyncStore(string root) : ISyncStore
    {
        public string Location => root;

        private string Full(string path) => System.IO.Path.Combine(root, path.Replace('/', System.IO.Path.DirectorySeparatorChar));

        public Task<IReadOnlyList<SyncEntry>> ListAsync(string prefix, CancellationToken ct = default) => Task.Run(() =>
        {
            var dir = Full(prefix);
            if (!Directory.Exists(dir)) return (IReadOnlyList<SyncEntry>)[];
            return Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
                .Where(f => !f.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
                .Select(f => new FileInfo(f))
                .Select(fi => new SyncEntry(
                    System.IO.Path.GetRelativePath(root, fi.FullName).Replace(System.IO.Path.DirectorySeparatorChar, '/'),
                    fi.Length, fi.LastWriteTimeUtc))
                .ToList() as IReadOnlyList<SyncEntry>;
        }, ct);

        public async Task<byte[]?> ReadAsync(string path, CancellationToken ct = default)
        {
            var full = Full(path);
            return File.Exists(full) ? await File.ReadAllBytesAsync(full, ct) : null;
        }

        public async Task<SyncEntry> WriteAsync(string path, byte[] data, CancellationToken ct = default)
        {
            var full = Full(path);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
            var tmp = full + ".tmp";
            await File.WriteAllBytesAsync(tmp, data, ct);
            File.Move(tmp, full, overwrite: true);
            var fi = new FileInfo(full);
            return new SyncEntry(path, fi.Length, fi.LastWriteTimeUtc);
        }

        public Task DeleteAsync(string path, CancellationToken ct = default)
        {
            var full = Full(path);
            if (File.Exists(full)) File.Delete(full);
            // Tidy up folders left empty (a deleted book), but never the root
            var dir = System.IO.Path.GetDirectoryName(full);
            var rootFull = System.IO.Path.GetFullPath(root).TrimEnd(System.IO.Path.DirectorySeparatorChar);
            while (dir is not null
                   && System.IO.Path.GetFullPath(dir).TrimEnd(System.IO.Path.DirectorySeparatorChar).Length > rootFull.Length
                   && Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any())
            {
                Directory.Delete(dir);
                dir = System.IO.Path.GetDirectoryName(dir);
            }
            return Task.CompletedTask;
        }
    }
}
