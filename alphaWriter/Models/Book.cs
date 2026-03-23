using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace alphaWriter.Models
{
    public class Book : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        private void Notify(string name) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        public string Id { get; set; } = Guid.NewGuid().ToString();

        private string _title = string.Empty;
        public string Title
        {
            get => _title;
            set
            {
                if (_title == value) return;
                _title = value;
                Notify(nameof(Title));
            }
        }

        private string _author = string.Empty;
        public string Author
        {
            get => _author;
            set
            {
                if (_author == value) return;
                _author = value;
                Notify(nameof(Author));
            }
        }

        private string _authorBio = string.Empty;
        public string AuthorBio
        {
            get => _authorBio;
            set
            {
                if (_authorBio == value) return;
                _authorBio = value;
                Notify(nameof(AuthorBio));
            }
        }

        private string _description = string.Empty;
        public string Description
        {
            get => _description;
            set
            {
                if (_description == value) return;
                _description = value;
                Notify(nameof(Description));
            }
        }

        private int _wordTarget;
        public int WordTarget
        {
            get => _wordTarget;
            set
            {
                if (_wordTarget == value) return;
                _wordTarget = value;
                Notify(nameof(WordTarget));
                Notify(nameof(WordCountProgress));
                Notify(nameof(WordCountSummary));
                Notify(nameof(HasWordTarget));
            }
        }

        private ObservableCollection<Chapter> _chapters = new();
        public ObservableCollection<Chapter> Chapters
        {
            get => _chapters;
            set
            {
                if (_chapters == value) return;
                UnsubscribeChapters(_chapters);
                _chapters = value;
                SubscribeChapters(_chapters);
                Notify(nameof(Chapters));
                Notify(nameof(WordCount));
                Notify(nameof(WordCountProgress));
                Notify(nameof(WordCountSummary));
            }
        }

        public ObservableCollection<Character> Characters { get; set; } = new();
        public ObservableCollection<Location> Locations { get; set; } = new();
        public ObservableCollection<Item> Items { get; set; } = new();

        /// <summary>
        /// Keyed by "yyyy-MM-dd". Value is the book's total word count at the end of that day's session.
        /// Used to compute the daily words-written delta in Writing Statistics.
        /// </summary>
        public Dictionary<string, int> DailyWordSnapshots { get; set; } = new();

        public int WordCount => Chapters.Sum(c => c.WordCount);

        public bool HasWordTarget => WordTarget > 0;

        public double WordCountProgress =>
            WordTarget > 0 ? Math.Clamp((double)WordCount / WordTarget, 0.0, 1.0) : 0.0;

        public string WordCountSummary =>
            WordTarget > 0
                ? $"{WordCount:N0} / {WordTarget:N0} words ({WordCountProgress:P0})"
                : $"{WordCount:N0} words";

        public Book()
        {
            SubscribeChapters(_chapters);
        }

        private void SubscribeChapters(ObservableCollection<Chapter> chapters)
        {
            chapters.CollectionChanged += OnChaptersChanged;
            foreach (var c in chapters) c.PropertyChanged += OnChapterPropertyChanged;
        }

        private void UnsubscribeChapters(ObservableCollection<Chapter> chapters)
        {
            chapters.CollectionChanged -= OnChaptersChanged;
            foreach (var c in chapters) c.PropertyChanged -= OnChapterPropertyChanged;
        }

        private void OnChaptersChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems != null)
                foreach (Chapter c in e.OldItems) c.PropertyChanged -= OnChapterPropertyChanged;
            if (e.NewItems != null)
                foreach (Chapter c in e.NewItems) c.PropertyChanged += OnChapterPropertyChanged;
            Notify(nameof(WordCount));
            Notify(nameof(WordCountProgress));
            Notify(nameof(WordCountSummary));
        }

        private void OnChapterPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(Chapter.WordCount))
            {
                Notify(nameof(WordCount));
                Notify(nameof(WordCountProgress));
                Notify(nameof(WordCountSummary));
            }
        }
    }
}
