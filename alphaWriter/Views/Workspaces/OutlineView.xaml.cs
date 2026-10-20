using alphaWriter.ViewModels;
using static alphaWriter.Views.DragDropHelpers;

namespace alphaWriter.Views.Workspaces
{
    public partial class OutlineView : ContentView
    {
        public OutlineView()
        {
            InitializeComponent();
        }

        private WriterViewModel? ViewModel => BindingContext as WriterViewModel;

        // ── Drag-to-reorder ───────────────────────────────────────────────────
        // Chapters drop onto chapter rows (reorder). Scenes drop onto scene rows
        // (reorder, possibly into another chapter) or onto a chapter row (append there).

        private OutlineRow? _dragging;
        private bool _dropAfter;

        private void OnRowDragStarting(object? sender, DragStartingEventArgs e)
        {
            if (sender is Element { BindingContext: OutlineRow row })
                _dragging = row;
        }

        private void OnRowDragCompleted(object? sender, DropCompletedEventArgs e)
        {
            _dragging = null;
            _dropAfter = false;
        }

        private void OnRowDragOver(object? sender, DragEventArgs e)
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
            SuppressDragCallout(e);
            if (sender is not Element { BindingContext: OutlineRow target } || _dragging is null) return;

            switch (_dragging, target)
            {
                case (ChapterOutlineRow, ChapterOutlineRow):
                case (SceneOutlineRow, SceneOutlineRow):
                    _dropAfter = IsCursorInBottomHalf(sender, e);
                    ShowDropIndicator(sender, atBottom: _dropAfter);
                    break;
                case (SceneOutlineRow, ChapterOutlineRow):
                    // Appends to that chapter — show the line under the chapter row
                    _dropAfter = true;
                    ShowDropIndicator(sender, atBottom: true);
                    break;
                default:
                    e.AcceptedOperation = DataPackageOperation.None;
                    break;
            }
        }

        private void OnRowDragLeave(object? sender, DragEventArgs e) => HideDropIndicator(sender);

        private void OnRowDrop(object? sender, DropEventArgs e)
        {
            HideDropIndicator(sender);
            var vm = ViewModel;
            var book = vm?.SelectedBook;
            if (vm is null || book is null || _dragging is null) return;
            if (sender is not Element { BindingContext: OutlineRow target } || target == _dragging) return;

            switch (_dragging, target)
            {
                case (ChapterOutlineRow from, ChapterOutlineRow to):
                {
                    var idx = book.Chapters.IndexOf(to.Chapter) + (_dropAfter ? 1 : 0);
                    vm.MoveChapter(from.Chapter, idx);
                    break;
                }
                case (SceneOutlineRow from, SceneOutlineRow to):
                {
                    var idx = to.Chapter.Scenes.IndexOf(to.Scene) + (_dropAfter ? 1 : 0);
                    vm.MoveSceneToChapter(from.Scene, to.Chapter, idx);
                    break;
                }
                case (SceneOutlineRow from, ChapterOutlineRow to):
                    vm.MoveSceneToChapter(from.Scene, to.Chapter, to.Chapter.Scenes.Count);
                    break;
            }

            _dragging = null;
            _dropAfter = false;
        }
    }

    /// <summary>Chooses the chapter or scene row template for the outline.</summary>
    public class OutlineRowTemplateSelector : DataTemplateSelector
    {
        public DataTemplate? ChapterTemplate { get; set; }
        public DataTemplate? SceneTemplate { get; set; }

        protected override DataTemplate OnSelectTemplate(object item, BindableObject container) =>
            (item is ChapterOutlineRow ? ChapterTemplate : SceneTemplate)!;
    }
}
