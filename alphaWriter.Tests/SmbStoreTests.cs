using System.Text;
using alphaWriter.Services.Sync;
using SMBLibrary;
using SMBLibrary.Client;
using Xunit;
using FileAttributes = SMBLibrary.FileAttributes;

namespace alphaWriter.Tests;

/// <summary>
/// An in-memory SMB share that answers the way SMBLibrary does, quirks included: a folder
/// listing comes back with STATUS_NO_MORE_FILES alongside the entries, reads end with
/// STATUS_END_OF_FILE, and deletes happen when the handle closes.
/// </summary>
internal sealed class FakeSmbShare : ISMBFileStore
{
    private sealed class Handle(string path, bool isDirectory)
    {
        public string Path { get; set; } = path;
        public bool IsDirectory { get; } = isDirectory;
        public bool DeleteOnClose { get; set; }
    }

    public Dictionary<string, (byte[] Data, DateTime Modified)> Files { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> Folders { get; } = new(StringComparer.OrdinalIgnoreCase) { "" };
    private int _tick;

    /// <summary>Tiny chunks, so reading and writing in pieces is exercised.</summary>
    public uint MaxReadSize => 7;
    public uint MaxWriteSize => 5;

    private static string Parent(string path) => path.Contains('\\') ? path[..path.LastIndexOf('\\')] : "";

    public NTStatus CreateFile(out object handle, out FileStatus fileStatus, string path, AccessMask desiredAccess,
        FileAttributes fileAttributes, ShareAccess shareAccess, CreateDisposition createDisposition,
        CreateOptions createOptions, SecurityContext securityContext)
    {
        handle = null!;
        fileStatus = FileStatus.FILE_OPENED;
        if (!Folders.Contains(Parent(path))) return NTStatus.STATUS_OBJECT_PATH_NOT_FOUND;

        if ((createOptions & CreateOptions.FILE_DIRECTORY_FILE) != 0)
        {
            if (!Folders.Contains(path))
            {
                if (createDisposition != CreateDisposition.FILE_OPEN_IF) return NTStatus.STATUS_OBJECT_NAME_NOT_FOUND;
                Folders.Add(path);
                fileStatus = FileStatus.FILE_CREATED;
            }
            handle = new Handle(path, true);
            return NTStatus.STATUS_SUCCESS;
        }

        if (!Files.ContainsKey(path))
        {
            if (createDisposition == CreateDisposition.FILE_OPEN) return NTStatus.STATUS_OBJECT_NAME_NOT_FOUND;
            Files[path] = ([], Now());
            fileStatus = FileStatus.FILE_CREATED;
        }
        else if (createDisposition == CreateDisposition.FILE_OVERWRITE_IF)
        {
            Files[path] = ([], Now());
        }
        handle = new Handle(path, false);
        return NTStatus.STATUS_SUCCESS;
    }

    private DateTime Now() => new DateTime(2026, 11, 2, 12, 0, 0, DateTimeKind.Utc).AddSeconds(++_tick);

    public NTStatus CloseFile(object handle)
    {
        var h = (Handle)handle;
        if (h.DeleteOnClose) Files.Remove(h.Path);
        return NTStatus.STATUS_SUCCESS;
    }

    public NTStatus QueryDirectory(out List<QueryDirectoryFileInformation> result, object handle, string fileName, FileInformationClass informationClass)
    {
        var dir = ((Handle)handle).Path;
        result =
        [
            new FileDirectoryInformation { FileName = ".", FileAttributes = FileAttributes.Directory },
            new FileDirectoryInformation { FileName = "..", FileAttributes = FileAttributes.Directory }
        ];
        foreach (var folder in Folders.Where(f => f.Length > 0 && Parent(f).Equals(dir, StringComparison.OrdinalIgnoreCase)))
            result.Add(new FileDirectoryInformation { FileName = folder[(folder.LastIndexOf('\\') + 1)..], FileAttributes = FileAttributes.Directory });
        foreach (var (path, file) in Files.Where(f => Parent(f.Key).Equals(dir, StringComparison.OrdinalIgnoreCase)))
            result.Add(new FileDirectoryInformation
            {
                FileName = path[(path.LastIndexOf('\\') + 1)..], FileAttributes = FileAttributes.Normal,
                EndOfFile = file.Data.Length, LastWriteTime = file.Modified
            });
        // Like SMB2FileStore.QueryDirectory: pages until the server says there's nothing more
        return NTStatus.STATUS_NO_MORE_FILES;
    }

    public NTStatus ReadFile(out byte[] data, object handle, long offset, int maxCount)
    {
        var bytes = Files[((Handle)handle).Path].Data;
        if (offset >= bytes.Length) { data = null!; return NTStatus.STATUS_END_OF_FILE; }
        data = bytes.Skip((int)offset).Take(maxCount).ToArray();
        return NTStatus.STATUS_SUCCESS;
    }

    public NTStatus WriteFile(out int numberOfBytesWritten, object handle, long offset, byte[] data)
    {
        var path = ((Handle)handle).Path;
        var bytes = Files[path].Data.ToList();
        while (bytes.Count < offset + data.Length) bytes.Add(0);
        for (int i = 0; i < data.Length; i++) bytes[(int)offset + i] = data[i];
        Files[path] = (bytes.ToArray(), Now());
        numberOfBytesWritten = data.Length;
        return NTStatus.STATUS_SUCCESS;
    }

    public NTStatus SetFileInformation(object handle, FileInformation information)
    {
        var h = (Handle)handle;
        switch (information)
        {
            case FileRenameInformationType2 rename:
                if (Files.ContainsKey(rename.FileName) && !rename.ReplaceIfExists) return NTStatus.STATUS_OBJECT_NAME_COLLISION;
                Files[rename.FileName] = Files[h.Path];
                Files.Remove(h.Path);
                h.Path = rename.FileName;
                return NTStatus.STATUS_SUCCESS;
            case FileDispositionInformation dispose:
                h.DeleteOnClose = dispose.DeletePending;
                return NTStatus.STATUS_SUCCESS;
            default:
                return NTStatus.STATUS_NOT_SUPPORTED;
        }
    }

    public NTStatus GetFileInformation(out FileInformation result, object handle, FileInformationClass informationClass)
    {
        var file = Files[((Handle)handle).Path];
        result = new FileNetworkOpenInformation { EndOfFile = file.Data.Length, LastWriteTime = file.Modified };
        return NTStatus.STATUS_SUCCESS;
    }

    public string? Text(string path) => Files.TryGetValue(path, out var f) ? Encoding.UTF8.GetString(f.Data) : null;

    public NTStatus Disconnect() => NTStatus.STATUS_SUCCESS;
    public NTStatus FlushFileBuffers(object handle) => NTStatus.STATUS_SUCCESS;
    public NTStatus LockFile(object handle, long byteOffset, long length, bool exclusiveLock) => NTStatus.STATUS_NOT_SUPPORTED;
    public NTStatus UnlockFile(object handle, long byteOffset, long length) => NTStatus.STATUS_NOT_SUPPORTED;
    public NTStatus GetFileSystemInformation(out FileSystemInformation result, FileSystemInformationClass informationClass) { result = null!; return NTStatus.STATUS_NOT_SUPPORTED; }
    public NTStatus SetFileSystemInformation(FileSystemInformation information) => NTStatus.STATUS_NOT_SUPPORTED;
    public NTStatus GetSecurityInformation(out SecurityDescriptor result, object handle, SecurityInformation securityInformation) { result = null!; return NTStatus.STATUS_NOT_SUPPORTED; }
    public NTStatus SetSecurityInformation(object handle, SecurityInformation securityInformation, SecurityDescriptor securityDescriptor) => NTStatus.STATUS_NOT_SUPPORTED;
    public NTStatus NotifyChange(out object ioRequest, object handle, NotifyChangeFilter completionFilter, bool watchTree, int outputBufferSize, OnNotifyChangeCompleted onNotifyChangeCompleted, object context) { ioRequest = null!; return NTStatus.STATUS_NOT_SUPPORTED; }
    public NTStatus Cancel(object ioRequest) => NTStatus.STATUS_NOT_SUPPORTED;
    public NTStatus DeviceIOControl(object handle, uint ctlCode, byte[] input, out byte[] output, int maxOutputLength) { output = null!; return NTStatus.STATUS_NOT_SUPPORTED; }
}

/// <summary>The SMB sync store (used on Android) against a share that behaves like SMBLibrary.</summary>
public sealed class SmbStoreTests : IDisposable
{
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "aw-smb-" + Guid.NewGuid().ToString("N"));
    private readonly FakeSmbShare _share = new();
    private static readonly SmbSettings Settings = new("nas", "writing", "Test", "ann", "pw");

    public SmbStoreTests() => _share.Folders.Add("Test");

    public void Dispose()
    {
        try { Directory.Delete(_tmp, recursive: true); } catch { }
    }

    private SmbSyncStore Store() => new(Settings, _share);

    [Fact]
    public async Task List_ReturnsEntries_ThoughTheListingEndsWithNoMoreFiles()
    {
        // The bug: entries arrive together with STATUS_NO_MORE_FILES and were thrown away
        _share.Folders.UnionWith([@"Test\books", @"Test\books\b1", @"Test\books\b1\scenes"]);
        _share.Files[@"Test\books\b1\book.json"] = ("{}"u8.ToArray(), DateTime.UtcNow);
        _share.Files[@"Test\books\b1\scenes\s1.html"] = ("<p>Fog.</p>"u8.ToArray(), DateTime.UtcNow);

        var paths = (await Store().ListAsync("books")).Select(e => e.Path).Order().ToList();

        Assert.Equal(["books/b1/book.json", "books/b1/scenes/s1.html"], paths);
    }

    [Fact]
    public async Task List_MissingFolder_IsEmpty() =>
        Assert.Empty(await Store().ListAsync("books"));

    [Fact]
    public async Task WriteReadDelete_RoundTripInChunks()
    {
        var store = Store();
        var text = "<p>“Fog,” she said — longer than one chunk.</p>";
        var entry = await store.WriteAsync("books/b1/scenes/s1.html", Encoding.UTF8.GetBytes(text));

        Assert.Equal(Encoding.UTF8.GetByteCount(text), entry.Size);
        Assert.Equal(text, _share.Text(@"Test\books\b1\scenes\s1.html"));
        Assert.DoesNotContain(_share.Files.Keys, k => k.EndsWith(".tmp"));   // written via a temporary file
        Assert.Equal(text, Encoding.UTF8.GetString((await store.ReadAsync("books/b1/scenes/s1.html"))!));

        await store.DeleteAsync("books/b1/scenes/s1.html");
        Assert.Null(await store.ReadAsync("books/b1/scenes/s1.html"));
    }

    [Fact]
    public async Task Sync_BooksFromAnotherDevice_ArriveOverSmb()
    {
        // The PC uploads through the SMB store; the tablet, which has its own book, receives it
        var pc = new MemoryStore("pc");
        pc.Put("books/pcbook/book.json", """{"Id":"pcbook","Chapters":[]}""");
        pc.Put("books/pcbook/scenes/s1.html", "<p>Written on the PC.</p>");
        var tablet = new MemoryStore("tablet");
        tablet.Put("books/tabbook/book.json", """{"Id":"tabbook","Chapters":[]}""");

        await new SyncEngine(pc, Store(), Path.Combine(_tmp, "pc"), "PC").RunAsync();
        var report = await new SyncEngine(tablet, Store(), Path.Combine(_tmp, "tablet"), "Tablet").RunAsync();
        await new SyncEngine(pc, Store(), Path.Combine(_tmp, "pc"), "PC").RunAsync();

        Assert.Contains("pcbook", report.ChangedLocalBooks);
        Assert.Equal("<p>Written on the PC.</p>", tablet.Text("books/pcbook/scenes/s1.html"));
        Assert.NotNull(pc.Text("books/tabbook/book.json"));
    }

    [Fact]
    public async Task Sync_NothingChanged_DoesNotReupload()
    {
        var device = new MemoryStore("d");
        device.Put("books/b/book.json", """{"Id":"b","Chapters":[]}""");
        await new SyncEngine(device, Store(), Path.Combine(_tmp, "d"), "D").RunAsync();
        var second = await new SyncEngine(device, Store(), Path.Combine(_tmp, "d"), "D").RunAsync();
        Assert.False(second.ChangedAnything);
    }
}
