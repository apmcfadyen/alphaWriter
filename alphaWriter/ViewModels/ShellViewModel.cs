using alphaWriter.Models;
using alphaWriter.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using alphaWriter.Messages;
using System.Collections.ObjectModel;

namespace alphaWriter.ViewModels
{
    public partial class ShellViewModel : ObservableObject
    {
        private readonly WriterState _state;

        public ShellViewModel(WriterState state)
        {
            _state = state;
            _state.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(WriterState.SelectedBook))
                {
                    OnPropertyChanged(nameof(HasSelectedBook));
                    OnPropertyChanged(nameof(CurrentBookTitle));
                    // Reset panel state on book switch
                    IsBookInfoVisible = false;
                    IsStatsPanelVisible = false;
                    IsReportsPanelVisible = false;
                }
            };
        }

        public WriterState State => _state;
        public ObservableCollection<Book> Books => _state.Books;
        public bool HasSelectedBook => _state.HasSelectedBook;
        public string CurrentBookTitle => _state.CurrentBookTitle;

        [ObservableProperty]
        private bool isSidebarVisible = true;

        [ObservableProperty]
        private bool isRightPanelVisible;

        [ObservableProperty]
        private bool isBottomPanelVisible = true;

        [ObservableProperty]
        private bool isBookInfoVisible;

        [ObservableProperty]
        private bool isStatsPanelVisible;

        [ObservableProperty]
        private bool isReportsPanelVisible;

        [ObservableProperty]
        private string activeBottomTab = "Analysis";

        // Whether the scene editor WebView should be visible
        public bool IsSceneEditorVisible => _state.HasSelectedScene
            && _state.SelectedCharacter is null
            && _state.SelectedLocation is null
            && _state.SelectedItem is null;

        // Whether the element editor should be visible in the right panel
        public bool IsElementEditorVisible =>
            _state.SelectedCharacter is not null
            || _state.SelectedLocation is not null
            || _state.SelectedItem is not null;

        public async Task InitializeAsync() => await _state.InitializeAsync();

        // ── Book commands ──────────────────────────────────────────────────

        [RelayCommand]
        private async Task AddBook()
        {
            var book = new Book { Title = "New Book" };
            _state.Books.Add(book);
            _state.SelectedBook = book;
            await _state.TriggerSaveAsync();
        }

        [RelayCommand]
        private async Task DeleteBook(Book book)
        {
            if (_state.SelectedBook == book)
                _state.SelectedBook = null;
            _state.Books.Remove(book);

            var imageDir = Path.Combine(FileSystem.AppDataDirectory, "images", book.Id);
            if (Directory.Exists(imageDir))
            {
                try { Directory.Delete(imageDir, recursive: true); }
                catch { /* best-effort cleanup */ }
            }

            await _state.TriggerSaveAsync();
        }

        [RelayCommand]
        private async Task RenameBook(Book book)
        {
            var name = await Shell.Current.DisplayPromptAsync(
                "Rename Book", "Enter a new title:", initialValue: book.Title);
            if (name is not null)
            {
                book.Title = name;
                var idx = _state.Books.IndexOf(book);
                if (idx >= 0) { _state.Books.RemoveAt(idx); _state.Books.Insert(idx, book); }
                OnPropertyChanged(nameof(CurrentBookTitle));
                await _state.TriggerSaveAsync();
            }
        }

        [RelayCommand]
        private async Task SwitchBook()
        {
            if (_state.Books.Count == 0) return;
            var titles = _state.Books.Select(b => b.Title).ToArray();
            var result = await Shell.Current.DisplayActionSheetAsync(
                "Switch Book", "Cancel", null, titles);
            if (result is not null && result != "Cancel")
            {
                var book = _state.Books.FirstOrDefault(b => b.Title == result);
                if (book is not null)
                    _state.SelectedBook = book;
            }
        }

        [RelayCommand]
        private async Task ManageBooks()
        {
            var actions = new List<string> { "Add New Book" };
            if (_state.SelectedBook is not null)
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
                case "Rename Current Book" when _state.SelectedBook is not null:
                    await RenameBook(_state.SelectedBook);
                    break;
                case "Delete Current Book" when _state.SelectedBook is not null:
                    await DeleteBook(_state.SelectedBook);
                    break;
            }
        }

        [RelayCommand]
        private void ToggleSidebar() => IsSidebarVisible = !IsSidebarVisible;

        [RelayCommand]
        private void ToggleRightPanel() => IsRightPanelVisible = !IsRightPanelVisible;

        [RelayCommand]
        private void ToggleBottomPanel() => IsBottomPanelVisible = !IsBottomPanelVisible;

        [RelayCommand]
        private void ShowBookInfo()
        {
            if (_state.SelectedBook is null) return;
            IsBookInfoVisible = true;
            IsRightPanelVisible = true;
        }

        [RelayCommand]
        private void CloseBookInfo() => IsBookInfoVisible = false;

        [RelayCommand]
        private void ShowStatistics()
        {
            if (_state.SelectedBook is null) return;
            IsStatsPanelVisible = true;
            ActiveBottomTab = "Frequency";
            IsBottomPanelVisible = true;
        }

        [RelayCommand]
        private void CloseStatistics() => IsStatsPanelVisible = false;

        [RelayCommand]
        private void ShowReports()
        {
            if (_state.SelectedBook is null) return;
            IsReportsPanelVisible = true;
            WeakReferenceMessenger.Default.Send(new RequestReportRefreshMessage("Entities"));
        }

        [RelayCommand]
        private void CloseReports() => IsReportsPanelVisible = false;

        [RelayCommand]
        private void SetBottomTab(string tab) => ActiveBottomTab = tab;

        public void SaveBookInfo() => _state.TriggerSave();
    }
}
