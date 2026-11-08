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
        internal void StartSyncing()
        {
            if (!SyncEnabled) { SyncStatusText = "Sync is off"; return; }
            StartSyncTimer();
            _ = RunSyncAsync(manual: false);
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
            var folder = SyncFolder?.Trim() ?? string.Empty;
            if (folder.Length == 0)
            {
                if (manual) SyncStatusText = "Choose a sync folder first.";
                return;
            }

            IsSyncing = true;
            SyncStatusText = "Syncing…";
            try
            {
                if (!Directory.Exists(folder))
                {
                    SyncStatusText = "Offline: the sync folder isn't reachable";
                    return;
                }

                if (FlushEditorAsync is not null) await FlushEditorAsync();
                await TriggerSaveAsync();

                var openScene = SelectedScene;
                var textAtStart = openScene?.Content;
                _syncInProgress = true;

                var engine = new SyncEngine(
                    new FolderSyncStore(AppPaths.DataDirectory), new FolderSyncStore(folder),
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
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
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
