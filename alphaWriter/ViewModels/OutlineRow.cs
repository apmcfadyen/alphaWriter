using alphaWriter.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace alphaWriter.ViewModels
{
    /// <summary>
    /// One row in the Write workspace's outline. MAUI has no tree control, so the
    /// chapter → scene hierarchy is flattened into chapter rows followed by their
    /// scene rows (when the chapter is expanded).
    /// </summary>
    public abstract partial class OutlineRow : ObservableObject
    {
        [ObservableProperty]
        private bool isSelected;
    }

    public sealed partial class ChapterOutlineRow : OutlineRow
    {
        public ChapterOutlineRow(Chapter chapter, int number, bool isExpanded)
        {
            Chapter = chapter;
            Number = number;
            this.isExpanded = isExpanded;
        }

        public Chapter Chapter { get; }

        /// <summary>1-based position in the book.</summary>
        public int Number { get; }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ExpandGlyph))]
        [NotifyPropertyChangedFor(nameof(ExpandHint))]
        private bool isExpanded;

        public string ExpandGlyph => IsExpanded ? "▾" : "▸";

        public bool HasScenes => Chapter.Scenes.Count > 0;

        public string ExpandHint => IsExpanded ? "Hide scenes" : "Show scenes";

        public string SceneCountText => Chapter.Scenes.Count switch
        {
            0 => "no scenes",
            1 => "1 scene",
            var n => $"{n} scenes"
        };
    }

    public sealed class SceneOutlineRow : OutlineRow
    {
        public SceneOutlineRow(Scene scene, Chapter chapter)
        {
            Scene = scene;
            Chapter = chapter;
        }

        public Scene Scene { get; }
        public Chapter Chapter { get; }
    }
}
