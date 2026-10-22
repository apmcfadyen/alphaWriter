using alphaWriter.Services;
using alphaWriter.Services.Nlp;
using alphaWriter.Services.Nlp.Rules;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace alphaWriter.ViewModels
{
    /// <summary>
    /// The open scene's report card (Revise), genre guidelines, and the exportable
    /// editorial summary for the whole book.
    /// </summary>
    public partial class WriterViewModel
    {
        /// <summary>The open scene at a glance, once the book has been analyzed.</summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsSceneCardVisible))]
        private SceneReportCard? sceneCard;

        public bool IsSceneCardVisible => SceneCard is not null && ReviseScopeIsScene && IsBrowsingNotes;

        internal void RefreshSceneCard()
        {
            var result = SelectedScene is null ? null : _analysisResults?.FirstOrDefault(r => r.SceneId == SelectedScene.Id);
            SceneCard = result is null || result.Style.TotalWords == 0
                ? null
                : SceneReportCard.Build(result, TensionArc.Compute(_analysisResults!), VisibleNotes);
            OnPropertyChanged(nameof(IsSceneCardVisible));
        }

        /// <summary>The book's measurements against its genre's rule-of-thumb ranges.</summary>
        public List<BenchmarkRow> BenchmarkRows => SelectedBook is null || _analysisResults is null
            ? []
            : GenreBenchmarks.Compare(SelectedBook.StyleSheet?.Genre, SelectedBook, _analysisResults);

        /// <summary>
        /// Writes the editorial summary as an HTML page (Documents\alphaWriter, or the
        /// sandbox data folder) and opens it in the default browser.
        /// </summary>
        [RelayCommand]
        private async Task ExportSummary()
        {
            try
            {
                if (await WriteSummaryAsync() is not { } path) return;
                await Launcher.Default.OpenAsync(new OpenFileRequest("Editorial summary", new ReadOnlyFile(path)));
            }
            catch (Exception ex)
            {
                AnalysisProgress = $"Couldn't export the summary: {ex.Message}";
            }
        }

        /// <summary>Writes the summary file and returns its path (null with nothing analyzed).</summary>
        internal async Task<string?> WriteSummaryAsync()
        {
            if (SelectedBook is null || _analysisResults is null || _analysisResults.Count == 0) return null;
            var html = EditorialSummaryWriter.BuildHtml(SelectedBook, _analysisResults, VisibleNotes.ToList(), DateTime.Now);
            var folder = AppPaths.IsSandboxed
                ? Path.Combine(AppPaths.DataDirectory, "exports")
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "alphaWriter");
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, SummaryFileName(SelectedBook.Title, DateTime.Now));
            await File.WriteAllTextAsync(path, html);
            AnalysisProgress = AppPaths.IsSandboxed
                ? $"Summary saved to {path}"
                : $"Summary saved in Documents › alphaWriter: {Path.GetFileName(path)}";
            return path;
        }

        internal static string SummaryFileName(string title, DateTime date)
        {
            var safe = string.Concat((string.IsNullOrWhiteSpace(title) ? "Book" : title)
                .Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '-' : c)).Trim();
            if (safe.Length > 60) safe = safe[..60].Trim();
            return $"{safe} - editorial summary {date:yyyy-MM-dd}.html";
        }
    }
}
