using alphaWriter.Models;
using alphaWriter.Services;
using alphaWriter.Services.Sync;
using CommunityToolkit.Maui.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;

namespace alphaWriter.ViewModels
{
    /// <summary>
    /// Keeping this device's library in step with a shared folder (an SMB share, a mapped
    /// drive, any folder): on start, every few minutes, and on demand. Each device keeps its
    /// own copy, so writing never waits on the network.
    /// </summary>
    public partial class WriterViewModel
    {
        private static readonly TimeSpan SyncInterval = TimeSpan.FromMinutes(5);

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsSyncStatusVisible))]
        private bool syncEnabled = LoadPref("sync.enabled", "false") == "true";

        [ObservableProperty]
        private string syncFolder = LoadPref("sync.folder", string.Empty);

        [ObservableProperty]
        private string syncDeviceName = LoadPref("sync.device", DefaultDeviceName());

        public IReadOnlyList<string> SyncModeOptions { get; } = ["Folder", "SMB server"];

        /// <summary>"Folder" (a path Windows can open) or "SMB server" (connect directly; the only choice on Android).</summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsSyncFolderMode))]
        [NotifyPropertyChangedFor(nameof(IsSyncServerMode))]
        private string syncMode = LoadPref("sync.mode", DefaultSyncMode());

        /// <summary>\\server\share\folder (or smb://server/share/folder).</summary>
        [ObservableProperty]
        private string smbAddress = LoadPref("sync.smb.address", string.Empty);

        [ObservableProperty]
        private string smbUser = LoadPref("sync.smb.user", string.Empty);

        /// <summary>Kept in secure storage (Windows credential protection / Android Keystore), never in preferences or logs.</summary>
        [ObservableProperty]
        private string smbPassword = string.Empty;

        public bool IsSyncFolderMode => SyncMode != "SMB server";
        public bool IsSyncServerMode => SyncMode == "SMB server";

        /// <summary>Android can't open network paths, so it always connects to the server directly.</summary>
        public bool CanChooseSyncMode => DeviceInfo.Current.Platform == DevicePlatform.WinUI;

        private const string PasswordKey = "sync.smb.password";
        private bool _passwordLoaded;
        private DateTime _lastSyncFinished = DateTime.MinValue;

        private static string DefaultSyncMode()
        {
            try { return DeviceInfo.Current.Platform == DevicePlatform.WinUI ? "Folder" : "SMB server"; }
            catch { return "Folder"; }
        }

        [ObservableProperty]
        private string syncStatusText = "Not synced yet";

        [ObservableProperty]
        private string syncReportText = string.Empty;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(SyncNowCommand))]
        private bool isSyncing;

        public bool IsSyncStatusVisible => SyncEnabled;

        /// <summary>Set by the page: writes the editor's latest text into the open scene.</summary>
        public Func<Task>? FlushEditorAsync { get; set; }

        /// <summary>Raised when the Book workspace should scroll to the sync settings.</summary>
        public event Action? SyncPanelRequested;

        private bool _syncInProgress;
        private bool _saveAfterSync;
        private System.Threading.Timer? _syncTimer;

        private static string DefaultDeviceName()
        {
            try { return DeviceInfo.Current.Name; }
            catch { return Environment.MachineName; }
        }

        partial void OnSyncEnabledChanged(bool value)
        {
            StorePref("sync.enabled", value ? "true" : "false");
            if (value) StartSyncTimer(); else StopSyncTimer();
            SyncStatusText = value ? "Not synced yet" : "Sync is off";
        }

        partial void OnSyncFolderChanged(string value) => StorePref("sync.folder", value?.Trim() ?? string.Empty);
        partial void OnSyncModeChanged(string value) => StorePref("sync.mode", value);
        partial void OnSmbAddressChanged(string value) => StorePref("sync.smb.address", value?.Trim() ?? string.Empty);
        partial void OnSmbUserChanged(string value) => StorePref("sync.smb.user", value?.Trim() ?? string.Empty);

        partial void OnSmbPasswordChanged(string value)
        {
            if (!_passwordLoaded) return;   // the value just read back from secure storage
            _ = SavePasswordAsync(value);
        }

        private static async Task SavePasswordAsync(string value)
        {
            try
            {
                if (string.IsNullOrEmpty(value)) SecureStorage.Default.Remove(PasswordKey);
                else await SecureStorage.Default.SetAsync(PasswordKey, value);
            }
            catch { /* secure storage unavailable: the password just isn't remembered */ }
        }

        private async Task LoadPasswordAsync()
        {
            try { SmbPassword = await SecureStorage.Default.GetAsync(PasswordKey) ?? string.Empty; }
            catch { SmbPassword = string.Empty; }
            finally { _passwordLoaded = true; }
        }

        /// <summary>The shared folder as a store, per the chosen mode. Null (with a reason) when it isn't set up.</summary>
        internal (ISyncStore? Store, string Problem) CreateRemoteStore()
        {
            if (IsSyncServerMode)
            {
                if (SmbSyncStore.ParseAddressOrNull(SmbAddress) is not { } address)
                    return (null, "Enter the share as \\\\server\\share\\folder.");
                return (new SmbSyncStore(new SmbSettings(address.Server, address.Share, address.Folder, SmbUser, SmbPassword)), string.Empty);
            }
            var folder = SyncFolder?.Trim() ?? string.Empty;
            if (folder.Length == 0) return (null, "Choose a sync folder first.");
            if (!Directory.Exists(folder)) return (null, "Offline: the sync folder isn't reachable");
            return (new FolderSyncStore(folder), string.Empty);
        }

        [RelayCommand]
        private async Task TestSyncConnection()
        {
            var (store, problem) = CreateRemoteStore();
            if (store is null) { SyncReportText = problem; return; }
            SyncReportText = "Connecting…";
            try
            {
                var books = (await store.ListAsync(SyncEngine.BooksPrefix)).Select(e => e.Path.Split('/')[1]).Distinct().Count();
                SyncReportText = books == 0
                    ? "Connected. The folder has no books yet: the first sync will upload this device's."
                    : $"Connected. The folder holds {books} book{(books == 1 ? "" : "s")}.";
            }
            catch (Exception ex)
            {
                SyncReportText = ex.Message;
            }
            finally
            {
                (store as IDisposable)?.Dispose();
            }
        }

        /// <summary>App back in front (Android resume, Windows restore): catch up if it's been a while.</summary>
        public void OnAppResumed()
        {
            if (SyncEnabled && DateTime.Now - _lastSyncFinished > TimeSpan.FromMinutes(1))
                _ = RunSyncAsync(manual: false);
        }

        /// <summary>App going to the background (Android sleep, Windows minimise): send what's new.</summary>
        public void OnAppStopped()
        {
            if (SyncEnabled) _ = RunSyncAsync(manual: false);
        }
        partial void OnSyncDeviceNameChanged(string value) => StorePref("sync.device", value?.Trim() ?? string.Empty);

        /// <summary>From the status bar: shows the sync settings in the Book workspace.</summary>
        [RelayCommand]
        private void OpenSyncSettings()
        {
            if (SelectedBook is null) return;
            ActiveWorkspace = Workspace.Book;
            SyncPanelRequested?.Invoke();
        }

        [RelayCommand]
        private async Task BrowseSyncFolder()
        {
            try
            {
                var result = await FolderPicker.Default.PickAsync(CancellationToken.None);
                if (result.IsSuccessful && result.Folder is not null) SyncFolder = result.Folder.Path;
            }
            catch (Exception ex)
            {
                SyncStatusText = $"Couldn't open the folder picker: {ex.Message}";
            }
        }

        private bool CanSyncNow() => !IsSyncing;

        [RelayCommand(CanExecute = nameof(CanSyncNow))]
        private Task SyncNow() => RunSyncAsync(manual: true);

        /// <summary>Called once the library has loaded.</summary>
        internal async void StartSyncing()
        {
            await LoadPasswordAsync();
            if (!SyncEnabled) { SyncStatusText = "Sync is off"; return; }
            StartSyncTimer();
            await RunSyncAsync(manual: false);
        }

        private void StartSyncTimer()
        {
            _syncTimer?.Dispose();
            _syncTimer = new System.Threading.Timer(
                _ => MainThread.BeginInvokeOnMainThread(() => _ = RunSyncAsync(manual: false)),
                null, SyncInterval, SyncInterval);
        }

        private void StopSyncTimer()
        {
            _syncTimer?.Dispose();
            _syncTimer = null;
        }

        /// <summary>
        /// Saves, syncs while saves wait, then swaps in any books that changed. Typing that
        /// happens during the sync is carried over to the reloaded scene.
        /// </summary>
        internal async Task RunSyncAsync(bool manual)
        {
            if (IsSyncing) return;
            if (!SyncEnabled)
            {
                if (manual) SyncStatusText = "Tick “Sync this device” first.";
                return;
            }
            var (remote, problem) = CreateRemoteStore();
            if (remote is null)
            {
                if (manual || problem.StartsWith("Offline", StringComparison.Ordinal)) SyncStatusText = problem;
                return;
            }

            IsSyncing = true;
            SyncStatusText = "Syncing…";
            try
            {

                if (FlushEditorAsync is not null) await FlushEditorAsync();
                await TriggerSaveAsync();

                var openScene = SelectedScene;
                var textAtStart = openScene?.Content;
                _syncInProgress = true;

                var engine = new SyncEngine(
                    new FolderSyncStore(AppPaths.DataDirectory), remote,
                    Path.Combine(AppPaths.DataDirectory, "sync"), SyncDeviceName);
                SyncReport? report = null;
                var reloaded = await _bookService.RunExclusiveAsync(async () =>
                {
                    report = await Task.Run(() => engine.RunAsync());
                    return report.ChangedLocalBooks.Count > 0;
                });

                if (report!.Busy)
                {
                    SyncStatusText = "Another device is syncing; will try again shortly";
                    return;
                }
                if (reloaded is not null)
                    ApplySyncedBooks(reloaded, report.ChangedLocalBooks, openScene, textAtStart);

                SyncStatusText = $"Synced {DateTime.Now:HH:mm}";
                SyncReportText = Describe(report);
            }
            catch (SyncFolderException ex)
            {
                SyncStatusText = "Sync paused";
                SyncReportText = ex.Message;
            }
            catch (UnauthorizedAccessException ex) when (IsSyncServerMode)
            {
                SyncStatusText = "Sync paused: sign-in failed";
                SyncReportText = ex.Message;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Net.Sockets.SocketException)
            {
                SyncStatusText = "Offline: the sync folder isn't reachable";
                SyncReportText = ex.Message;
            }
            catch (Exception ex)
            {
                SyncStatusText = "Sync failed";
                SyncReportText = ex.Message;
            }
            finally
            {
                (remote as IDisposable)?.Dispose();
                _lastSyncFinished = DateTime.Now;
                _syncInProgress = false;
                IsSyncing = false;
                if (_saveAfterSync)
                {
                    _saveAfterSync = false;
                    await TriggerSaveAsync();
                }
            }
        }

        internal static string Describe(SyncReport report)
        {
            var parts = new List<string>();
            if (report.Downloaded > 0) parts.Add($"{report.Downloaded} file{(report.Downloaded == 1 ? "" : "s")} received");
            if (report.Uploaded > 0) parts.Add($"{report.Uploaded} sent");
            if (report.Deleted > 0) parts.Add($"{report.Deleted} removed");
            var summary = parts.Count == 0 ? "Everything was already up to date." : string.Join(", ", parts) + ".";
            return report.Notes.Count == 0 ? summary : summary + " " + string.Join(" ", report.Notes);
        }

        /// <summary>
        /// Replaces books whose files changed with their reloaded versions, adds new ones and
        /// removes deleted ones, keeping the open book, chapter, scene and workspace.
        /// </summary>
        internal void ApplySyncedBooks(List<Book> reloaded, ICollection<string> changed, Scene? openScene, string? textAtStart)
        {
            var bookId = SelectedBook?.Id;
            var chapterId = SelectedChapter?.Id;
            var sceneId = SelectedScene?.Id;
            var workspace = ActiveWorkspace;
            // Words typed while the sync ran belong to the old objects: carry them over
            string? typed = openScene is not null && openScene.Content != textAtStart ? openScene.Content : null;

            var byId = reloaded.ToDictionary(b => b.Id);
            var current = Books.ToList();
            for (int i = current.Count - 1; i >= 0; i--)
            {
                var book = current[i];
                if (!byId.TryGetValue(book.Id, out var fresh)) { if (changed.Contains(book.Id)) Books.Remove(book); continue; }
                if (changed.Contains(book.Id)) Books[Books.IndexOf(book)] = fresh;
            }
            foreach (var fresh in reloaded.Where(b => Books.All(x => x.Id != b.Id)))
                Books.Add(fresh);

            var selected = Books.FirstOrDefault(b => b.Id == bookId);
            if (selected is null || !changed.Contains(selected.Id))
            {
                // The open book was deleted elsewhere, or none was open (a new device's first sync)
                if (selected is null) SelectedBook = Books.FirstOrDefault();
                return;
            }

            SelectedBook = selected;
            var chapter = selected.Chapters.FirstOrDefault(c => c.Id == chapterId);
            var scene = chapter?.Scenes.FirstOrDefault(s => s.Id == sceneId);
            if (scene is not null && typed is not null)
            {
                scene.Content = typed;
                _saveAfterSync = true;
            }
            if (chapter is not null) SelectedChapter = chapter;
            if (scene is not null) SelectedScene = scene;
            ActiveWorkspace = workspace;
        }
    }
}
