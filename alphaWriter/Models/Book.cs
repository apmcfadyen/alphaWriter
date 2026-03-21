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
            }
        }

        public ObservableCollection<Character> Characters { get; set; } = new();
        public ObservableCollection<Location> Locations { get; set; } = new();
        public ObservableCollection<Item> Items { get; set; } = new();

        public int WordCount => Chapters.Sum(c => c.WordCount);

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
        }

        private void OnChapterPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(Chapter.WordCount))
                Notify(nameof(WordCount));
        }
    }
}
