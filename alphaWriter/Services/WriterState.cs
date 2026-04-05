using alphaWriter.Models;
using alphaWriter.Models.Analysis;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;
using Location = alphaWriter.Models.Location;

namespace alphaWriter.Services
{
    /// <summary>
    /// Shared application state injected into all ViewModels.
    /// Holds Books, selection state, and the persistence mechanism.
    /// </summary>
    public partial class WriterState : ObservableObject
    {
        private readonly IBookService _bookService;
        private System.Threading.Timer? _saveTimer;
        private bool _initialized;

        public WriterState(IBookService bookService)
        {
            _bookService = bookService;
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
        private Character? selectedCharacter;

        [ObservableProperty]
        private Location? selectedLocation;

        [ObservableProperty]
        private Item? selectedItem;

        // Raised when the selected scene changes so the page can update the WebView
        public event Action<Scene?>? SelectedSceneChanged;

        // Convenience flags
        public bool HasSelectedBook => SelectedBook is not null;
        public bool HasSelectedChapter => SelectedChapter is not null;
        public bool HasSelectedScene => SelectedScene is not null;

        public string CurrentBookTitle => SelectedBook?.Title ?? "No Book";

        // ── Analysis results (shared across Analysis, Reports, Editor VMs) ──
        public List<SceneAnalysisResult>? AnalysisResults { get; set; }
        public List<NlpNote> AllNotes { get; set; } = [];
        public List<CharacterVoiceProfile> VoiceProfiles { get; set; } = [];
        public List<DialogueVoiceProfile> DialogueProfiles { get; set; } = [];

        // ── Lifecycle ──────────────────────────────────────────────────────

        public async Task InitializeAsync()
        {
            if (_initialized) return;
            _initialized = true;

            var loaded = await _bookService.LoadBooksAsync();
            Books = new ObservableCollection<Book>(loaded);

            var lastId = Preferences.Default.Get("lastBookId", string.Empty);
            if (!string.IsNullOrEmpty(lastId))
            {
                var lastBook = Books.FirstOrDefault(b => b.Id == lastId);
                if (lastBook is not null)
                    SelectedBook = lastBook;
            }
        }

        // ── Selection change handlers ─────────────────────────────────────

        partial void OnSelectedBookChanged(Book? value)
        {
            SelectedChapter = null;
            SelectedScene = null;
            SelectedCharacter = null;
            SelectedLocation = null;
            SelectedItem = null;

            // Clear stale analysis data
            AnalysisResults = null;
            AllNotes = [];
            VoiceProfiles = [];
            DialogueProfiles = [];

            OnPropertyChanged(nameof(HasSelectedBook));
            OnPropertyChanged(nameof(CurrentBookTitle));

            Preferences.Default.Set("lastBookId", value?.Id ?? string.Empty);
        }

        partial void OnSelectedChapterChanged(Chapter? value)
        {
            SelectedScene = null;
            OnPropertyChanged(nameof(HasSelectedChapter));
        }

        partial void OnSelectedSceneChanged(Scene? value)
        {
            if (value is not null)
            {
                SelectedCharacter = null;
                SelectedLocation = null;
                SelectedItem = null;
            }

            // Fire event FIRST so the WebView flush runs while the Grid is still visible.
            SelectedSceneChanged?.Invoke(value);

            OnPropertyChanged(nameof(HasSelectedScene));
        }

        partial void OnSelectedCharacterChanged(Character? value)
        {
            if (value is not null)
            {
                SelectedLocation = null;
                SelectedItem = null;
                SelectedScene = null;
            }
        }

        partial void OnSelectedLocationChanged(Location? value)
        {
            if (value is not null)
            {
                SelectedCharacter = null;
                SelectedItem = null;
                SelectedScene = null;
            }
        }

        partial void OnSelectedItemChanged(Item? value)
        {
            if (value is not null)
            {
                SelectedCharacter = null;
                SelectedLocation = null;
                SelectedScene = null;
            }
        }

        // ── Persistence ──────────────────────────────────────────────────

        public void DebouncedSave()
        {
            _saveTimer?.Dispose();
            _saveTimer = new System.Threading.Timer(
                async _ => await TriggerSaveAsync(),
                null,
                TimeSpan.FromMilliseconds(1500),
                Timeout.InfiniteTimeSpan);
        }

        public void TriggerSave() => TriggerSaveAsync().ConfigureAwait(false);

        public async Task TriggerSaveAsync()
        {
            var today = DateTime.Today.ToString("yyyy-MM-dd");
            foreach (var book in Books)
                book.DailyWordSnapshots[today] = book.WordCount;

            await _bookService.SaveBooksAsync(Books.ToList());
        }
    }
}
