using System.IO.Compression;
using System.Text;
using alphaWriter.Models;

namespace alphaWriter.Services.Export
{
    /// <summary>A finished export, ready to save.</summary>
    public sealed record ExportFile(string FileName, byte[] Data);

    /// <summary>
    /// Builds a manuscript and writes it in the chosen format: one file, or (one file per
    /// chapter) a .zip of chapter files, so it saves in one step on every platform.
    /// </summary>
    public static class ExportService
    {
        public static ExportFile Export(Book book, ExportOptions options)
        {
            var manuscript = ManuscriptBuilder.Build(book, options);
            if (manuscript.Chapters.Count == 0)
                throw new InvalidOperationException("There are no chapters to export.");

            var baseName = SafeName(manuscript.Title);
            if (options.FilePerChapter && manuscript.Chapters.Count > 1)
            {
                using var zip = new MemoryStream();
                using (var archive = new ZipArchive(zip, ZipArchiveMode.Create, leaveOpen: true))
                {
                    foreach (var chapter in manuscript.Chapters)
                    {
                        var single = manuscript with { Chapters = [chapter] };
                        var entry = archive.CreateEntry($"{chapter.Number:00} {SafeName(chapter.Heading)}{Extension(options.Format)}");
                        using var stream = entry.Open();
                        stream.Write(Render(single, options));
                    }
                }
                return new ExportFile($"{baseName} - chapters.zip", zip.ToArray());
            }

            var suffix = manuscript.Chapters.Count == 1 && options.ChapterIds is { Count: > 0 }
                ? $" - {SafeName(manuscript.Chapters[0].Heading)}"
                : string.Empty;
            return new ExportFile(baseName + suffix + Extension(options.Format), Render(manuscript, options));
        }

        public static byte[] Render(Manuscript manuscript, ExportOptions options) => options.Format switch
        {
            ExportFormat.Docx => DocxWriter.Write(manuscript, options),
            ExportFormat.Pdf => PdfWriter.Write(manuscript, options),
            ExportFormat.Markdown => Encoding.UTF8.GetBytes(MarkdownWriter.Write(manuscript, options)),
            _ => Encoding.UTF8.GetBytes(PlainTextWriter.Write(manuscript, options))
        };

        public static string Extension(ExportFormat format) => format switch
        {
            ExportFormat.Docx => ".docx",
            ExportFormat.Pdf => ".pdf",
            ExportFormat.Markdown => ".md",
            _ => ".txt"
        };

        internal static string SafeName(string name)
        {
            var safe = string.Concat((string.IsNullOrWhiteSpace(name) ? "Untitled" : name)
                .Select(c => Path.GetInvalidFileNameChars().Contains(c) || c == ':' ? '-' : c)).Trim();
            return safe.Length > 60 ? safe[..60].Trim() : safe;
        }
    }
}
