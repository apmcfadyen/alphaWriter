namespace alphaWriter.Services.Sync
{
    // Imported inside the namespace so these names win over MAUI's project-wide usings
    // (e.g. FontSize, Font), which differ by platform and clash on Android.
    using SMBLibrary;
    using SMBLibrary.Client;
    using FileAttributes = SMBLibrary.FileAttributes;

    /// <summary>Where and how to reach an SMB share. The password is kept in secure storage, never in preferences.</summary>
    public sealed record SmbSettings(string Server, string Share, string Folder, string User, string Password)
    {
        /// <summary>"DOMAIN\user" or "user@domain" split into (domain, user); most home NAS boxes need no domain.</summary>
        public (string Domain, string User) Account
        {
            get
            {
                var user = User?.Trim() ?? string.Empty;
                var slash = user.IndexOf('\\');
                return slash > 0 ? (user[..slash], user[(slash + 1)..]) : (string.Empty, user);
            }
        }

        /// <summary>
        /// Reads "\\server\share\folder", "//server/share/folder" or "smb://server/share/folder".
        /// Returns null when there's no server and share.
        /// </summary>
        public static (string Server, string Share, string Folder)? ParseAddress(string? address)
        {
            var text = (address ?? string.Empty).Trim();
            if (text.StartsWith("smb://", StringComparison.OrdinalIgnoreCase)) text = text[6..];
            var parts = text.Replace('/', '\\').Split('\\', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) return null;
            return (parts[0], parts[1], string.Join('\\', parts.Skip(2)));
        }
    }

    /// <summary>
    /// The shared folder reached directly over SMB 2/3 (SMBLibrary), for devices that can't
    /// mount network shares, like Android tablets. One connection per sync; disposed after.
    /// </summary>
    public sealed class SmbSyncStore(SmbSettings settings) : ISyncStore, IDisposable
    {
        public static (string Server, string Share, string Folder)? ParseAddressOrNull(string? address) =>
            SmbSettings.ParseAddress(address);

        private SMB2Client? _client;
        private ISMBFileStore? _store;

        /// <summary>For tests: works against an already-connected share (or a stand-in for one).</summary>
        internal SmbSyncStore(SmbSettings settings, ISMBFileStore connectedShare) : this(settings) => _store = connectedShare;

        public string Location =>
            $"smb://{settings.Server}/{settings.Share}/{settings.Folder.Replace('\\', '/').Trim('/')}".TrimEnd('/');

        /// <summary>Connects and signs in (once), turning SMB errors into messages people can act on.</summary>
        private ISMBFileStore Store()
        {
            if (_store is not null) return _store;
            _client = new SMB2Client();
            bool connected;
            try { connected = _client.Connect(settings.Server, SMBTransportType.DirectTCPTransport); }
            catch (System.Net.Sockets.SocketException) { connected = false; }   // e.g. the name doesn't resolve
            if (!connected)
                throw new IOException($"Couldn't reach {settings.Server}. Check the server name and that this device is on the same network.");

            var (domain, user) = settings.Account;
            var status = _client.Login(domain, user, settings.Password);
            if (status != NTStatus.STATUS_SUCCESS)
                throw new UnauthorizedAccessException($"{settings.Server} didn't accept the user name and password ({status}).");

            _store = _client.TreeConnect(settings.Share, out status);
            if (status != NTStatus.STATUS_SUCCESS || _store is null)
                throw new IOException($"Couldn't open the share “{settings.Share}” on {settings.Server} ({status}).");
            return _store;
        }

        /// <summary>Path inside the share: the configured folder, then the sync path, with backslashes.</summary>
        internal string SharePath(string path)
        {
            var folder = settings.Folder.Replace('/', '\\').Trim('\\');
            var rest = path.Replace('/', '\\').Trim('\\');
            return folder.Length == 0 ? rest : rest.Length == 0 ? folder : folder + "\\" + rest;
        }

        private static bool IsMissing(NTStatus s) =>
            s is NTStatus.STATUS_OBJECT_NAME_NOT_FOUND or NTStatus.STATUS_OBJECT_PATH_NOT_FOUND or NTStatus.STATUS_NO_SUCH_FILE;

        private static void Check(NTStatus status, string what)
        {
            if (status != NTStatus.STATUS_SUCCESS) throw new IOException($"Couldn't {what} ({status}).");
        }

        public Task<IReadOnlyList<SyncEntry>> ListAsync(string prefix, CancellationToken ct = default) => Task.Run(() =>
        {
            var entries = new List<SyncEntry>();
            ListInto(prefix.Trim('/'), entries, ct);
            return (IReadOnlyList<SyncEntry>)entries;
        }, ct);

        private void ListInto(string relDir, List<SyncEntry> entries, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            var store = Store();
            var status = store.CreateFile(out var handle, out _, SharePath(relDir), AccessMask.GENERIC_READ,
                FileAttributes.Directory, ShareAccess.Read | ShareAccess.Write, CreateDisposition.FILE_OPEN,
                CreateOptions.FILE_DIRECTORY_FILE, null);
            if (IsMissing(status)) return;
            Check(status, $"open the folder {relDir}");

            List<QueryDirectoryFileInformation> items;
            try
            {
                // SMBLibrary pages through the folder and ends with "no more files", returning that
                // status together with every entry it collected, so it means success, not "empty"
                status = store.QueryDirectory(out items, handle, "*", FileInformationClass.FileDirectoryInformation);
                if (status != NTStatus.STATUS_NO_MORE_FILES) Check(status, $"list the folder {relDir}");
                items ??= [];
            }
            finally
            {
                store.CloseFile(handle);
            }

            foreach (var item in items.OfType<FileDirectoryInformation>())
            {
                if (item.FileName is "." or "..") continue;
                var rel = relDir.Length == 0 ? item.FileName : $"{relDir}/{item.FileName}";
                if ((item.FileAttributes & FileAttributes.Directory) != 0) ListInto(rel, entries, ct);
                else if (!rel.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
                    entries.Add(new SyncEntry(rel, item.EndOfFile, item.LastWriteTime.ToUniversalTime()));
            }
        }

        public Task<byte[]?> ReadAsync(string path, CancellationToken ct = default) => Task.Run(() =>
        {
            var store = Store();
            var status = store.CreateFile(out var handle, out _, SharePath(path), AccessMask.GENERIC_READ | AccessMask.SYNCHRONIZE,
                FileAttributes.Normal, ShareAccess.Read, CreateDisposition.FILE_OPEN,
                CreateOptions.FILE_NON_DIRECTORY_FILE | CreateOptions.FILE_SYNCHRONOUS_IO_ALERT, null);
            if (IsMissing(status)) return null;
            Check(status, $"open {path}");
            try
            {
                using var data = new MemoryStream();
                while (true)
                {
                    ct.ThrowIfCancellationRequested();
                    status = store.ReadFile(out var chunk, handle, data.Length, (int)store.MaxReadSize);
                    if (status == NTStatus.STATUS_END_OF_FILE || chunk is null || chunk.Length == 0) break;
                    Check(status, $"read {path}");
                    data.Write(chunk);
                }
                return (byte[]?)data.ToArray();
            }
            finally
            {
                store.CloseFile(handle);
            }
        }, ct);

        public Task<SyncEntry> WriteAsync(string path, byte[] data, CancellationToken ct = default) => Task.Run(() =>
        {
            var store = Store();
            EnsureFolders(Path.GetDirectoryName(path.Replace('\\', '/'))?.Replace('\\', '/') ?? string.Empty);

            // Write a temporary file, then rename it over the real one, so readers never see half a file
            var tmp = SharePath(path) + ".tmp";
            var status = store.CreateFile(out var handle, out _, tmp,
                AccessMask.GENERIC_WRITE | AccessMask.DELETE | AccessMask.SYNCHRONIZE, FileAttributes.Normal, ShareAccess.None,
                CreateDisposition.FILE_OVERWRITE_IF, CreateOptions.FILE_NON_DIRECTORY_FILE | CreateOptions.FILE_SYNCHRONOUS_IO_ALERT, null);
            Check(status, $"write {path}");
            try
            {
                int max = (int)store.MaxWriteSize;
                for (int offset = 0; offset < data.Length; offset += max)
                {
                    ct.ThrowIfCancellationRequested();
                    var chunk = data.AsSpan(offset, Math.Min(max, data.Length - offset)).ToArray();
                    Check(store.WriteFile(out _, handle, offset, chunk), $"write {path}");
                }
                Check(store.SetFileInformation(handle, new FileRenameInformationType2 { FileName = SharePath(path), ReplaceIfExists = true }),
                    $"replace {path}");
                Check(store.GetFileInformation(out var info, handle, FileInformationClass.FileNetworkOpenInformation), $"check {path}");
                var open = (FileNetworkOpenInformation)info;
                return new SyncEntry(path, open.EndOfFile, open.LastWriteTime?.ToUniversalTime() ?? default);
            }
            finally
            {
                store.CloseFile(handle);
            }
        }, ct);

        private void EnsureFolders(string relDir)
        {
            var store = Store();
            var built = string.Empty;
            foreach (var part in relDir.Split('/', StringSplitOptions.RemoveEmptyEntries))
            {
                built = built.Length == 0 ? part : $"{built}/{part}";
                var status = store.CreateFile(out var handle, out _, SharePath(built), AccessMask.GENERIC_READ,
                    FileAttributes.Directory, ShareAccess.Read | ShareAccess.Write, CreateDisposition.FILE_OPEN_IF,
                    CreateOptions.FILE_DIRECTORY_FILE, null);
                Check(status, $"create the folder {built}");
                store.CloseFile(handle);
            }
        }

        public Task DeleteAsync(string path, CancellationToken ct = default) => Task.Run(() =>
        {
            var store = Store();
            var status = store.CreateFile(out var handle, out _, SharePath(path), AccessMask.DELETE | AccessMask.SYNCHRONIZE,
                FileAttributes.Normal, ShareAccess.Read | ShareAccess.Write | ShareAccess.Delete, CreateDisposition.FILE_OPEN,
                CreateOptions.FILE_NON_DIRECTORY_FILE, null);
            if (IsMissing(status)) return;
            Check(status, $"delete {path}");
            try
            {
                Check(store.SetFileInformation(handle, new FileDispositionInformation { DeletePending = true }), $"delete {path}");
            }
            finally
            {
                store.CloseFile(handle);
            }
        }, ct);

        public void Dispose()
        {
            try
            {
                _store?.Disconnect();
                _client?.Logoff();
                _client?.Disconnect();
            }
            catch { /* the connection may already be gone */ }
            _store = null;
            _client = null;
        }
    }
}
