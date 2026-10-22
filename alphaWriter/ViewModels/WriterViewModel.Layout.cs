using alphaWriter.Models;
using alphaWriter.Services;

namespace alphaWriter.ViewModels
{
    /// <summary>
    /// Remembered layout and keyboard shortcuts: the app reopens where the author left
    /// off — same workspace, same scene, same chapters folded away.
    /// </summary>
    public partial class WriterViewModel
    {
        /// <summary>Set by snapshot runs to request an explicit start-up state (Debug builds).</summary>
        public const string UiStateVariable = "ALPHAWRITER_UI_STATE";

        /// <summary>Where layout is remembered. Tests swap in a <see cref="MemoryUiStateStore"/>.</summary>
        public IUiStateStore UiStore { get; set; } = new PreferencesUiStateStore();

        /// <summary>
        /// False when a start-up state was requested explicitly (snapshot runs): restoring
        /// remembered layout on top of it would make captures depend on earlier runs.
        /// </summary>
        public static bool RemembersLayout =>
            string.IsNullOrEmpty(Environment.GetEnvironmentVariable(UiStateVariable));

        private bool _restoringLayout;

        private static string LastSceneKey(Book book) => $"lastScene.{book.Id}";
        private static string CollapsedKey(Book book) => $"collapsed.{book.Id}";

        /// <summary>Reopens the last workspace (called once, after the last book is restored).</summary>
        private void RestoreWorkspace()
        {
            if (!RemembersLayout || SelectedBook is null) return;
            if (Enum.TryParse<Workspace>(UiStore.Get("workspace"), out var ws) && ws != ActiveWorkspace)
            {
                _restoringLayout = true;
                try { ActiveWorkspace = ws; }
                finally { _restoringLayout = false; }
            }
        }

        /// <summary>Reopens the scene the author last had open in this book.</summary>
        private void RestoreLastScene(Book book)
        {
            if (!RemembersLayout) return;
            var id = UiStore.Get(LastSceneKey(book));
            if (string.IsNullOrEmpty(id)) return;

            foreach (var chapter in book.Chapters)
            {
                var scene = chapter.Scenes.FirstOrDefault(s => s.Id == id);
                if (scene is null) continue;
                _restoringLayout = true;
                try
                {
                    SelectedChapter = chapter;
                    SelectedScene = scene;
                }
                finally { _restoringLayout = false; }
                return;
            }
        }

        private void RememberWorkspace(Workspace workspace)
        {
            if (!_restoringLayout && RemembersLayout) UiStore.Set("workspace", workspace.ToString());
        }

        private void RememberScene(Scene? scene)
        {
            if (scene is null || SelectedBook is null || !RemembersLayout) return;
            UiStore.Set(LastSceneKey(SelectedBook), scene.Id);
        }

        private void RememberCollapsedChapters()
        {
            if (_outlineBook is null || !RemembersLayout) return;
            UiStore.Set(CollapsedKey(_outlineBook), string.Join(",", _collapsedChapterIds));
        }

        private void LoadCollapsedChapters(Book? book)
        {
            if (book is null || !RemembersLayout) return;
            foreach (var id in UiStore.Get(CollapsedKey(book)).Split(',', StringSplitOptions.RemoveEmptyEntries))
                _collapsedChapterIds.Add(id);
        }

        // ── Keyboard shortcuts ────────────────────────────────────────────────

        /// <summary>
        /// Ctrl+1…5 switch workspaces; Ctrl+\ shows or hides the sidebar; F8 / Shift+F8 step through notes. Called from the
        /// window's keyboard accelerators and, while the editor has focus, from editor.html.
        /// </summary>
        public bool HandleShortcut(string key)
        {
            switch (key)
            {
                case "1": ActiveWorkspace = Workspace.Write; return true;
                case "2": ActiveWorkspace = Workspace.StoryBible; return true;
                case "3": ActiveWorkspace = Workspace.Revise; return true;
                case "4": ActiveWorkspace = Workspace.Insights; return true;
                case "5": if (SelectedBook is not null) ActiveWorkspace = Workspace.Book; return true;
                case "sidebar": IsSidebarVisible = !IsSidebarVisible; return true;
                case "next": StepNote(+1); return true;
                case "prev": StepNote(-1); return true;
                default: return false;
            }
        }
    }
}
