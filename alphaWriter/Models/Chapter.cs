using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace alphaWriter.Models
{
    public class Chapter : INotifyPropertyChanged
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

        private ObservableCollection<Scene> _scenes = new();
        public ObservableCollection<Scene> Scenes
        {
            get => _scenes;
            set
            {
                if (_scenes == value) return;
                UnsubscribeScenes(_scenes);
                _scenes = value;
                SubscribeScenes(_scenes);
                Notify(nameof(Scenes));
                Notify(nameof(WordCount));
            }
        }

        public int WordCount => Scenes.Sum(s => s.WordCount);

        public Chapter()
        {
            SubscribeScenes(_scenes);
        }

        private void SubscribeScenes(ObservableCollection<Scene> scenes)
        {
            scenes.CollectionChanged += OnScenesChanged;
            foreach (var s in scenes) s.PropertyChanged += OnScenePropertyChanged;
        }

        private void UnsubscribeScenes(ObservableCollection<Scene> scenes)
        {
            scenes.CollectionChanged -= OnScenesChanged;
            foreach (var s in scenes) s.PropertyChanged -= OnScenePropertyChanged;
        }

        private void OnScenesChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems != null)
                foreach (Scene s in e.OldItems) s.PropertyChanged -= OnScenePropertyChanged;
            if (e.NewItems != null)
                foreach (Scene s in e.NewItems) s.PropertyChanged += OnScenePropertyChanged;
            Notify(nameof(WordCount));
        }

        private void OnScenePropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(Scene.WordCount))
                Notify(nameof(WordCount));
        }
    }
}
