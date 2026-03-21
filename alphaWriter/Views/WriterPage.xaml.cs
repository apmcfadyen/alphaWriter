using alphaWriter.Models;
using alphaWriter.ViewModels;

namespace alphaWriter.Views
{
    public partial class WriterPage : ContentPage
    {
        private WriterViewModel _viewModel;
        private bool _editorReady;
        private Scene? _previousScene;

        public WriterPage(WriterViewModel viewModel)
        {
            InitializeComponent();
            _viewModel = viewModel;
            BindingContext = viewModel;

            _viewModel.SelectedSceneChanged += OnSelectedSceneChanged;
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();
            await _viewModel.InitializeAsync();
            AttachGlobalDragSuppressor();
        }

        private bool _dragSuppressorAttached;

        // ── WebView events ────────────────────────────────────────────────────

        private void OnEditorNavigated(object? sender, WebNavigatedEventArgs e)
        {
            if (e.Result == WebNavigationResult.Success &&
                e.Url.EndsWith("editor.html", StringComparison.OrdinalIgnoreCase))
            {
                _editorReady = true;
                // Load content if a scene is already selected
                if (_viewModel.SelectedScene is not null)
                    _ = LoadSceneAsync(_viewModel.SelectedScene);
            }
        }

        private async void OnEditorNavigating(object? sender, WebNavigatingEventArgs e)
        {
            if (e.Url.StartsWith("alphawriter://contentChanged", StringComparison.OrdinalIgnoreCase))
            {
                e.Cancel = true;
                // getContent() returns _rawContent — a pre-captured plain string.
                // EvaluateJavaScriptAsync JSON-encodes the return value, so deserialize it.
                var raw = await EditorWebView.EvaluateJavaScriptAsync("getContent()");
                string content;
                try { content = System.Text.Json.JsonSerializer.Deserialize<string>(raw) ?? string.Empty; }
                catch { content = raw ?? string.Empty; }
                var wcRaw = await EditorWebView.EvaluateJavaScriptAsync("getWordCount()");
                int? jsWordCount = null;
                if (int.TryParse(wcRaw, out var wc)) jsWordCount = wc;
                _viewModel.SaveContent(content, jsWordCount);
            }
        }

        // ── Scene change: update WebView content ──────────────────────────────

        private async void OnSelectedSceneChanged(Scene? scene)
        {
            if (!_editorReady) return;

            // Flush the outgoing scene's content before loading the new one
            if (_previousScene is not null)
                await FlushEditorContentAsync(_previousScene);

            _previousScene = scene;

            if (scene is null)
            {
                MainThread.BeginInvokeOnMainThread(async () =>
                    await EditorWebView.EvaluateJavaScriptAsync("setContent('')"));
                return;
            }

            await LoadSceneAsync(scene);
        }

        private async Task FlushEditorContentAsync(Scene scene)
        {
            await MainThread.InvokeOnMainThreadAsync(async () =>
                await EditorWebView.EvaluateJavaScriptAsync("flushContent()"));

            var raw = await MainThread.InvokeOnMainThreadAsync(async () =>
                await EditorWebView.EvaluateJavaScriptAsync("getContent()"));
            string content;
            try { content = System.Text.Json.JsonSerializer.Deserialize<string>(raw) ?? string.Empty; }
            catch { content = raw ?? string.Empty; }

            var wcRaw = await MainThread.InvokeOnMainThreadAsync(async () =>
                await EditorWebView.EvaluateJavaScriptAsync("getWordCount()"));
            int? jsWordCount = null;
            if (int.TryParse(wcRaw, out var wc)) jsWordCount = wc;

            scene.Content = content;
            if (jsWordCount.HasValue)
                scene.WordCount = jsWordCount.Value;

            _viewModel.TriggerSave();
        }

        private async Task LoadSceneAsync(Scene scene)
        {
            if (!_editorReady) return;

            var content = scene.Content ?? string.Empty;
            var escaped = System.Text.Json.JsonSerializer.Serialize(content);
            await MainThread.InvokeOnMainThreadAsync(async () =>
                await EditorWebView.EvaluateJavaScriptAsync($"setContent({escaped})"));
        }

        // ── Global drag-glyph suppression (Windows) ──────────────────────────
        // Called once from OnAppearing (page is guaranteed to be in the visual tree).
        //
        // Two-pronged:
        //  1. Attach OnNativeDragOver to the WinUI window's content root with
        //     handledEventsToo:true — fires for every drag over any normal WinUI
        //     element, even those already handled by child DropGestureRecognizers.
        //  2. Set AllowDrop=false on the native WebView2 — WebView2 runs in its own
        //     Win32 HWND and doesn't propagate WinUI drag events up the tree, but it
        //     signals "Copy" to the OS by default; disabling AllowDrop stops that.
        private void AttachGlobalDragSuppressor()
        {
            if (_dragSuppressorAttached) return;
            _dragSuppressorAttached = true;
#if WINDOWS
            // 1. Window content root — covers all non-WebView areas
            if (Window?.Handler?.PlatformView is Microsoft.UI.Xaml.Window nativeWindow &&
                nativeWindow.Content is Microsoft.UI.Xaml.FrameworkElement windowContent)
            {
                windowContent.AllowDrop = true;
                windowContent.AddHandler(
                    Microsoft.UI.Xaml.UIElement.DragOverEvent,
                    new Microsoft.UI.Xaml.DragEventHandler(OnNativeDragOver),
                    handledEventsToo: true);
            }

            // 2. WebView2 — prevent it accepting drops so the OS shows no glyph over it
            if (EditorWebView.Handler?.PlatformView is Microsoft.UI.Xaml.Controls.WebView2 wv2)
                wv2.AllowDrop = false;
#endif
        }

#if WINDOWS
        private static void OnNativeDragOver(object sender, Microsoft.UI.Xaml.DragEventArgs e)
        {
            if (e.DragUIOverride is { } ui)
            {
                ui.IsGlyphVisible   = false;
                ui.IsCaptionVisible = false;
                ui.IsContentVisible = false;
            }
        }
#endif

        // ── Resizable splitter ──────────────────────────────────────────────────

        private double _previousPanY;

        private void OnSplitterPanUpdated(object? sender, PanUpdatedEventArgs e)
        {
            switch (e.StatusType)
            {
                case GestureStatus.Started:
                    _previousPanY = 0;
                    break;

                case GestureStatus.Running:
                    var delta = e.TotalY - _previousPanY;
                    _previousPanY = e.TotalY;
                    AdjustSplitRows(delta);
                    break;
            }
        }

        private void AdjustSplitRows(double deltaY)
        {
            var grid = EditorScenesGrid;
            var totalHeight = grid.Height;
            if (totalHeight <= 0) return;

            var editorRow = grid.RowDefinitions[0];
            var scenesRow = grid.RowDefinitions[2];

            var totalStar = editorRow.Height.Value + scenesRow.Height.Value;
            var usableHeight = totalHeight - 6.0;

            var editorPx = (editorRow.Height.Value / totalStar) * usableHeight + deltaY;
            var scenesPx = (scenesRow.Height.Value / totalStar) * usableHeight - deltaY;

            const double minEditor = 120;
            const double minScenes = 80;
            editorPx = Math.Max(editorPx, minEditor);
            scenesPx = Math.Max(scenesPx, minScenes);

            var newTotal = editorPx + scenesPx;
            editorRow.Height = new GridLength(editorPx / newTotal, GridUnitType.Star);
            scenesRow.Height = new GridLength(scenesPx / newTotal, GridUnitType.Star);
        }

        // ── Drag-and-drop reordering ──────────────────────────────────────────

        private Chapter? _draggingChapter;
        private Scene?   _draggingScene;

        private bool _chapterDropAfter;
        private bool _sceneDropAfter;

        // Returns true when the drag cursor is in the lower half of the hovered item.
        private static bool IsCursorInBottomHalf(object? sender, DragEventArgs e)
        {
#if WINDOWS
            if (e.PlatformArgs is PlatformDragEventArgs pd &&
                pd.DragEventArgs is { } nativeArgs &&
                sender is VisualElement ve &&
                ve.Handler?.PlatformView is Microsoft.UI.Xaml.FrameworkElement nativeEl)
            {
                var pt = nativeArgs.GetPosition(nativeEl);
                return pt.Y > nativeEl.ActualHeight / 2.0;
            }
#endif
            return false;
        }

        // Row 0 = top drop indicator, Row 2 = bottom drop indicator.
        // atBottom=true highlights the bottom edge.
        private static void ShowDropIndicator(object? sender, bool atBottom = false)
        {
            if (sender is not Grid g || g.Children.Count < 2) return;
            var top = g.Children[0]  as BoxView;
            var bot = g.Children[^1] as BoxView;
            if (top != null) top.IsVisible = false;
            if (bot != null) bot.IsVisible = false;
            if (atBottom && bot != null) bot.IsVisible = true;
            else if (!atBottom && top != null) top.IsVisible = true;
        }

        private static void HideDropIndicator(object? sender)
        {
            if (sender is not Grid g) return;
            if (g.Children.Count > 0 && g.Children[0] is BoxView top)   top.IsVisible = false;
            if (g.Children.Count > 1 && g.Children[^ 1] is BoxView bot)  bot.IsVisible = false;
        }

        // Suppress the platform "Copy" callout/glyph that appears beside the cursor.
        private static void SuppressDragCallout(DragEventArgs e)
        {
#if WINDOWS
            if (e.PlatformArgs is PlatformDragEventArgs pd &&
                pd.DragEventArgs?.DragUIOverride is { } ui)
            {
                ui.IsGlyphVisible   = false;
                ui.IsCaptionVisible = false;
                ui.IsContentVisible = false;
            }
#endif
        }

        // Chapters
        private void OnChapterDragStarting(object? sender, DragStartingEventArgs e)
        {
            if (sender is Element el && el.BindingContext is Chapter chapter)
                _draggingChapter = chapter;
        }

        private void OnChapterDragCompleted(object? sender, DropCompletedEventArgs e)
        { _draggingChapter = null; _chapterDropAfter = false; }

        private void OnChapterDragOver(object? sender, DragEventArgs e)
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
            if (sender is Element el && el.BindingContext is Chapter target)
            {
                var chapters = _viewModel.SelectedBook?.Chapters;
                var isLast = chapters is { Count: > 0 } && chapters[^1] == target;
                _chapterDropAfter = isLast && IsCursorInBottomHalf(sender, e);
            }
            ShowDropIndicator(sender, atBottom: _chapterDropAfter);
            SuppressDragCallout(e);
        }

        private void OnChapterDragLeave(object? sender, DragEventArgs e)
            => HideDropIndicator(sender);

        private void OnChapterDrop(object? sender, DropEventArgs e)
        {
            HideDropIndicator(sender);
            if (_draggingChapter is null) return;
            if (sender is Element el && el.BindingContext is Chapter target && target != _draggingChapter)
            {
                var chapters = _viewModel.SelectedBook?.Chapters;
                if (chapters is not null)
                {
                    var idx = chapters.IndexOf(target);
                    if (_chapterDropAfter) idx++;
                    _viewModel.MoveChapter(_draggingChapter, idx);
                }
            }
            _draggingChapter = null;
            _chapterDropAfter = false;
        }

        // Scenes
        private void OnSceneDragStarting(object? sender, DragStartingEventArgs e)
        {
            if (sender is Element el && el.BindingContext is Scene scene)
                _draggingScene = scene;
        }

        private void OnSceneDragCompleted(object? sender, DropCompletedEventArgs e)
        { _draggingScene = null; _sceneDropAfter = false; }

        private void OnSceneDragOver(object? sender, DragEventArgs e)
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
            if (sender is Element el && el.BindingContext is Scene target)
            {
                var scenes = _viewModel.SelectedChapter?.Scenes;
                var isLast = scenes is { Count: > 0 } && scenes[^1] == target;
                _sceneDropAfter = isLast && IsCursorInBottomHalf(sender, e);
            }
            ShowDropIndicator(sender, atBottom: _sceneDropAfter);
            SuppressDragCallout(e);
        }

        private void OnSceneDragLeave(object? sender, DragEventArgs e)
            => HideDropIndicator(sender);

        private void OnSceneDrop(object? sender, DropEventArgs e)
        {
            HideDropIndicator(sender);
            if (_draggingScene is null) return;
            if (sender is Element el && el.BindingContext is Scene target && target != _draggingScene)
            {
                var scenes = _viewModel.SelectedChapter?.Scenes;
                if (scenes is not null)
                {
                    var idx = scenes.IndexOf(target);
                    if (_sceneDropAfter) idx++;
                    _viewModel.MoveScene(_draggingScene, idx);
                }
            }
            _draggingScene = null;
            _sceneDropAfter = false;
        }

        private void OnSceneTitleUnfocused(object sender, FocusEventArgs e)
            => _viewModel.SaveSceneTitle();

        private void OnElementFieldUnfocused(object? sender, FocusEventArgs e)
            => _viewModel.SaveElementDetails();

        // ── Formatting toolbar ────────────────────────────────────────────────

        private async void OnBoldClicked(object sender, EventArgs e)
            => await EditorWebView.EvaluateJavaScriptAsync("execFormat('bold')");

        private async void OnItalicClicked(object sender, EventArgs e)
            => await EditorWebView.EvaluateJavaScriptAsync("execFormat('italic')");

        private async void OnUnderlineClicked(object sender, EventArgs e)
            => await EditorWebView.EvaluateJavaScriptAsync("execFormat('underline')");
    }
}
