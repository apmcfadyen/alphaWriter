using alphaWriter.Models;
using alphaWriter.Services;
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
        private System.Threading.Timer? _saveTimer;

        // Raised when the selected scene changes so the page can update the WebView
        public event Action<Scene?>? SelectedSceneChanged;

        public WriterViewModel(IBookService bookService, IImageService imageService)
        {
            _bookService = bookService;
            _imageService = imageService;
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
        private SidebarMode sidebarMode = SidebarMode.Chapters;

        [ObservableProperty]
        private Character? selectedCharacter;

        [ObservableProperty]
        private Location? selectedLocation;

        [ObservableProperty]
        private Item? selectedItem;

        // Convenience flags for IsEnabled bindings in XAML
        public bool HasSelectedBook => SelectedBook is not null;
        public bool HasSelectedChapter => SelectedChapter is not null;
        public bool HasSelectedScene => SelectedScene is not null;

        // Sidebar mode checks for XAML IsVisible bindings
        public bool IsChaptersMode => SidebarMode == SidebarMode.Chapters;
        public bool IsCharactersMode => SidebarMode == SidebarMode.Characters;
        public bool IsLocationsMode => SidebarMode == SidebarMode.Locations;
        public bool IsItemsMode => SidebarMode == SidebarMode.Items;

        // Whether an element editor or scene editor should be visible
        public bool IsElementEditorVisible => SelectedCharacter is not null
                                           || SelectedLocation is not null
                                           || SelectedItem is not null;
        public bool IsSceneEditorVisible => !IsElementEditorVisible;

        // Display name for the book selector button
        public string CurrentBookTitle => SelectedBook?.Title ?? "No Book";

        // ── Lifecycle ──────────────────────────────────────────────────────────

        public async Task InitializeAsync()
        {
            var loaded = await _bookService.LoadBooksAsync();
            Books = new ObservableCollection<Book>(loaded);
        }

        // ── Selection change handlers ──────────────────────────────────────────

        partial void OnSelectedBookChanged(Book? value)
        {
            SelectedChapter = null;
            SelectedScene = null;
            SelectedCharacter = null;
            SelectedLocation = null;
            SelectedItem = null;
            OnPropertyChanged(nameof(HasSelectedBook));
            OnPropertyChanged(nameof(CurrentBookTitle));
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
            WordCount = value?.WordCount ?? 0;
            OnPropertyChanged(nameof(HasSelectedScene));
            OnPropertyChanged(nameof(IsElementEditorVisible));
            OnPropertyChanged(nameof(IsSceneEditorVisible));
            SelectedSceneChanged?.Invoke(value);
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
                SelectedScene = null;
            }
            OnPropertyChanged(nameof(IsElementEditorVisible));
            OnPropertyChanged(nameof(IsSceneEditorVisible));
        }

        partial void OnSelectedLocationChanged(Location? value)
        {
            if (value is not null)
            {
                SelectedCharacter = null;
                SelectedItem = null;
                SelectedScene = null;
            }
            OnPropertyChanged(nameof(IsElementEditorVisible));
            OnPropertyChanged(nameof(IsSceneEditorVisible));
        }

        partial void OnSelectedItemChanged(Item? value)
        {
            if (value is not null)
            {
                SelectedCharacter = null;
                SelectedLocation = null;
                SelectedScene = null;
            }
            OnPropertyChanged(nameof(IsElementEditorVisible));
            OnPropertyChanged(nameof(IsSceneEditorVisible));
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
            var imageDir = Path.Combine(FileSystem.AppDataDirectory, "images", book.Id);
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
            if (SelectedChapter is null) return;
            if (SelectedScene == scene)
                SelectedScene = null;
            SelectedChapter.Scenes.Remove(scene);
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
                if (SelectedChapter is not null)
                {
                    var idx = SelectedChapter.Scenes.IndexOf(scene);
                    if (idx >= 0) { SelectedChapter.Scenes.RemoveAt(idx); SelectedChapter.Scenes.Insert(idx, scene); }
                }
                // Also update selected scene title entry if this is the current scene
                if (SelectedScene == scene)
                    OnPropertyChanged(nameof(SelectedScene));
                await TriggerSaveAsync();
            }
        }

        public void SaveSceneTitle() => TriggerSaveAsync().ConfigureAwait(false);

        // ── Content / editor ──────────────────────────────────────────────────

        public void SaveContent(string htmlContent, int? wordCount = null)
        {
            if (SelectedScene is null) return;
            SelectedScene.Content = htmlContent ?? string.Empty;
            WordCount = wordCount ?? SelectedScene.WordCount;

            // Debounced save: reset the timer on every keystroke
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

        // ── Element editor dismiss ──────────────────────────────────────────

        [RelayCommand]
        private void CloseElementEditor()
        {
            SelectedCharacter = null;
            SelectedLocation = null;
            SelectedItem = null;
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
        }

        [RelayCommand]
        private async Task DeleteItem(Item item)
        {
            if (SelectedBook is null) return;
            if (SelectedItem == item)
                SelectedItem = null;
            SelectedBook.Items.Remove(item);
            await TriggerSaveAsync();
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
            }
        }

        // ── Element detail save ─────────────────────────────────────────────

        public void SaveElementDetails() => TriggerSaveAsync().ConfigureAwait(false);

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
            if (SelectedChapter is null) return;
            var scenes = SelectedChapter.Scenes;
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
            await _bookService.SaveBooksAsync(Books.ToList());
        }
    }
}
