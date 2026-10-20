using alphaWriter.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Collections.Specialized;

namespace alphaWriter.ViewModels
{
    /// <summary>
    /// Workspace navigation, the Write outline, and status-bar state.
    /// The active workspace is the single source of truth for what the main area shows —
    /// it replaces the old precedence of IsBookInfoVisible / IsStatsPanelVisible / … flags.
    /// </summary>
    public partial class WriterViewModel
    {
        // ── Workspaces ────────────────────────────────────────────────────────

        [ObservableProperty]
        private Workspace activeWorkspace = Workspace.Write;

        /// <summary>Within Insights: a WebView report (true) or a statistics view (false).</summary>
        [ObservableProperty]
        private bool insightsShowsReport;

        private string _insightsStatsTab = "Frequency";

        public bool IsWriteWorkspace => ActiveWorkspace == Workspace.Write;
        public bool IsStoryBibleWorkspace => ActiveWorkspace == Workspace.StoryBible;
        public bool IsReviseWorkspace => ActiveWorkspace == Workspace.Revise;
        public bool IsInsightsWorkspace => ActiveWorkspace == Workspace.Insights;
        public bool IsBookWorkspace => ActiveWorkspace == Workspace.Book;

        /// <summary>Workspaces whose main area is the scene editor (one shared editor instance).</summary>
        public bool IsEditorHostVisible => ActiveWorkspace is Workspace.Write or Workspace.Revise;

        // Main-area panels, derived from the workspace
        public bool IsSceneEditorVisible => IsEditorHostVisible;
        public bool IsBookInfoVisible => IsBookWorkspace;
        public bool IsStatsPanelVisible => IsInsightsWorkspace && !InsightsShowsReport;
        public bool IsReportsPanelVisible => IsInsightsWorkspace && InsightsShowsReport;
        public bool IsElementEditorVisible => IsStoryBibleWorkspace && HasSelectedElement;
        public bool IsStoryBibleEmpty => IsStoryBibleWorkspace && !HasSelectedElement;
        private bool HasSelectedElement =>
            SelectedCharacter is not null || SelectedLocation is not null || SelectedItem is not null;

        /// <summary>The sidebar column shows for workspaces that have a navigator.</summary>
        public bool IsSidebarHostVisible => IsSidebarVisible && !IsBookWorkspace;

        /// <summary>Scene inspector: Write only, with a scene open, when the author wants it.</summary>
        public bool IsInspectorVisible => IsWriteWorkspace && HasSelectedScene && IsMetadataPanelVisible;

        public string StatsPanelTitle => "Insights — writing statistics";

        public string ReportPanelTitle => ActiveReportType == "NLP" ? "Insights — analysis report" : "Insights — story map";

        /// <summary>Key of the selected Insights entry, for highlighting the sidebar list.</summary>
        public string ActiveInsight => InsightsShowsReport ? $"report:{ActiveReportType}" : ActiveStatsTab;

        [RelayCommand]
        private void SetWorkspace(string workspace)
        {
            if (Enum.TryParse<Workspace>(workspace, out var ws))
                ActiveWorkspace = ws;
        }

        partial void OnActiveWorkspaceChanging(Workspace oldValue, Workspace newValue)
        {
            if (oldValue == Workspace.Insights)
                _insightsStatsTab = ActiveStatsTab;
        }

        partial void OnActiveWorkspaceChanged(Workspace value)
        {
            RememberWorkspace(value);
            switch (value)
            {
                case Workspace.Revise:
                    RebuildReviseGroups();
                    break;
                case Workspace.Insights:
                    ActiveStatsTab = _insightsStatsTab;
                    if (SelectedBook is not null) ComputeStatistics();
                    break;
                case Workspace.StoryBible:
                    if (SidebarMode == SidebarMode.Chapters) SidebarMode = SidebarMode.Characters;
                    break;
            }

            NotifyWorkspaceDerived();
            RefreshEditorEmptyState();
            if (IsReportsPanelVisible) ReportsPanelShown?.Invoke();
        }

        partial void OnInsightsShowsReportChanged(bool value)
        {
            NotifyWorkspaceDerived();
            if (IsReportsPanelVisible) ReportsPanelShown?.Invoke();
        }

        partial void OnIsSidebarVisibleChanged(bool value) => OnPropertyChanged(nameof(IsSidebarHostVisible));

        partial void OnIsMetadataPanelVisibleChanged(bool value) => OnPropertyChanged(nameof(IsInspectorVisible));

        partial void OnActiveReportTypeChanged(string value)
        {
            OnPropertyChanged(nameof(ReportPanelTitle));
            OnPropertyChanged(nameof(ActiveInsight));
        }

        private void NotifyWorkspaceDerived()
        {
            foreach (var name in new[]
            {
                nameof(IsWriteWorkspace), nameof(IsStoryBibleWorkspace), nameof(IsReviseWorkspace),
                nameof(IsInsightsWorkspace), nameof(IsBookWorkspace), nameof(IsEditorHostVisible),
                nameof(IsSceneEditorVisible), nameof(IsBookInfoVisible), nameof(IsStatsPanelVisible),
                nameof(IsReportsPanelVisible), nameof(IsElementEditorVisible), nameof(IsStoryBibleEmpty),
                nameof(IsSidebarHostVisible), nameof(IsInspectorVisible), nameof(StatsPanelTitle),
                nameof(ActiveInsight)
            })
                OnPropertyChanged(name);
        }

        private void NotifyElementSelectionDerived()
        {
            OnPropertyChanged(nameof(IsElementEditorVisible));
            OnPropertyChanged(nameof(IsStoryBibleEmpty));
        }

        /// <summary>Opens a statistics view or report in Insights (from the Insights sidebar).</summary>
        [RelayCommand]
        private void OpenInsight(string key)
        {
            if (SelectedBook is null) return;
            if (key.StartsWith("report:", StringComparison.Ordinal))
            {
                var type = key["report:".Length..];
                if (type == "NLP" && !HasAnalysisResults) return;
                ActiveReportType = type;
                InsightsShowsReport = true;
            }
            else
            {
                InsightsShowsReport = false;
                _insightsStatsTab = key;   // entering Insights restores this tab
                ActiveStatsTab = key;
            }
            ActiveWorkspace = Workspace.Insights;
            OnPropertyChanged(nameof(ActiveInsight));
        }

        // ── Outline (Write workspace sidebar) ─────────────────────────────────

        public ObservableCollection<OutlineRow> OutlineRows { get; } = [];

        private readonly HashSet<string> _collapsedChapterIds = [];
        private Book? _outlineBook;
        private readonly List<Chapter> _outlineChapters = [];

        /// <summary>Re-subscribes to the book's chapters/scenes and rebuilds the outline.</summary>
        private void AttachOutline(Book? book)
        {
            if (_outlineBook is not null)
                _outlineBook.Chapters.CollectionChanged -= OnOutlineStructureChanged;
            foreach (var c in _outlineChapters)
                c.Scenes.CollectionChanged -= OnOutlineStructureChanged;
            _outlineChapters.Clear();
            _collapsedChapterIds.Clear();
            LoadCollapsedChapters(book);

            _outlineBook = book;
            if (book is not null)
                book.Chapters.CollectionChanged += OnOutlineStructureChanged;

            RebuildOutline();
        }

        private void OnOutlineStructureChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            RebuildOutline();
            RefreshEditorEmptyState();
        }

        /// <summary>Flattens chapters and (expanded) scenes into <see cref="OutlineRows"/>.</summary>
        internal void RebuildOutline()
        {
            foreach (var c in _outlineChapters)
                c.Scenes.CollectionChanged -= OnOutlineStructureChanged;
            _outlineChapters.Clear();

            OutlineRows.Clear();
            if (_outlineBook is null) return;

            int number = 1;
            foreach (var chapter in _outlineBook.Chapters)
            {
                chapter.Scenes.CollectionChanged += OnOutlineStructureChanged;
                _outlineChapters.Add(chapter);

                bool expanded = !_collapsedChapterIds.Contains(chapter.Id);
                OutlineRows.Add(new ChapterOutlineRow(chapter, number++, expanded));
                if (expanded)
                    foreach (var scene in chapter.Scenes)
                        OutlineRows.Add(new SceneOutlineRow(scene, chapter));
            }
            SyncOutlineSelection();
        }

        /// <summary>Highlights the open scene, or the open chapter when no scene is open.</summary>
        private void SyncOutlineSelection()
        {
            foreach (var row in OutlineRows)
            {
                row.IsSelected = row switch
                {
                    SceneOutlineRow s => s.Scene == SelectedScene,
                    ChapterOutlineRow c => SelectedScene is null && c.Chapter == SelectedChapter,
                    _ => false
                };
            }
        }

        [RelayCommand]
        private void ToggleChapterExpanded(ChapterOutlineRow? row)
        {
            if (row is null) return;
            int index = OutlineRows.IndexOf(row);
            if (index < 0) return;

            row.IsExpanded = !row.IsExpanded;
            if (row.IsExpanded)
            {
                _collapsedChapterIds.Remove(row.Chapter.Id);
                int insertAt = index + 1;
                foreach (var scene in row.Chapter.Scenes)
                    OutlineRows.Insert(insertAt++, new SceneOutlineRow(scene, row.Chapter) { IsSelected = scene == SelectedScene });
            }
            else
            {
                _collapsedChapterIds.Add(row.Chapter.Id);
                while (index + 1 < OutlineRows.Count && OutlineRows[index + 1] is SceneOutlineRow)
                    OutlineRows.RemoveAt(index + 1);
            }
            RememberCollapsedChapters();
        }

        /// <summary>Selecting a chapter shows its scenes; selecting a scene opens it in the editor.</summary>
        [RelayCommand]
        private void OpenOutlineRow(OutlineRow? row)
        {
            switch (row)
            {
                case ChapterOutlineRow c:
                    if (SelectedChapter != c.Chapter) SelectedChapter = c.Chapter;
                    else if (SelectedScene is not null) SelectedScene = null;
                    if (!c.IsExpanded) ToggleChapterExpanded(c);
                    break;
                case SceneOutlineRow s:
                    if (SelectedChapter != s.Chapter) SelectedChapter = s.Chapter;
                    SelectedScene = s.Scene;
                    break;
            }
            ActiveWorkspace = Workspace.Write;
        }

        [RelayCommand]
        private async Task AddSceneToChapter(Chapter? chapter)
        {
            if (chapter is null) return;
            _collapsedChapterIds.Remove(chapter.Id);
            if (SelectedChapter != chapter) SelectedChapter = chapter;
            await AddScene();
        }

        /// <summary>Moves a scene to <paramref name="target"/> at <paramref name="desiredIndex"/> (may change chapters).</summary>
        public void MoveSceneToChapter(Scene scene, Chapter target, int desiredIndex)
        {
            var source = FindChapterOf(scene);
            if (source is null) return;

            if (source == target)
            {
                var scenes = source.Scenes;
                var oldIndex = scenes.IndexOf(scene);
                var moveIndex = Math.Clamp(desiredIndex > oldIndex ? desiredIndex - 1 : desiredIndex, 0, scenes.Count - 1);
                if (oldIndex == moveIndex) return;
                scenes.Move(oldIndex, moveIndex);
            }
            else
            {
                bool wasOpen = SelectedScene == scene;
                source.Scenes.Remove(scene);
                target.Scenes.Insert(Math.Clamp(desiredIndex, 0, target.Scenes.Count), scene);
                _collapsedChapterIds.Remove(target.Id);
                if (wasOpen)
                {
                    SelectedChapter = target;
                    SelectedScene = scene;
                }
                RebuildOutline();
            }
            TriggerSaveAsync().ConfigureAwait(false);
        }

        internal Chapter? FindChapterOf(Scene scene) =>
            SelectedChapter is { } open && open.Scenes.Contains(scene)
                ? open
                : SelectedBook?.Chapters.FirstOrDefault(c => c.Scenes.Contains(scene));

        // ── Status bar ────────────────────────────────────────────────────────

        [ObservableProperty]
        private string saveStateText = "All changes saved";

        private void SetSaveState(string text)
        {
            try
            {
                if (MainThread.IsMainThread) SaveStateText = text;
                else MainThread.BeginInvokeOnMainThread(() => SaveStateText = text);
            }
            catch
            {
                // No UI dispatcher (unit tests) — set directly.
                SaveStateText = text;
            }
        }
    }
}
