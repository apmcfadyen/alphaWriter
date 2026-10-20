using alphaWriter.Models;
using alphaWriter.Models.Analysis;
using alphaWriter.Services;
using alphaWriter.Services.Nlp;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using Location = alphaWriter.Models.Location;

namespace alphaWriter.ViewModels
{
    public partial class WriterViewModel : ObservableObject
    {
        private readonly IBookService _bookService;
        private readonly IImageService _imageService;
        private readonly INlpAnalysisService _nlpAnalysisService;
        private readonly INlpModelManager _modelManager;
        private readonly INerService _nerService;
        private readonly IPosTaggingService _posTaggingService;
        private readonly ILocationHeuristicService _locationHeuristic;
        private readonly INlpCacheService _nlpCacheService;
        private System.Threading.Timer? _saveTimer;
        private bool _initialized;
        private CancellationTokenSource? _analysisCts;
        private List<Models.Analysis.SceneAnalysisResult>? _analysisResults;
        private List<NlpNote> _allNotes = [];
        private List<Models.Analysis.CharacterVoiceProfile> _voiceProfiles = [];
        private List<Models.Analysis.DialogueVoiceProfile> _dialogueProfiles = [];

        // Raised when the selected scene changes so the page can update the WebView
        public event Action<Scene?>? SelectedSceneChanged;

        public WriterViewModel(IBookService bookService, IImageService imageService,
            INlpAnalysisService nlpAnalysisService, INlpModelManager modelManager,
            INerService nerService, IPosTaggingService posTaggingService,
            ILocationHeuristicService locationHeuristic, INlpCacheService nlpCacheService)
        {
            _bookService = bookService;
            _imageService = imageService;
            _nlpAnalysisService = nlpAnalysisService;
            _modelManager = modelManager;
            _nerService = nerService;
            _posTaggingService = posTaggingService;
            _locationHeuristic = locationHeuristic;
            _nlpCacheService = nlpCacheService;
        }

        [ObservableProperty]
        private ObservableCollection<Book> books = [];

        [ObservableProperty]
        private Book? selectedBook;

        [ObservableProperty]
        private Chapter? selectedChapter;

        [ObservableProperty]
        private Scene? selectedScene;

        [ObservableProperty]
        private int wordCount;

        [ObservableProperty]
        private bool isSidebarVisible = true;

        [ObservableProperty]
        private bool isMetadataPanelVisible;


        [ObservableProperty]
        private SidebarMode sidebarMode = SidebarMode.Chapters;

        [ObservableProperty]
        private Character? selectedCharacter;

        [ObservableProperty]
        private Location? selectedLocation;

        [ObservableProperty]
        private Item? selectedItem;

        [ObservableProperty]
        private ObservableCollection<ViewpointOption> viewpointOptions = [];

        [ObservableProperty]
        private ObservableCollection<SceneEntityOption> sceneCharacterOptions = [];

        [ObservableProperty]
        private ObservableCollection<SceneEntityOption> sceneLocationOptions = [];

        [ObservableProperty]
        private ObservableCollection<SceneEntityOption> sceneItemOptions = [];



        [ObservableProperty]
        private string activeStatsTab = "Frequency";

        [ObservableProperty]
        private ObservableCollection<WordFrequencyEntry> wordFrequencyData = [];

        [ObservableProperty]
        private ObservableCollection<WordFrequencyEntry> problemWordsData = [];

        [ObservableProperty]
        private ObservableCollection<CharacterWordCount> characterWordCountData = [];

        [ObservableProperty]
        private ObservableCollection<DailyProgressEntry> dailyProgressData = [];

        [ObservableProperty]
        private ObservableCollection<NlpNote> nlpNotes = [];

        [ObservableProperty]
        private string activeReportType = "Entities";

        [ObservableProperty]
        private bool isAnalyzing;

        [ObservableProperty]
        private bool isDownloadingModels;

        [ObservableProperty]
        private string analysisProgress = "";

        [ObservableProperty]
        private string analysisStatusText = "";

        [ObservableProperty]
        private string analysisStatusColor = "#9E9AA0";

        [ObservableProperty]
        private string selectedCategoryFilter = "All";

        [ObservableProperty]
        private string selectedSeverityFilter = "All";

        public List<string> CategoryFilterOptions { get; } =
            ["All", "Copy Editor", "Line Editor", "Developmental Editor"];

        public List<string> SeverityFilterOptions { get; } =
            ["All", "Issue", "Warning", "Info"];

        public bool AreModelsAvailable => _modelManager.AreModelsAvailable;

        [ObservableProperty]
        private bool isEntityPreviewVisible;

        [ObservableProperty]
        private EntityPreview? entityPreview;

        [ObservableProperty]
        private ObservableCollection<NerCandidateViewModel> characterCandidates = [];

        [ObservableProperty]
        private ObservableCollection<NerCandidateViewModel> locationCandidates = [];

        [ObservableProperty]
        private ObservableCollection<NerCandidateViewModel> itemCandidates = [];

        [ObservableProperty]
        private bool hasCharacterCandidates;

        [ObservableProperty]
        private bool hasLocationCandidates;

        [ObservableProperty]
        private bool hasItemCandidates;

        [ObservableProperty]
        private bool isScanning;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasScanProgress))]
        private string scanProgress = string.Empty;

        public bool HasScanProgress => !string.IsNullOrEmpty(ScanProgress);

        [ObservableProperty]
        private ObservableCollection<BreadcrumbItem> breadcrumbItems = [];

        [ObservableProperty]
        private string breadcrumbPath = string.Empty;

        // Raised whenever entity names or aliases change so the WebView can re-highlight.
        public event Action? EntitiesChanged;
        public event Action? ReportsPanelShown;

        public List<SceneStatus> AvailableStatuses { get; } =
            [SceneStatus.Outline, SceneStatus.Draft, SceneStatus.FirstEdit, SceneStatus.SecondEdit, SceneStatus.Done];

        // Convenience flags for IsEnabled bindings in XAML
        public bool HasSelectedBook => SelectedBook is not null;
        public bool HasSelectedChapter => SelectedChapter is not null;
        public bool HasSelectedScene => SelectedScene is not null;

        // Sidebar mode checks for XAML IsVisible bindings
        public bool IsChaptersMode => SidebarMode == SidebarMode.Chapters;
        public bool IsCharactersMode => SidebarMode == SidebarMode.Characters;
        public bool IsLocationsMode => SidebarMode == SidebarMode.Locations;
        public bool IsItemsMode => SidebarMode == SidebarMode.Items;

        // Main-area visibility (IsSceneEditorVisible, IsElementEditorVisible, …) is derived
        // from ActiveWorkspace — see WriterViewModel.Workspaces.cs.

        // Display name for the book selector button
        public string CurrentBookTitle => SelectedBook?.Title ?? "No book open";

        // ── Editor empty state ────────────────────────────────────────────────
        // When no scene is open, the editor area explains what to do next instead
        // of showing a blank page. One clear next step per situation.

        public bool IsEditorEmpty => SelectedScene is null;

        public string EditorEmptyTitle => SelectedBook switch
        {
            null => "No book open",
            _ when IsReviseWorkspace => "Pick a note",
            { Chapters.Count: 0 } => "Start your outline",
            _ when SelectedChapter is null => "Choose a scene",
            _ when SelectedChapter.Scenes.Count == 0 => $"“{SelectedChapter.Title}” has no scenes yet",
            _ => "Choose a scene"
        };

        public string EditorEmptyMessage => SelectedBook switch
        {
            null => "Create a book to start writing, or switch to one you already have.",
            _ when IsReviseWorkspace => HasAnalysisResults || NlpNotes.Count > 0
                ? "Choose a note in the list to open its scene with the flagged words underlined."
                : "Run an analysis to get editorial notes for the whole book, or open a scene in Write — its live notes appear as you edit.",
            { Chapters.Count: 0 } => "Books are made of chapters, and chapters of scenes. Add your first chapter to begin.",
            _ when SelectedChapter is null => "Pick a scene in the outline, or add one with + next to a chapter.",
            _ when SelectedChapter.Scenes.Count == 0 => "Scenes are where the writing happens. Add one to open the editor.",
            _ => "Pick a scene in the outline, or add a new one."
        };

        public bool ShowCreateBookAction => SelectedBook is null;
        public bool ShowAddChapterAction => SelectedBook is { Chapters.Count: 0 } && !IsReviseWorkspace;
        public bool ShowAddSceneAction => SelectedChapter is not null && !IsReviseWorkspace;

        private void RefreshEditorEmptyState()
        {
            OnPropertyChanged(nameof(IsEditorEmpty));
            OnPropertyChanged(nameof(EditorEmptyTitle));
            OnPropertyChanged(nameof(EditorEmptyMessage));
            OnPropertyChanged(nameof(ShowCreateBookAction));
            OnPropertyChanged(nameof(ShowAddChapterAction));
            OnPropertyChanged(nameof(ShowAddSceneAction));
        }

        // ── Lifecycle ──────────────────────────────────────────────────────────

        public async Task InitializeAsync()
        {
            if (_initialized) return;
            _initialized = true;

            var loaded = await _bookService.LoadBooksAsync();
            Books = new ObservableCollection<Book>(loaded);

            // Restore last-opened book across sessions
            var lastId = AppPreferences.Get("lastBookId", string.Empty);
            if (!string.IsNullOrEmpty(lastId))
            {
                var lastBook = Books.FirstOrDefault(b => b.Id == lastId);
                if (lastBook is not null)
                {
                    SelectedBook = lastBook;   // also reopens its last scene
                    RestoreWorkspace();
                }
            }
        }

        // ── Selection change handlers ──────────────────────────────────────────

        partial void OnSelectedBookChanged(Book? value)
        {
            SelectedChapter = null;
            SelectedScene = null;
            SelectedCharacter = null;
            SelectedLocation = null;
            SelectedItem = null;
            ActiveWorkspace = Workspace.Write;
            InsightsShowsReport = false;
            AttachOutline(value);
            OnPropertyChanged(nameof(HasSelectedBook));
            OnPropertyChanged(nameof(CurrentBookTitle));

            // Clear stale analysis data from the previous book
            _allNotes = [];
            _analysisResults = null;
            _voiceProfiles = [];
            _dialogueProfiles = [];
            NlpNotes = [];
            AnalysisProgress = "";
            AnalysisStatusText = "";
            OnPropertyChanged(nameof(HasAnalysisResults));
            OnPropertyChanged(nameof(IsFilterRowVisible));

            // Auto-load persisted analysis for the newly selected book
            IsRuleSettingsVisible = false;
            RefreshEditorialSettings();
            AnnotationsChanged?.Invoke();

            if (value is not null)
                _ = TryLoadPersistedAnalysisAsync(value);

            // Persist last-opened book across sessions
            AppPreferences.Set("lastBookId", value?.Id ?? string.Empty);
            UpdateBreadcrumb();

            // Pick up where the author left off in this book
            if (value is not null) RestoreLastScene(value);
        }

        partial void OnActiveStatsTabChanged(string value)
        {
            OnPropertyChanged(nameof(IsFrequencyTab));
            OnPropertyChanged(nameof(IsProblemsTab));
            OnPropertyChanged(nameof(IsCharactersTab));
            OnPropertyChanged(nameof(IsDailyProgressTab));
            OnPropertyChanged(nameof(IsAnalysisTab));
            OnPropertyChanged(nameof(ActiveInsight));
        }

        partial void OnSelectedCategoryFilterChanged(string value) => ApplyNoteFilters();
        partial void OnSelectedSeverityFilterChanged(string value) => ApplyNoteFilters();

        public bool IsFrequencyTab     => ActiveStatsTab == "Frequency";
        public bool IsProblemsTab      => ActiveStatsTab == "Problems";
        public bool IsCharactersTab    => ActiveStatsTab == "Characters";
        public bool IsDailyProgressTab => ActiveStatsTab == "DailyProgress";
        public bool IsAnalysisTab      => ActiveStatsTab == "Analysis";

        partial void OnSelectedChapterChanged(Chapter? value)
        {
            SelectedScene = null;
            OnPropertyChanged(nameof(HasSelectedChapter));
            UpdateBreadcrumb();
            SyncOutlineSelection();
        }

        partial void OnSelectedSceneChanged(Scene? value)
        {
            // Opening a scene shows the editor. Story Bible selections are left alone —
            // each workspace keeps its own place.
            if (value is not null && !IsEditorHostVisible)
                ActiveWorkspace = Workspace.Write;
            WordCount = value?.WordCount ?? 0;
            RememberScene(value);

            // Fire event FIRST so the WebView flush runs while the Grid is still visible.
            // The OnPropertyChanged calls below change IsSceneEditorVisible, which collapses
            // the WebView's parent Grid — EvaluateJavaScriptAsync may fail after that.
            SelectedSceneChanged?.Invoke(value);

            OnPropertyChanged(nameof(HasSelectedScene));
            OnPropertyChanged(nameof(IsInspectorVisible));
            SyncOutlineSelection();
            if (ReviseScopeIsScene) RebuildReviseGroups();
            RebuildViewpointOptions();
            RebuildSceneEntityOptions();
            UpdateBreadcrumb();
        }

        partial void OnSidebarModeChanged(SidebarMode value)
        {
            OnPropertyChanged(nameof(IsChaptersMode));
            OnPropertyChanged(nameof(IsCharactersMode));
            OnPropertyChanged(nameof(IsLocationsMode));
            OnPropertyChanged(nameof(IsItemsMode));
        }

        partial void OnSelectedCharacterChanged(Character? value)
        {
            if (value is not null)
            {
                SelectedLocation = null;
                SelectedItem = null;
                ActiveWorkspace = Workspace.StoryBible;
            }
            NotifyElementSelectionDerived();
        }

        partial void OnSelectedLocationChanged(Location? value)
        {
            if (value is not null)
            {
                SelectedCharacter = null;
                SelectedItem = null;
                ActiveWorkspace = Workspace.StoryBible;
            }
            NotifyElementSelectionDerived();
        }

        partial void OnSelectedItemChanged(Item? value)
        {
            if (value is not null)
            {
                SelectedCharacter = null;
                SelectedLocation = null;
                ActiveWorkspace = Workspace.StoryBible;
            }
            NotifyElementSelectionDerived();
        }

        // ── Book commands ──────────────────────────────────────────────────────

        [RelayCommand]
        private async Task AddBook()
        {
            var book = new Book { Title = "New Book" };
            Books.Add(book);
            SelectedBook = book;
            await TriggerSaveAsync();
        }

        [RelayCommand]
        private async Task DeleteBook(Book book)
        {
            if (SelectedBook == book)
                SelectedBook = null;
            Books.Remove(book);

            // Clean up stored images for this book
            var imageDir = Path.Combine(AppPaths.DataDirectory, "images", book.Id);
            if (Directory.Exists(imageDir))
            {
                try { Directory.Delete(imageDir, recursive: true); }
                catch { /* best-effort cleanup */ }
            }

            await TriggerSaveAsync();
        }

        [RelayCommand]
        private async Task RenameBook(Book book)
        {
            var name = await Shell.Current.DisplayPromptAsync(
                "Rename Book", "Enter a new title:", initialValue: book.Title);
            if (name is not null)
            {
                book.Title = name;
                // Refresh the collection so the UI picks up the change
                var idx = Books.IndexOf(book);
                if (idx >= 0) { Books.RemoveAt(idx); Books.Insert(idx, book); }
                OnPropertyChanged(nameof(CurrentBookTitle));
                await TriggerSaveAsync();
            }
        }

        [RelayCommand]
        private async Task SwitchBook()
        {
            if (Books.Count == 0) return;
            var titles = Books.Select(b => b.Title).ToArray();
            var result = await Shell.Current.DisplayActionSheetAsync(
                "Switch Book", "Cancel", null, titles);
            if (result is not null && result != "Cancel")
            {
                var book = Books.FirstOrDefault(b => b.Title == result);
                if (book is not null)
                    SelectedBook = book;
            }
        }

        [RelayCommand]
        private async Task ManageBooks()
        {
            var actions = new List<string> { "Add New Book" };
            if (SelectedBook is not null)
            {
                actions.Add("Rename Current Book");
                actions.Add("Delete Current Book");
            }
            var result = await Shell.Current.DisplayActionSheetAsync(
                "Manage Books", "Cancel", null, actions.ToArray());
            switch (result)
            {
                case "Add New Book":
                    await AddBook();
                    break;
                case "Rename Current Book" when SelectedBook is not null:
                    await RenameBook(SelectedBook);
                    break;
                case "Delete Current Book" when SelectedBook is not null:
                    await DeleteBook(SelectedBook);
                    break;
            }
        }

        // ── Chapter commands ───────────────────────────────────────────────────

        [RelayCommand]
        private async Task AddChapter()
        {
            if (SelectedBook is null) return;
            var chapter = new Chapter { Title = "New Chapter" };
            SelectedBook.Chapters.Add(chapter);
            SelectedChapter = chapter;
            await TriggerSaveAsync();
        }

        [RelayCommand]
        private async Task DeleteChapter(Chapter chapter)
        {
            if (SelectedBook is null) return;
            if (SelectedChapter == chapter)
                SelectedChapter = null;
            SelectedBook.Chapters.Remove(chapter);
            RefreshEditorEmptyState();
            await TriggerSaveAsync();
        }

        [RelayCommand]
        private async Task RenameChapter(Chapter chapter)
        {
            var name = await Shell.Current.DisplayPromptAsync(
                "Rename Chapter", "Enter a new title:", initialValue: chapter.Title);
            if (name is not null)
            {
                chapter.Title = name;
                if (SelectedBook is not null)
                {
                    var idx = SelectedBook.Chapters.IndexOf(chapter);
                    if (idx >= 0) { SelectedBook.Chapters.RemoveAt(idx); SelectedBook.Chapters.Insert(idx, chapter); }
                }
                await TriggerSaveAsync();
            }
        }

        // ── Scene commands ─────────────────────────────────────────────────────

        [RelayCommand]
        private async Task AddScene()
        {
            if (SelectedChapter is null) return;
            var scene = new Scene { Title = "New Scene" };
            SelectedChapter.Scenes.Add(scene);
            SelectedScene = scene;
            await TriggerSaveAsync();
        }

        [RelayCommand]
        private async Task DeleteScene(Scene scene)
        {
            var chapter = FindChapterOf(scene);
            if (chapter is null) return;
            if (SelectedScene == scene)
                SelectedScene = null;
            chapter.Scenes.Remove(scene);
            RefreshEditorEmptyState();
            await TriggerSaveAsync();
        }

        [RelayCommand]
        private async Task RenameScene(Scene scene)
        {
            var name = await Shell.Current.DisplayPromptAsync(
                "Rename Scene", "Enter a new title:", initialValue: scene.Title);
            if (name is not null)
            {
                scene.Title = name;
                var owner = FindChapterOf(scene);
                if (owner is not null)
                {
                    var idx = owner.Scenes.IndexOf(scene);
                    if (idx >= 0) { owner.Scenes.RemoveAt(idx); owner.Scenes.Insert(idx, scene); }
                }
                // Also update selected scene title entry if this is the current scene
                if (SelectedScene == scene)
                    OnPropertyChanged(nameof(SelectedScene));
                await TriggerSaveAsync();
            }
        }

        public void SaveSceneTitle() => TriggerSaveAsync().ConfigureAwait(false);

        // ── Breadcrumb navigation ──────────────────────────────────────────────

        [RelayCommand]
        private void NavigateToBreadcrumb(string level)
        {
            // level can be "book", "chapter", or "scene"
            switch (level)
            {
                case "chapter":
                    // Deselect scene when navigating to chapter level
                    SelectedScene = null;
                    break;
                case "book":
                    // Deselect chapter and scene when navigating to book level
                    SelectedChapter = null;
                    SelectedScene = null;
                    break;
            }
        }

        private void UpdateBreadcrumb()
        {
            var items = new ObservableCollection<BreadcrumbItem>();

            // Book level
            if (SelectedBook is not null)
            {
                items.Add(new BreadcrumbItem
                {
                    Label = SelectedBook.Title,
                    Level = "book",
                    Command = NavigateToBreadcrumbCommand
                });

                // Chapter level
                if (SelectedChapter is not null)
                {
                    items.Add(new BreadcrumbItem
                    {
                        Label = SelectedChapter.Title,
                        Level = "chapter",
                        Command = NavigateToBreadcrumbCommand
                    });

                    // Scene level
                    if (SelectedScene is not null)
                    {
                        items.Add(new BreadcrumbItem
                        {
                            Label = SelectedScene.Title,
                            Level = "scene",
                            Command = null // Scene is current, so not clickable
                        });
                    }
                }
            }

            // The last crumb is where the author is: shown as text, not a link
            if (items.Count > 0)
                items[^1].Command = null;

            BreadcrumbItems = items;
            RefreshEditorEmptyState();

            // Also update the simple string format
            BreadcrumbPath = string.Join(" / ", items.Select(x => x.Label));
        }

        // ── Content / editor ──────────────────────────────────────────────────

        public void SaveContent(string htmlContent, int? wordCount = null)
        {
            if (SelectedScene is null) return;
            SelectedScene.Content = htmlContent ?? string.Empty;
            WordCount = wordCount ?? SelectedScene.WordCount;
            RefreshSceneEntityOptionStates(); // auto-detect entities in updated content
            DebouncedSave();
        }

        /// <summary>Updates the toolbar word count display if the given scene is still selected.</summary>
        public void UpdateWordCountDisplay(Scene scene)
        {
            if (SelectedScene == scene)
                WordCount = scene.WordCount;
        }

        /// <summary>Debounced save — resets the timer on every call.</summary>
        public void DebouncedSave()
        {
            _saveTimer?.Dispose();
            _saveTimer = new System.Threading.Timer(
                async _ => await TriggerSaveAsync(),
                null,
                TimeSpan.FromMilliseconds(1500),
                Timeout.InfiniteTimeSpan);
        }

        [RelayCommand]
        private void ToggleSidebar()
        {
            IsSidebarVisible = !IsSidebarVisible;
        }

        [RelayCommand]
        private void ToggleMetadataPanel()
        {
            IsMetadataPanelVisible = !IsMetadataPanelVisible;
        }

        // ── Scene metadata ──────────────────────────────────────────────────

        [RelayCommand]
        private async Task SetSceneStatus(string statusStr)
        {
            if (SelectedScene is null) return;
            if (Enum.TryParse<SceneStatus>(statusStr, out var status))
            {
                SelectedScene.Status = status;
                await TriggerSaveAsync();
            }
        }

        public void SaveSceneMetadata() => TriggerSaveAsync().ConfigureAwait(false);

        // ── Viewpoint character management ──────────────────────────────────

        private void RebuildViewpointOptions()
        {
            ViewpointOptions.Clear();
            if (SelectedBook is null || SelectedScene is null) return;
            foreach (var c in SelectedBook.Characters)
            {
                var opt = new ViewpointOption(c, SelectedScene.ViewpointCharacterIds.Contains(c.Id));
                opt.PropertyChanged += OnViewpointOptionChanged;
                ViewpointOptions.Add(opt);
            }
        }

        private void OnViewpointOptionChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(ViewpointOption.IsSelected) || SelectedScene is null) return;
            SyncViewpointsToScene();
            TriggerSaveAsync().ConfigureAwait(false);
        }

        private void SyncViewpointsToScene()
        {
            if (SelectedScene is null) return;
            SelectedScene.ViewpointCharacterIds = ViewpointOptions
                .Where(o => o.IsSelected)
                .Select(o => o.Character.Id)
                .ToList();
        }

        // ── Scene entity participation ──────────────────────────────────────
        // Characters, locations, and items present in a scene — whether
        // auto-detected from the text or manually checked by the writer.

        private bool _suppressEntitySync;

        /// <summary>
        /// Full rebuild: called on scene switch.  Runs auto-detection first so
        /// that newly loaded content is already reflected in the option states.
        /// </summary>
        private void RebuildSceneEntityOptions()
        {
            // Unsubscribe old options
            foreach (var o in SceneCharacterOptions) o.PropertyChanged -= OnSceneEntityOptionChanged;
            foreach (var o in SceneLocationOptions)  o.PropertyChanged -= OnSceneEntityOptionChanged;
            foreach (var o in SceneItemOptions)      o.PropertyChanged -= OnSceneEntityOptionChanged;

            SceneCharacterOptions.Clear();
            SceneLocationOptions.Clear();
            SceneItemOptions.Clear();

            if (SelectedBook is null || SelectedScene is null) return;

            // Auto-detect before building options so newly detected IDs are already
            // in the scene's lists when we check IsSelected below.
            AutoDetectSceneEntities();

            var content = SelectedScene.Content;

            foreach (var c in SelectedBook.Characters)
            {
                bool sel  = SelectedScene.CharacterIds.Contains(c.Id);
                bool auto = IsEntityDetectedInContent(content, c.Name, c.Aka);
                var opt = new SceneEntityOption(c.Id, c.Name, c.Aka, sel, auto);
                opt.PropertyChanged += OnSceneEntityOptionChanged;
                SceneCharacterOptions.Add(opt);
            }

            foreach (var l in SelectedBook.Locations)
            {
                bool sel  = SelectedScene.LocationIds.Contains(l.Id);
                bool auto = IsEntityDetectedInContent(content, l.Name, l.Aka);
                var opt = new SceneEntityOption(l.Id, l.Name, l.Aka, sel, auto);
                opt.PropertyChanged += OnSceneEntityOptionChanged;
                SceneLocationOptions.Add(opt);
            }

            foreach (var i in SelectedBook.Items)
            {
                bool sel  = SelectedScene.ItemIds.Contains(i.Id);
                bool auto = IsEntityDetectedInContent(content, i.Name, i.Aka);
                var opt = new SceneEntityOption(i.Id, i.Name, i.Aka, sel, auto);
                opt.PropertyChanged += OnSceneEntityOptionChanged;
                SceneItemOptions.Add(opt);
            }
        }

        /// <summary>
        /// Lightweight refresh: called whenever the editor content changes.
        /// Re-runs auto-detection and updates IsSelected / IsAutoDetected on
        /// existing options without rebuilding the entire list.
        /// </summary>
        private void RefreshSceneEntityOptionStates()
        {
            if (SelectedScene is null || SelectedBook is null) return;

            AutoDetectSceneEntities(); // may add new IDs to the scene's lists

            var content = SelectedScene.Content;

            _suppressEntitySync = true;
            try
            {
                foreach (var opt in SceneCharacterOptions)
                {
                    opt.IsSelected     = SelectedScene.CharacterIds.Contains(opt.Id);
                    opt.IsAutoDetected = IsEntityDetectedInContent(content, opt.Name, opt.Aka);
                }
                foreach (var opt in SceneLocationOptions)
                {
                    opt.IsSelected     = SelectedScene.LocationIds.Contains(opt.Id);
                    opt.IsAutoDetected = IsEntityDetectedInContent(content, opt.Name, opt.Aka);
                }
                foreach (var opt in SceneItemOptions)
                {
                    opt.IsSelected     = SelectedScene.ItemIds.Contains(opt.Id);
                    opt.IsAutoDetected = IsEntityDetectedInContent(content, opt.Name, opt.Aka);
                }
            }
            finally { _suppressEntitySync = false; }
        }

        /// <summary>
        /// Scans the current scene's HTML content and adds any entity whose
        /// name or alias is found as a whole word.  Only ever ADDS — manual
        /// links are never removed by content changes.
        /// </summary>
        private void AutoDetectSceneEntities()
        {
            if (SelectedScene is null || SelectedBook is null) return;
            var content = SelectedScene.Content;

            foreach (var c in SelectedBook.Characters)
                if (IsEntityDetectedInContent(content, c.Name, c.Aka)
                    && !SelectedScene.CharacterIds.Contains(c.Id))
                    SelectedScene.CharacterIds.Add(c.Id);

            foreach (var l in SelectedBook.Locations)
                if (IsEntityDetectedInContent(content, l.Name, l.Aka)
                    && !SelectedScene.LocationIds.Contains(l.Id))
                    SelectedScene.LocationIds.Add(l.Id);

            foreach (var i in SelectedBook.Items)
                if (IsEntityDetectedInContent(content, i.Name, i.Aka)
                    && !SelectedScene.ItemIds.Contains(i.Id))
                    SelectedScene.ItemIds.Add(i.Id);
        }

        /// <summary>
        /// Returns true when <paramref name="name"/> or any alias in
        /// <paramref name="aka"/> appears as a whole word in the scene HTML
        /// (case-insensitive, HTML tags stripped before matching).
        /// </summary>
        private static bool IsEntityDetectedInContent(string content, string name, string? aka)
        {
            if (string.IsNullOrWhiteSpace(content) || string.IsNullOrWhiteSpace(name))
                return false;

            // Strip HTML tags so we only match visible text, not attributes.
            var plain = System.Text.RegularExpressions.Regex.Replace(content, "<[^>]*>", " ");

            var terms = new[] { name }.Concat(
                string.IsNullOrEmpty(aka)
                    ? []
                    : aka.Split(',', StringSplitOptions.RemoveEmptyEntries
                                   | StringSplitOptions.TrimEntries));

            return terms
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Any(t => System.Text.RegularExpressions.Regex.IsMatch(
                    plain,
                    $@"\b{System.Text.RegularExpressions.Regex.Escape(t)}\b",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase));
        }

        private void OnSceneEntityOptionChanged(object? sender,
            System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (_suppressEntitySync
                || e.PropertyName != nameof(SceneEntityOption.IsSelected)) return;
            SyncSceneEntitiesToScene();
            TriggerSaveAsync().ConfigureAwait(false);
        }

        private void SyncSceneEntitiesToScene()
        {
            if (SelectedScene is null) return;
            SelectedScene.CharacterIds = SceneCharacterOptions
                .Where(o => o.IsSelected).Select(o => o.Id).ToList();
            SelectedScene.LocationIds = SceneLocationOptions
                .Where(o => o.IsSelected).Select(o => o.Id).ToList();
            SelectedScene.ItemIds = SceneItemOptions
                .Where(o => o.IsSelected).Select(o => o.Id).ToList();
        }

        // ── Element editor dismiss ──────────────────────────────────────────

        [RelayCommand]
        private void CloseElementEditor()
        {
            SelectedCharacter = null;
            SelectedLocation = null;
            SelectedItem = null;
        }

        // ── Book info editor ────────────────────────────────────────────────

        [RelayCommand]
        private void ShowBookInfo()
        {
            if (SelectedBook is null) return;
            ActiveWorkspace = Workspace.Book;
        }

        [RelayCommand]
        private void CloseBookInfo()
        {
            ActiveWorkspace = Workspace.Write;
        }

        public void SaveBookInfo() => TriggerSaveAsync().ConfigureAwait(false);

        // ── Writing statistics ──────────────────────────────────────────────

        [RelayCommand]
        private void ShowStatistics()
        {
            if (SelectedBook is null) return;
            InsightsShowsReport = false;
            ActiveWorkspace = Workspace.Insights;
        }

        [RelayCommand]
        private void CloseStatistics()
        {
            ActiveWorkspace = Workspace.Write;
        }

        [RelayCommand]
        private void SetStatsTab(string tab)
        {
            ActiveStatsTab = tab;
        }

        [RelayCommand]
        private async Task RunAnalysis()
        {
            if (SelectedBook is null || IsAnalyzing) return;

            _analysisCts?.Cancel();
            _analysisCts = new CancellationTokenSource();
            var ct = _analysisCts.Token;

            IsAnalyzing = true;
            AnalysisProgress = "Starting analysis...";

            try
            {
                var progress = new Progress<string>(msg => AnalysisProgress = msg);
                // Unchanged scenes from the previous run are reused rather than re-analyzed
                var (notes, results, profiles, dialogueProfiles) = await _nlpAnalysisService.AnalyzeBookAsync(
                    SelectedBook, progress, ct, _analysisResults);
                _analysisResults = results;
                _allNotes = notes;
                _voiceProfiles = profiles;
                _dialogueProfiles = dialogueProfiles;
                SelectedCategoryFilter = "All";
                SelectedSeverityFilter = "All";
                ApplyNoteFilters();
                AnalysisProgress = $"{AnalysisProgress} Found {NlpNotes.Count} note{(NlpNotes.Count == 1 ? "" : "s")}.";
                AnnotationsChanged?.Invoke();
                OnPropertyChanged(nameof(HasAnalysisResults));
                OnPropertyChanged(nameof(IsFilterRowVisible));

                // Populate inline scene analysis badges
                PopulateSceneNoteCounts(notes);

                // Persist analysis results for next session
                await PersistAnalysisResultsAsync(results, notes);
            }
            catch (OperationCanceledException)
            {
                AnalysisProgress = "Analysis cancelled.";
            }
            finally
            {
                IsAnalyzing = false;
            }
        }

        [RelayCommand]
        private void CancelAnalysis()
        {
            _analysisCts?.Cancel();
        }

        [RelayCommand]
        private void ClearAnalysis()
        {
            _allNotes = [];
            _analysisResults = null;
            _voiceProfiles = [];
            _dialogueProfiles = [];
            NlpNotes = [];
            AnalysisProgress = "";
            AnalysisStatusText = "";
            SelectedCategoryFilter = "All";
            SelectedSeverityFilter = "All";
            OnPropertyChanged(nameof(HasAnalysisResults));
            OnPropertyChanged(nameof(IsFilterRowVisible));
            ClearSceneNoteCounts();
            AnnotationsChanged?.Invoke();
        }

        private async Task PersistAnalysisResultsAsync(
            List<SceneAnalysisResult> results, List<NlpNote> notes)
        {
            if (SelectedBook is null) return;

            var hashes = new Dictionary<string, string>();
            foreach (var scene in SelectedBook.Chapters.SelectMany(c => c.Scenes))
                hashes[scene.Id] = NlpCacheService.ComputeHash(scene.Content ?? string.Empty);

            var data = new PersistedAnalysisData
            {
                AnalyzedAtUtc = DateTime.UtcNow,
                SceneContentHashes = hashes,
                Results = results,
                Notes = notes
            };

            await _nlpCacheService.SaveAnalysisResultsAsync(SelectedBook.Id, data);

            var timestamp = data.AnalyzedAtUtc.ToLocalTime().ToString("MMM d, yyyy 'at' h:mm tt");
            AnalysisStatusText = $"Analysis from {timestamp}";
            AnalysisStatusColor = "#9E9AA0";
        }

        private async Task TryLoadPersistedAnalysisAsync(Book book)
        {
            try
            {
                var data = await Task.Run(() => _nlpCacheService.LoadAnalysisResults(book.Id));
                if (data is null) return;

                // User may have switched books while we were loading
                if (SelectedBook?.Id != book.Id) return;

                _analysisResults = data.Results;
                _allNotes = data.Notes;
                _voiceProfiles = [];
                _dialogueProfiles = [];
                SelectedCategoryFilter = "All";
                SelectedSeverityFilter = "All";
                ApplyNoteFilters();
                PopulateSceneNoteCounts(data.Notes);
                AnalysisProgress = $"Found {NlpNotes.Count} note{(NlpNotes.Count == 1 ? "" : "s")}.";
                OnPropertyChanged(nameof(HasAnalysisResults));
                OnPropertyChanged(nameof(IsFilterRowVisible));
                AnnotationsChanged?.Invoke();

                // Check staleness
                var stale = false;
                var currentScenes = book.Chapters.SelectMany(c => c.Scenes).ToList();
                var currentIds = new HashSet<string>(currentScenes.Select(s => s.Id));
                var persistedIds = new HashSet<string>(data.SceneContentHashes.Keys);

                if (!currentIds.SetEquals(persistedIds))
                {
                    stale = true;
                }
                else
                {
                    foreach (var scene in currentScenes)
                    {
                        var currentHash = NlpCacheService.ComputeHash(scene.Content ?? string.Empty);
                        if (!data.SceneContentHashes.TryGetValue(scene.Id, out var savedHash)
                            || currentHash != savedHash)
                        {
                            stale = true;
                            break;
                        }
                    }
                }

                var timestamp = data.AnalyzedAtUtc.ToLocalTime().ToString("MMM d, yyyy 'at' h:mm tt");
                if (stale)
                {
                    AnalysisStatusText = $"Analysis from {timestamp} \u2014 content has changed since";
                    AnalysisStatusColor = "#E5C07B";
                }
                else
                {
                    AnalysisStatusText = $"Analysis from {timestamp}";
                    AnalysisStatusColor = "#9E9AA0";
                }
            }
            catch
            {
                // Silently ignore — user can always re-run analysis
            }
        }

        [RelayCommand]
        private async Task ScanEntities()
        {
            if (SelectedBook is null) return;

            IsScanning = true;
            ScanProgress = "Scanning\u2026";
            CharacterCandidates = [];
            LocationCandidates  = [];
            ItemCandidates      = [];
            HasCharacterCandidates = false;
            HasLocationCandidates  = false;
            HasItemCandidates      = false;

            try
            {
                // Collect all sentences from every non-empty scene in the book
                var allSentences = SelectedBook.Chapters
                    .SelectMany(c => c.Scenes)
                    .Where(s => !string.IsNullOrWhiteSpace(s.Content))
                    .SelectMany(s => NlpTextExtractor.SplitSentences(
                                    NlpTextExtractor.ExtractPlainText(s.Content)))
                    .ToList();

                if (allSentences.Count == 0)
                {
                    ScanProgress = "No text found in book.";
                    return;
                }

                ScanProgress = "Loading entity recognition model\u2026";
                await _nerService.LoadAsync();

                ScanProgress = "Extracting entities\u2026";
                var entities = await Task.Run(() => _nerService.ExtractEntities(allSentences));

                // Run spatial keyword heuristic for fictional locations
                ScanProgress = "Loading POS tagger\u2026";
                await _posTaggingService.LoadAsync();

                ScanProgress = "Scanning for locations\u2026";
                var tagged = await Task.Run(() => _posTaggingService.TagSentences(allSentences));

                // Build character name set from WikiNER Person results to exclude from locations
                var nerCharacterNames = entities
                    .Where(e => e.Type == Models.NerEntityType.Character)
                    .Select(e => e.Name.ToLowerInvariant())
                    .ToHashSet() as IReadOnlySet<string>;

                var heuristicLocs = await Task.Run(
                    () => _locationHeuristic.FindLocationCandidates(allSentences, tagged, nerCharacterNames));

                // Merge heuristic locations into the entities list (avoid duplicates)
                var existingLocNames = entities
                    .Where(e => e.Type == Models.NerEntityType.Location)
                    .Select(e => e.Name.ToLowerInvariant())
                    .ToHashSet();

                foreach (var (name, count) in heuristicLocs)
                {
                    if (!existingLocNames.Contains(name.ToLowerInvariant()))
                    {
                        entities.Add((name, Models.NerEntityType.Location, count));
                        existingLocNames.Add(name.ToLowerInvariant());
                    }
                }

                // Build case-insensitive sets of already-known entity names for dedup
                var knownChars = SelectedBook.Characters
                    .Select(c => c.Name.Trim().ToLowerInvariant()).ToHashSet();
                var knownLocs = SelectedBook.Locations
                    .Select(l => l.Name.Trim().ToLowerInvariant()).ToHashSet();
                var knownItems = SelectedBook.Items
                    .Select(i => i.Name.Trim().ToLowerInvariant()).ToHashSet();

                var chars = new List<NerCandidateViewModel>();
                var locs  = new List<NerCandidateViewModel>();
                var items = new List<NerCandidateViewModel>();

                foreach (var (name, type, count) in entities)
                {
                    var lower = name.ToLowerInvariant();
                    var vm = new NerCandidateViewModel { Name = name, Type = type, Count = count };
                    switch (type)
                    {
                        case Models.NerEntityType.Character when !knownChars.Contains(lower):
                            chars.Add(vm);
                            break;
                        case Models.NerEntityType.Location when !knownLocs.Contains(lower):
                            locs.Add(vm);
                            break;
                        case Models.NerEntityType.Item when !knownItems.Contains(lower):
                            items.Add(vm);
                            break;
                    }
                }

                CharacterCandidates    = new ObservableCollection<NerCandidateViewModel>(chars);
                LocationCandidates     = new ObservableCollection<NerCandidateViewModel>(locs);
                ItemCandidates         = new ObservableCollection<NerCandidateViewModel>(items);
                HasCharacterCandidates = chars.Count > 0;
                HasLocationCandidates  = locs.Count > 0;
                HasItemCandidates      = items.Count > 0;

                var total = chars.Count + locs.Count + items.Count;
                if (total > 0)
                {
                    ScanProgress = $"Found {total} new entit{(total == 1 ? "y" : "ies")}.";
                }
                else if (entities.Count > 0)
                {
                    // Entities were found but all filtered as already-known
                    ScanProgress = $"All {entities.Count} detected entities already exist in your book.";
                }
                else
                {
                    // Nothing detected at all — show raw label names for diagnostics
                    var rawLabels = _nerService.LastRawEntityTypes;
                    ScanProgress = rawLabels.Count > 0
                        ? $"No mappable entities found. Raw labels seen: {string.Join(", ", rawLabels)}"
                        : "No entities detected. The WikiNER model may not have tagged any spans.";
                }
            }
            catch (Exception ex)
            {
                ScanProgress = $"Scan failed: {ex.Message.Split('\n')[0]}";
            }
            finally
            {
                IsScanning = false;
            }
        }

        [RelayCommand]
        private async Task AddCheckedCharacters()
        {
            if (SelectedBook is null) return;
            var toAdd = CharacterCandidates.Where(c => c.IsSelected).ToList();
            foreach (var candidate in toAdd)
            {
                SelectedBook.Characters.Add(new Models.Character { Name = candidate.Name });
                CharacterCandidates.Remove(candidate);
            }
            HasCharacterCandidates = CharacterCandidates.Count > 0;
            if (toAdd.Count > 0)
            {
                await TriggerSaveAsync();
                EntitiesChanged?.Invoke();
            }
        }

        [RelayCommand]
        private async Task AddCheckedLocations()
        {
            if (SelectedBook is null) return;
            var toAdd = LocationCandidates.Where(c => c.IsSelected).ToList();
            foreach (var candidate in toAdd)
            {
                SelectedBook.Locations.Add(new Models.Location { Name = candidate.Name });
                LocationCandidates.Remove(candidate);
            }
            HasLocationCandidates = LocationCandidates.Count > 0;
            if (toAdd.Count > 0)
            {
                await TriggerSaveAsync();
                EntitiesChanged?.Invoke();
            }
        }

        [RelayCommand]
        private async Task AddCheckedItems()
        {
            if (SelectedBook is null) return;
            var toAdd = ItemCandidates.Where(c => c.IsSelected).ToList();
            foreach (var candidate in toAdd)
            {
                SelectedBook.Items.Add(new Models.Item { Name = candidate.Name });
                ItemCandidates.Remove(candidate);
            }
            HasItemCandidates = ItemCandidates.Count > 0;
            if (toAdd.Count > 0)
            {
                await TriggerSaveAsync();
                EntitiesChanged?.Invoke();
            }
        }

        private void ApplyNoteFilters()
        {
            if (_allNotes.Count == 0)
            {
                NlpNotes = [];
                return;
            }

            IEnumerable<NlpNote> filtered = _allNotes.Where(IsNoteVisible);

            if (SelectedCategoryFilter != "All")
            {
                var categoryMap = new Dictionary<string, NlpNoteCategory>
                {
                    ["Copy Editor"] = NlpNoteCategory.CopyEditor,
                    ["Line Editor"] = NlpNoteCategory.LineEditor,
                    ["Developmental Editor"] = NlpNoteCategory.DevelopmentalEditor
                };
                if (categoryMap.TryGetValue(SelectedCategoryFilter, out var category))
                {
                    filtered = filtered.Where(n => n.Category == category);
                }
            }

            if (SelectedSeverityFilter != "All"
                && Enum.TryParse<NlpNoteSeverity>(SelectedSeverityFilter, out var severity))
            {
                filtered = filtered.Where(n => n.Severity == severity);
            }

            // Sort: Issue first, then Warning, then Info
            var sorted = filtered.OrderBy(n => n.Severity switch
            {
                NlpNoteSeverity.Issue => 0,
                NlpNoteSeverity.Warning => 1,
                _ => 2
            }).ThenBy(n => n.ChapterTitle)
              .ThenBy(n => n.SceneTitle);

            NlpNotes = new ObservableCollection<NlpNote>(sorted);
            RebuildReviseGroups();
        }

        private void ClearSceneNoteCounts()
        {
            if (SelectedBook is null) return;
            foreach (var chapter in SelectedBook.Chapters)
                foreach (var scene in chapter.Scenes)
                    scene.AnalysisNoteCount = 0;
        }

        [RelayCommand]
        private async Task DownloadModels()
        {
            if (IsDownloadingModels) return;
            IsDownloadingModels = true;
            AnalysisProgress = "Downloading models...";

            try
            {
                var progress = new Progress<(string model, double percent)>(p =>
                    AnalysisProgress = $"Downloading {p.model}... {p.percent:F0}%");
                await _modelManager.DownloadModelsAsync(progress);
                AnalysisProgress = "Models downloaded successfully.";
                OnPropertyChanged(nameof(AreModelsAvailable));
            }
            catch (Exception ex)
            {
                AnalysisProgress = $"Download failed: {ex.Message}";
            }
            finally
            {
                IsDownloadingModels = false;
            }
        }

        // ── Reports ──────────────────────────────────────────────────────────

        [RelayCommand]
        private void ShowReports()
        {
            if (SelectedBook is null) return;
            ActiveReportType = "Entities";
            InsightsShowsReport = true;
            ActiveWorkspace = Workspace.Insights;
        }

        [RelayCommand]
        private void ShowNlpReport()
        {
            if (SelectedBook is null || _analysisResults is null || _analysisResults.Count == 0) return;
            ActiveReportType = "NLP";
            InsightsShowsReport = true;
            ActiveWorkspace = Workspace.Insights;
        }

        [RelayCommand]
        private void SwitchReportType(string reportType)
        {
            if (reportType == ActiveReportType) return;
            ActiveReportType = reportType;
            ReportsPanelShown?.Invoke();
        }

        [RelayCommand]
        private void CloseReports()
        {
            ActiveWorkspace = Workspace.Write;
        }

        public bool HasAnalysisResults => _analysisResults is not null && _analysisResults.Count > 0;

        [RelayCommand]
        private void NavigateToNote(NlpNote note)
        {
            if (SelectedBook is null || string.IsNullOrEmpty(note.SceneId)) return;

            foreach (var chapter in SelectedBook.Chapters)
            {
                var scene = chapter.Scenes.FirstOrDefault(s => s.Id == note.SceneId);
                if (scene is null) continue;

                // Make sure an editor is showing (Write or Revise), then let it record
                // where to land before the scene switch loads it
                if (!IsEditorHostVisible) ActiveWorkspace = Workspace.Write;
                NoteNavigationRequested?.Invoke(note);
                if (SelectedScene != scene)
                {
                    SelectedChapter = chapter;
                    SelectedScene = scene;
                }
                return;
            }
        }

        private void PopulateSceneNoteCounts(List<NlpNote> notes)
        {
            if (SelectedBook is null) return;

            // Build a lookup: sceneId -> count
            var countByScene = notes
                .Where(n => !string.IsNullOrEmpty(n.SceneId) && IsNoteVisible(n))
                .GroupBy(n => n.SceneId)
                .ToDictionary(g => g.Key, g => g.Count());

            foreach (var chapter in SelectedBook.Chapters)
            {
                foreach (var scene in chapter.Scenes)
                {
                    scene.AnalysisNoteCount = countByScene.GetValueOrDefault(scene.Id, 0);
                }
            }
        }

        /// <summary>
        /// Builds a JSON string with all analysis data needed by the NLP report HTML.
        /// </summary>
        public string BuildNlpReportJson()
        {
            if (_analysisResults is null || _analysisResults.Count == 0)
                return "{}";

            var scenes = _analysisResults.Select(r => new
            {
                title = r.SceneTitle,
                chapter = r.ChapterTitle,
                wordCount = r.Style.TotalWords,
                sentenceCount = r.Style.TotalSentences,
                avgSentenceLength = Math.Round(r.Style.AverageSentenceLength, 1),
                vocabRichness = Math.Round(r.Style.VocabularyRichness, 3),
                contractionRate = Math.Round(r.Style.ContractionRate, 3),
                dialogueRatio = Math.Round(r.Style.DialogueRatio, 3),
                chapterSimilarity = Math.Round(r.ChapterSimilarity, 3),
                noteCount = r.Notes.Count,
                emotions = AggregateSceneEmotions(r)
            }).ToList();

            var notes = NlpNotes.Select(n => new
            {
                severity = n.Severity.ToString().ToLower(),
                category = n.CategoryLabel.ToLower(),
                scene = n.SceneTitle,
                chapter = n.ChapterTitle,
                sceneId = n.SceneId,
                sentenceIndex = n.SentenceIndex ?? -1,
                fingerprint = n.Fingerprint,
                message = n.Message
            }).ToList();

            // Character voice profiles for the radar chart + consistency timeline
            var colors = new[] { "#A89EC9", "#7EC8A4", "#C8B07A", "#61AFEF", "#E06C75" };
            var characterProfiles = _voiceProfiles.Take(8).Select((p, idx) => new
            {
                id = p.CharacterId,
                name = p.CharacterName,
                color = colors[idx % colors.Length],
                sceneCount = p.SceneCount,
                totalWords = p.TotalWords,
                // Radar axes — raw values (normalized in JS against all character max values)
                avgSentenceLength = Math.Round(p.Style.AverageSentenceLength, 1),
                vocabRichness = Math.Round(p.Style.VocabularyRichness, 3),
                contractionRate = Math.Round(p.Style.ContractionRate, 3),
                dialogueRatio = Math.Round(p.Style.DialogueRatio, 3),
                adverbDensity = Math.Round(p.AdverbDensity, 4),
                passiveVoiceRate = Math.Round(p.PassiveVoiceRate, 3),
                clauseComplexity = Math.Round(p.ClauseComplexity, 3),
                readabilityScore = Math.Round(p.ReadabilityScore, 1),
                // Top 10 distinctive words for word-chip cloud
                distinctiveWords = p.DistinctiveWords.Take(10).Select(w => new
                {
                    word = w.Word,
                    weight = Math.Round(w.TfIdf, 4)
                }).ToList(),
                // Per-scene snapshots for the consistency timeline
                sceneSnapshots = p.SceneSnapshots.Select(s => new
                {
                    scene = s.SceneTitle,
                    chapter = s.ChapterTitle,
                    avgSentenceLength = Math.Round(s.AvgSentenceLength, 1),
                    vocabRichness = Math.Round(s.VocabularyRichness, 3),
                    contractionRate = Math.Round(s.ContractionRate, 3),
                    dialogueRatio = Math.Round(s.DialogueRatio, 3),
                    adverbDensity = Math.Round(s.AdverbDensity, 4),
                    passiveVoiceRate = Math.Round(s.PassiveVoiceRate, 3),
                    readabilityScore = Math.Round(s.ReadabilityScore, 1)
                }).ToList()
            }).ToList();

            // Dialogue voice profiles for the dialogue fingerprint radar chart
            var dialogueColors = new[] { "#E5C07B", "#61C88A", "#C678DD", "#56B6C2", "#E06C75" };
            var dialogueProfiles = _dialogueProfiles.Take(8).Select((p, idx) => new
            {
                id = p.CharacterId,
                name = p.CharacterName,
                color = dialogueColors[idx % dialogueColors.Length],
                dialogueLineCount = p.DialogueLineCount,
                sceneCount = p.SceneCount,
                totalWords = p.TotalWords,
                avgSentenceLength = Math.Round(p.AvgSentenceLength, 1),
                vocabRichness = Math.Round(p.VocabularyRichness, 3),
                contractionRate = Math.Round(p.ContractionRate, 3),
                exclamationRate = Math.Round(p.ExclamationRate, 3),
                adverbDensity = Math.Round(p.AdverbDensity, 4),
                passiveVoiceRate = Math.Round(p.PassiveVoiceRate, 3),
                clauseComplexity = Math.Round(p.ClauseComplexity, 3),
                readabilityScore = Math.Round(p.ReadabilityScore, 1),
                distinctiveWords = p.DistinctiveWords.Take(10).Select(w => new
                {
                    word = w.Word,
                    weight = Math.Round(w.TfIdf, 4)
                }).ToList()
            }).ToList();

            return System.Text.Json.JsonSerializer.Serialize(
                new { scenes, notes, characterProfiles, dialogueProfiles },
                new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase });
        }

        private static object? AggregateSceneEmotions(Models.Analysis.SceneAnalysisResult result)
        {
            var allEmotions = result.Sentences
                .Where(s => s.Emotions.Count > 0)
                .SelectMany(s => s.Emotions)
                .Where(e => e.Label != Models.Analysis.EmotionLabel.Neutral)
                .ToList();

            if (allEmotions.Count == 0) return null;

            var labelSums = new Dictionary<string, double>();
            foreach (var (label, conf) in allEmotions)
            {
                var key = label.ToString().ToLower();
                labelSums[key] = labelSums.GetValueOrDefault(key) + conf;
            }

            var total = labelSums.Values.Sum();
            if (total <= 0) return null;

            return labelSums
                .Where(kv => kv.Value > 0)
                .ToDictionary(kv => kv.Key, kv => Math.Round(kv.Value / total, 3));
        }

        /// <summary>
        /// Builds a JSON string describing entity co-occurrence relationships
        /// across all scenes in the selected book.  The JSON shape is:
        /// <code>{ nodes: [{ id, label, type, scenes }], edges: [{ from, to, value }] }</code>
        /// where <c>value</c> is the number of scenes in which both entities appear together.
        /// </summary>
        public string BuildEntityRelationshipJson()
        {
            if (SelectedBook is null) return "{\"nodes\":[],\"edges\":[]}";

            // ── Collect every entity the book defines ────────────────────────
            var entityMeta = new Dictionary<string, (string name, string type, string? aka)>();
            foreach (var c in SelectedBook.Characters) entityMeta[c.Id] = (c.Name, "character", c.Aka);
            foreach (var l in SelectedBook.Locations)  entityMeta[l.Id] = (l.Name, "location",  l.Aka);
            foreach (var i in SelectedBook.Items)      entityMeta[i.Id] = (i.Name, "item",      i.Aka);

            // ── Full-book auto-detection pass ────────────────────────────────
            // The per-scene entity lists (CharacterIds etc.) are populated when
            // a scene is opened in the editor.  Scenes the user hasn't visited
            // since entity-tagging was added still have empty lists.  Run a
            // one-time scan across ALL scenes so the report reflects reality.
            foreach (var chapter in SelectedBook.Chapters)
            foreach (var scene in chapter.Scenes)
            {
                foreach (var c in SelectedBook.Characters)
                    if (IsEntityDetectedInContent(scene.Content, c.Name, c.Aka)
                        && !scene.CharacterIds.Contains(c.Id))
                        scene.CharacterIds.Add(c.Id);

                foreach (var l in SelectedBook.Locations)
                    if (IsEntityDetectedInContent(scene.Content, l.Name, l.Aka)
                        && !scene.LocationIds.Contains(l.Id))
                        scene.LocationIds.Add(l.Id);

                foreach (var i in SelectedBook.Items)
                    if (IsEntityDetectedInContent(scene.Content, i.Name, i.Aka)
                        && !scene.ItemIds.Contains(i.Id))
                        scene.ItemIds.Add(i.Id);
            }

            // ── Walk every scene and count co-occurrences ────────────────────
            var edgeCounts  = new Dictionary<string, int>();   // "id1|id2" → scene count
            var sceneCounts = new Dictionary<string, int>();   // entity id → scene count

            foreach (var chapter in SelectedBook.Chapters)
            foreach (var scene in chapter.Scenes)
            {
                var ids = scene.CharacterIds
                    .Concat(scene.LocationIds)
                    .Concat(scene.ItemIds)
                    .Where(id => entityMeta.ContainsKey(id))   // skip orphan refs
                    .Distinct()
                    .OrderBy(id => id, StringComparer.Ordinal)
                    .ToList();

                foreach (var id in ids)
                    sceneCounts[id] = sceneCounts.GetValueOrDefault(id) + 1;

                for (int a = 0; a < ids.Count; a++)
                for (int b = a + 1; b < ids.Count; b++)
                {
                    var key = $"{ids[a]}|{ids[b]}";
                    edgeCounts[key] = edgeCounts.GetValueOrDefault(key) + 1;
                }
            }

            // ── Build nodes: ALL entities in the book, not just tagged ones ──
            var nodes = entityMeta
                .Select(kv => new
                {
                    id     = kv.Key,
                    label  = kv.Value.name,
                    type   = kv.Value.type,
                    scenes = sceneCounts.GetValueOrDefault(kv.Key, 0)
                })
                .ToList();

            var edges = edgeCounts
                .Select(kv =>
                {
                    var parts = kv.Key.Split('|');
                    return new { from = parts[0], to = parts[1], value = kv.Value };
                })
                .ToList();

            return System.Text.Json.JsonSerializer.Serialize(
                new { nodes, edges },
                new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase });
        }

        [RelayCommand]
        private void RefreshStatistics()
        {
            if (SelectedBook is not null)
                ComputeStatistics();
        }

        // ── Entity preview ──────────────────────────────────────────────────

        public void ShowEntityPreview(string id, string type)
        {
            if (SelectedBook is null) return;
            EntityPreview? preview = type switch
            {
                "character" => SelectedBook.Characters.FirstOrDefault(c => c.Id == id) is { } ch
                    ? new EntityPreview
                    {
                        Id = id, Type = type, TypeLabel = "Character",
                        Name = ch.Name, FullName = ch.FullName,
                        Description = ch.Description, Notes = ch.Notes,
                        ImagePath = _imageService.GetFullImagePath(ch.ImagePath)
                    } : null,
                "location" => SelectedBook.Locations.FirstOrDefault(l => l.Id == id) is { } loc
                    ? new EntityPreview
                    {
                        Id = id, Type = type, TypeLabel = "Location",
                        Name = loc.Name,
                        Description = loc.Description, Notes = loc.Notes,
                        ImagePath = _imageService.GetFullImagePath(loc.ImagePath)
                    } : null,
                "item" => SelectedBook.Items.FirstOrDefault(i => i.Id == id) is { } itm
                    ? new EntityPreview
                    {
                        Id = id, Type = type, TypeLabel = "Item",
                        Name = itm.Name,
                        Description = itm.Description, Notes = itm.Notes
                    } : null,
                _ => null
            };
            if (preview is null) return;
            EntityPreview = preview;
            IsEntityPreviewVisible = true;
        }

        [RelayCommand]
        private void CloseEntityPreview() => IsEntityPreviewVisible = false;

        [RelayCommand]
        private void EditEntityFromPreview()
        {
            if (EntityPreview is null) return;
            var id = EntityPreview.Id;
            var type = EntityPreview.Type;
            IsEntityPreviewVisible = false;
            switch (type)
            {
                case "character":
                    SelectedCharacter = SelectedBook?.Characters.FirstOrDefault(c => c.Id == id);
                    break;
                case "location":
                    SelectedLocation = SelectedBook?.Locations.FirstOrDefault(l => l.Id == id);
                    break;
                case "item":
                    SelectedItem = SelectedBook?.Items.FirstOrDefault(i => i.Id == id);
                    break;
            }
        }

        // ── Entity manifest for JS highlighting ─────────────────────────────

        public string GetEntityManifestJson()
        {
            if (SelectedBook is null) return "[]";
            var entities = new List<object>();
            foreach (var c in SelectedBook.Characters)
                entities.Add(new { id = c.Id, type = "character", name = c.Name, aliases = SplitAka(c.Aka) });
            foreach (var l in SelectedBook.Locations)
                entities.Add(new { id = l.Id, type = "location", name = l.Name, aliases = SplitAka(l.Aka) });
            foreach (var i in SelectedBook.Items)
                entities.Add(new { id = i.Id, type = "item", name = i.Name, aliases = SplitAka(i.Aka) });
            return System.Text.Json.JsonSerializer.Serialize(entities);
        }

        private static string[] SplitAka(string? aka) =>
            string.IsNullOrEmpty(aka) ? [] : aka.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        private static readonly HashSet<string> _stopwords = new(StringComparer.OrdinalIgnoreCase)
        {
            "the","a","an","and","or","but","in","on","at","to","for","of","with","by",
            "from","is","was","are","were","be","been","being","have","has","had","do",
            "does","did","will","would","could","should","may","might","shall","can","not",
            "no","nor","so","yet","both","either","neither","that","this","these","those",
            "it","its","he","she","they","we","you","i","me","him","her","them","us","my",
            "your","his","their","our","as","if","when","while","because","although",
            "though","even","just","up","out","about","into","than","then","also","more",
            "which","who","what","there","all","said","s","t","re","ll","ve","m","d"
        };

        private void ComputeStatistics()
        {
            if (SelectedBook is null) return;

            var allWords = new List<string>();
            var charWords  = new Dictionary<string, int>(StringComparer.Ordinal);
            var charScenes = new Dictionary<string, int>(StringComparer.Ordinal);

            foreach (var chapter in SelectedBook.Chapters)
            foreach (var scene in chapter.Scenes)
            {
                if (scene.Status == SceneStatus.Outline) continue;
                var words = Scene.ExtractWords(scene.Content);
                allWords.AddRange(words);

                foreach (var vpId in scene.ViewpointCharacterIds)
                {
                    var name = SelectedBook.Characters
                        .FirstOrDefault(c => c.Id == vpId)?.Name ?? "Unknown";
                    charWords[name]  = charWords.GetValueOrDefault(name)  + words.Count;
                    charScenes[name] = charScenes.GetValueOrDefault(name) + 1;
                }
            }

            int total = allWords.Count;

            // ── Frequency (top 50 all words) ──────────────────────────────
            var freqList = allWords.GroupBy(w => w)
                .OrderByDescending(g => g.Count())
                .Take(50)
                .Select(g => new WordFrequencyEntry
                {
                    Word       = g.Key,
                    Count      = g.Count(),
                    Percentage = total > 0 ? (double)g.Count() / total * 100 : 0
                })
                .ToList();
            double freqMax = freqList.Count > 0 ? freqList[0].Count : 1;
            foreach (var e in freqList) e.ProgressValue = e.Count / freqMax;
            WordFrequencyData = new ObservableCollection<WordFrequencyEntry>(freqList);

            // ── Problem words (non-stopwords appearing > 0.5% and > 3 times) ─
            ProblemWordsData = new ObservableCollection<WordFrequencyEntry>(
                allWords.GroupBy(w => w)
                    .Where(g => !_stopwords.Contains(g.Key)
                             && g.Count() > 3
                             && total > 0
                             && (double)g.Count() / total * 100 > 0.5)
                    .OrderByDescending(g => g.Count())
                    .Take(30)
                    .Select(g => new WordFrequencyEntry
                    {
                        Word       = g.Key,
                        Count      = g.Count(),
                        Percentage = total > 0 ? (double)g.Count() / total * 100 : 0
                    }));

            // ── Words per character ────────────────────────────────────────
            CharacterWordCountData = new ObservableCollection<CharacterWordCount>(
                charWords
                    .OrderByDescending(kv => kv.Value)
                    .Select(kv => new CharacterWordCount
                    {
                        CharacterName = kv.Key,
                        WordCount     = kv.Value,
                        SceneCount    = charScenes.GetValueOrDefault(kv.Key)
                    }));

            // ── Daily progress ─────────────────────────────────────────────
            var snapshots = SelectedBook.DailyWordSnapshots;
            var sortedDates = snapshots.Keys.OrderBy(d => d).ToList();
            var dailyEntries = new List<DailyProgressEntry>(sortedDates.Count);
            for (int i = sortedDates.Count - 1; i >= 0; i--)
            {
                var date  = sortedDates[i];
                var wc    = snapshots[date];
                var delta = i > 0 ? wc - snapshots[sortedDates[i - 1]] : wc;
                dailyEntries.Add(new DailyProgressEntry
                {
                    Date  = date,
                    Delta = delta,
                    Total = wc
                });
            }
            DailyProgressData = new ObservableCollection<DailyProgressEntry>(dailyEntries);
        }

        // ── Sidebar mode ────────────────────────────────────────────────────

        [RelayCommand]
        private void SetSidebarMode(string mode)
        {
            SidebarMode = Enum.Parse<SidebarMode>(mode);
        }

        // ── Character commands ──────────────────────────────────────────────

        [RelayCommand]
        private async Task AddCharacter()
        {
            if (SelectedBook is null) return;
            var character = new Character { Name = "New Character" };
            SelectedBook.Characters.Add(character);
            SelectedCharacter = character;
            await TriggerSaveAsync();
            EntitiesChanged?.Invoke();
        }

        [RelayCommand]
        private async Task DeleteCharacter(Character character)
        {
            if (SelectedBook is null) return;
            if (SelectedCharacter == character)
                SelectedCharacter = null;
            _imageService.DeleteImage(character.ImagePath);
            SelectedBook.Characters.Remove(character);
            await TriggerSaveAsync();
            EntitiesChanged?.Invoke();
        }

        [RelayCommand]
        private async Task RenameCharacter(Character character)
        {
            var name = await Shell.Current.DisplayPromptAsync(
                "Rename Character", "Enter a new name:", initialValue: character.Name);
            if (name is not null)
            {
                character.Name = name;
                if (SelectedBook is not null)
                {
                    var idx = SelectedBook.Characters.IndexOf(character);
                    if (idx >= 0) { SelectedBook.Characters.RemoveAt(idx); SelectedBook.Characters.Insert(idx, character); }
                }
                if (SelectedCharacter == character)
                    OnPropertyChanged(nameof(SelectedCharacter));
                await TriggerSaveAsync();
                EntitiesChanged?.Invoke();
            }
        }

        [RelayCommand]
        private async Task PickCharacterImage()
        {
            if (SelectedBook is null || SelectedCharacter is null) return;
            var path = await _imageService.PickAndSaveImageAsync(SelectedBook.Id);
            if (!string.IsNullOrEmpty(path))
            {
                _imageService.DeleteImage(SelectedCharacter.ImagePath);
                SelectedCharacter.ImagePath = path;
                OnPropertyChanged(nameof(SelectedCharacter));
                await TriggerSaveAsync();
            }
        }

        // ── Location commands ───────────────────────────────────────────────

        [RelayCommand]
        private async Task AddLocation()
        {
            if (SelectedBook is null) return;
            var location = new Location { Name = "New Location" };
            SelectedBook.Locations.Add(location);
            SelectedLocation = location;
            await TriggerSaveAsync();
            EntitiesChanged?.Invoke();
        }

        [RelayCommand]
        private async Task DeleteLocation(Location location)
        {
            if (SelectedBook is null) return;
            if (SelectedLocation == location)
                SelectedLocation = null;
            _imageService.DeleteImage(location.ImagePath);
            SelectedBook.Locations.Remove(location);
            await TriggerSaveAsync();
            EntitiesChanged?.Invoke();
        }

        [RelayCommand]
        private async Task RenameLocation(Location location)
        {
            var name = await Shell.Current.DisplayPromptAsync(
                "Rename Location", "Enter a new name:", initialValue: location.Name);
            if (name is not null)
            {
                location.Name = name;
                if (SelectedBook is not null)
                {
                    var idx = SelectedBook.Locations.IndexOf(location);
                    if (idx >= 0) { SelectedBook.Locations.RemoveAt(idx); SelectedBook.Locations.Insert(idx, location); }
                }
                if (SelectedLocation == location)
                    OnPropertyChanged(nameof(SelectedLocation));
                await TriggerSaveAsync();
                EntitiesChanged?.Invoke();
            }
        }

        [RelayCommand]
        private async Task PickLocationImage()
        {
            if (SelectedBook is null || SelectedLocation is null) return;
            var path = await _imageService.PickAndSaveImageAsync(SelectedBook.Id);
            if (!string.IsNullOrEmpty(path))
            {
                _imageService.DeleteImage(SelectedLocation.ImagePath);
                SelectedLocation.ImagePath = path;
                OnPropertyChanged(nameof(SelectedLocation));
                await TriggerSaveAsync();
            }
        }

        // ── Item commands ───────────────────────────────────────────────────

        [RelayCommand]
        private async Task AddItem()
        {
            if (SelectedBook is null) return;
            var item = new Item { Name = "New Item" };
            SelectedBook.Items.Add(item);
            SelectedItem = item;
            await TriggerSaveAsync();
            EntitiesChanged?.Invoke();
        }

        [RelayCommand]
        private async Task DeleteItem(Item item)
        {
            if (SelectedBook is null) return;
            if (SelectedItem == item)
                SelectedItem = null;
            SelectedBook.Items.Remove(item);
            await TriggerSaveAsync();
            EntitiesChanged?.Invoke();
        }

        [RelayCommand]
        private async Task RenameItem(Item item)
        {
            var name = await Shell.Current.DisplayPromptAsync(
                "Rename Item", "Enter a new name:", initialValue: item.Name);
            if (name is not null)
            {
                item.Name = name;
                if (SelectedBook is not null)
                {
                    var idx = SelectedBook.Items.IndexOf(item);
                    if (idx >= 0) { SelectedBook.Items.RemoveAt(idx); SelectedBook.Items.Insert(idx, item); }
                }
                if (SelectedItem == item)
                    OnPropertyChanged(nameof(SelectedItem));
                await TriggerSaveAsync();
                EntitiesChanged?.Invoke();
            }
        }

        // ── Element detail save ─────────────────────────────────────────────

        public void SaveElementDetails()
        {
            TriggerSaveAsync().ConfigureAwait(false);
            EntitiesChanged?.Invoke();
        }

        public void TriggerSave() => TriggerSaveAsync().ConfigureAwait(false);

        // ── Reorder ───────────────────────────────────────────────────────────

        public void MoveBook(Book book, int desiredIndex)
        {
            var oldIndex = Books.IndexOf(book);
            if (oldIndex < 0) return;
            var moveIndex = Math.Clamp(
                desiredIndex > oldIndex ? desiredIndex - 1 : desiredIndex,
                0, Books.Count - 1);
            if (oldIndex == moveIndex) return;
            Books.Move(oldIndex, moveIndex);
            TriggerSaveAsync().ConfigureAwait(false);
        }

        public void MoveChapter(Chapter chapter, int desiredIndex)
        {
            if (SelectedBook is null) return;
            var chapters = SelectedBook.Chapters;
            var oldIndex = chapters.IndexOf(chapter);
            if (oldIndex < 0) return;
            var moveIndex = Math.Clamp(
                desiredIndex > oldIndex ? desiredIndex - 1 : desiredIndex,
                0, chapters.Count - 1);
            if (oldIndex == moveIndex) return;
            chapters.Move(oldIndex, moveIndex);
            TriggerSaveAsync().ConfigureAwait(false);
        }

        public void MoveScene(Scene scene, int desiredIndex)
        {
            var chapter = FindChapterOf(scene);
            if (chapter is null) return;
            var scenes = chapter.Scenes;
            var oldIndex = scenes.IndexOf(scene);
            if (oldIndex < 0) return;
            var moveIndex = Math.Clamp(
                desiredIndex > oldIndex ? desiredIndex - 1 : desiredIndex,
                0, scenes.Count - 1);
            if (oldIndex == moveIndex) return;
            scenes.Move(oldIndex, moveIndex);
            TriggerSaveAsync().ConfigureAwait(false);
        }

        // ── Persistence ───────────────────────────────────────────────────────

        private async Task TriggerSaveAsync()
        {
            // Snapshot every book's current word count for daily progress tracking
            var today = DateTime.Today.ToString("yyyy-MM-dd");
            foreach (var book in Books)
                book.DailyWordSnapshots[today] = book.WordCount;

            SetSaveState("Saving…");
            try
            {
                await _bookService.SaveBooksAsync(Books.ToList());
                SetSaveState("All changes saved");
            }
            catch (Exception ex)
            {
                SetSaveState("Save failed — " + ex.Message.Split('\n')[0]);
                throw;
            }
        }
    }
}
