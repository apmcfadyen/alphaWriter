using alphaWriter.Models;
using alphaWriter.Services;
using alphaWriter.Services.Export;
using CommunityToolkit.Maui.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;

namespace alphaWriter.ViewModels
{
    /// <summary>A chapter that can be ticked for export.</summary>
    public sealed partial class ExportChapterOption(string id, string label, bool isSelected) : ObservableObject
    {
        public string Id { get; } = id;
        public string Label { get; } = label;

        [ObservableProperty]
        private bool isSelected = isSelected;
    }

    /// <summary>Exporting the book, or some of its chapters, to Word, PDF, Markdown or text (Book workspace).</summary>
    public partial class WriterViewModel
    {
        public IReadOnlyList<string> ExportFormatOptions { get; } = ["Word (.docx)", "PDF", "Markdown", "Plain text"];
        public IReadOnlyList<string> ExportStyleOptions { get; } = ["Standard manuscript", "Reading copy"];
        public IReadOnlyList<string> ExportHeadingOptions { get; } = ["Chapter 1: Title", "Chapter 1", "Title only"];
        public IReadOnlyList<string> ExportSceneBreakOptions { get; } = ["#", "* * *", "Blank line"];

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsExportStyleRelevant))]
        private string exportFormat = ExportPref("export.format", "Word (.docx)");

        [ObservableProperty]
        private string exportStyle = ExportPref("export.style", "Standard manuscript");

        [ObservableProperty]
        private string exportHeadings = "Chapter 1: Title";

        [ObservableProperty]
        private string exportSceneBreak = "#";

        [ObservableProperty]
        private bool exportIncludeSceneTitles;

        [ObservableProperty]
        private bool exportIncludeOutline;

        [ObservableProperty]
        private bool exportTypewriterFont;

        [ObservableProperty]
        private bool exportFilePerChapter;

        /// <summary>True: the whole book. False: only the ticked chapters.</summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsExportChapterListVisible))]
        private bool exportWholeBook = true;

        [ObservableProperty]
        private ObservableCollection<ExportChapterOption> exportChapters = [];

        [ObservableProperty]
        private string exportStatus = string.Empty;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(ExportBookCommand))]
        private bool isExporting;

        public bool IsExportChapterListVisible => !ExportWholeBook;

        /// <summary>Page layout only applies to Word and PDF.</summary>
        public bool IsExportStyleRelevant => ExportFormat is "Word (.docx)" or "PDF";

        partial void OnExportFormatChanged(string value) => SetExportPref("export.format", value);
        partial void OnExportStyleChanged(string value) => SetExportPref("export.style", value);

        // Remembered choices are a convenience: never fail over them (no platform preferences in tests)
        private static string ExportPref(string key, string fallback)
        {
            try { return AppPreferences.Get(key, fallback); }
            catch { return fallback; }
        }

        private static void SetExportPref(string key, string value)
        {
            try { AppPreferences.Set(key, value); }
            catch { }
        }

        [RelayCommand]
        private void SetExportScope(string scope) => ExportWholeBook = scope == "book";

        /// <summary>Rebuilds the chapter checklist, keeping ticks for chapters that are still there.</summary>
        internal void RefreshExportChapters(string? onlyChapterId = null)
        {
            var ticked = ExportChapters.Where(c => c.IsSelected).Select(c => c.Id).ToHashSet();
            var chapters = SelectedBook?.Chapters ?? [];
            ExportChapters = new ObservableCollection<ExportChapterOption>(chapters.Select((c, i) =>
                new ExportChapterOption(c.Id, ManuscriptBuilder.Heading(i + 1, c.Title, ChapterHeadingStyle.NumberAndTitle),
                    onlyChapterId is not null ? c.Id == onlyChapterId : ticked.Contains(c.Id))));
        }

        /// <summary>Raised when the Book workspace should scroll to the export settings.</summary>
        public event Action? ExportPanelRequested;

        /// <summary>From the outline's chapter menu: opens Export with just this chapter ticked.</summary>
        [RelayCommand]
        private void ExportChapter(Chapter? chapter)
        {
            if (chapter is null || SelectedBook is null) return;
            ActiveWorkspace = Workspace.Book;
            ExportWholeBook = false;
            RefreshExportChapters(onlyChapterId: chapter.Id);
            ExportStatus = $"Ready to export “{chapter.Title}”. Choose a format and press Export.";
            ExportPanelRequested?.Invoke();
        }

        internal ExportOptions BuildExportOptions() => new()
        {
            Format = ExportFormat switch
            {
                "PDF" => Services.Export.ExportFormat.Pdf,
                "Markdown" => Services.Export.ExportFormat.Markdown,
                "Plain text" => Services.Export.ExportFormat.Text,
                _ => Services.Export.ExportFormat.Docx
            },
            Style = ExportStyle == "Reading copy" ? Services.Export.ExportStyle.ReadingCopy : Services.Export.ExportStyle.StandardManuscript,
            ChapterHeadings = ExportHeadings switch
            {
                "Chapter 1" => ChapterHeadingStyle.Number,
                "Title only" => ChapterHeadingStyle.Title,
                _ => ChapterHeadingStyle.NumberAndTitle
            },
            SceneBreak = ExportSceneBreak == "Blank line" ? string.Empty : ExportSceneBreak,
            IncludeSceneTitles = ExportIncludeSceneTitles,
            IncludeOutlineScenes = ExportIncludeOutline,
            TypewriterFont = ExportTypewriterFont,
            FilePerChapter = ExportFilePerChapter,
            ChapterIds = ExportWholeBook ? null : ExportChapters.Where(c => c.IsSelected).Select(c => c.Id).ToList()
        };

        private bool CanExport() => !IsExporting;

        [RelayCommand(CanExecute = nameof(CanExport))]
        private async Task ExportBook()
        {
            if (SelectedBook is null) return;
            var options = BuildExportOptions();
            if (options.ChapterIds is { Count: 0 })
            {
                ExportStatus = "Tick at least one chapter, or choose Whole book.";
                return;
            }

            IsExporting = true;
            ExportStatus = "Exporting…";
            try
            {
                if (options.Format == Services.Export.ExportFormat.Pdf) await PdfFonts.LoadBundledAsync();
                var book = SelectedBook;
                var file = await Task.Run(() => ExportService.Export(book, options));

                var lastFolder = ExportPref("export.folder", string.Empty);
                using var stream = new MemoryStream(file.Data);
                var result = Directory.Exists(lastFolder)
                    ? await FileSaver.Default.SaveAsync(lastFolder, file.FileName, stream, CancellationToken.None)
                    : await FileSaver.Default.SaveAsync(file.FileName, stream, CancellationToken.None);

                if (result.IsSuccessful)
                {
                    if (Path.GetDirectoryName(result.FilePath) is { Length: > 0 } folder)
                        SetExportPref("export.folder", folder);
                    ExportStatus = $"Saved {Path.GetFileName(result.FilePath)}.";
                }
                else
                {
                    ExportStatus = result.Exception is null or OperationCanceledException
                        ? "Export cancelled."
                        : $"Couldn't save: {result.Exception.Message}";
                }
            }
            catch (Exception ex)
            {
                ExportStatus = $"Couldn't export: {ex.Message}";
            }
            finally
            {
                IsExporting = false;
            }
        }
    }
}
