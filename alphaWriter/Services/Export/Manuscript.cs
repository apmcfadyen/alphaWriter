using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using alphaWriter.Models;

namespace alphaWriter.Services.Export
{
    // ── Options ──────────────────────────────────────────────────────────────

    public enum ExportFormat { Docx, Pdf, Markdown, Text }

    /// <summary>
    /// StandardManuscript: what agents and editors expect (12 pt, double-spaced, indented,
    /// a running header). ReadingCopy: a comfortable single-spaced book layout.
    /// </summary>
    public enum ExportStyle { StandardManuscript, ReadingCopy }

    public enum ChapterHeadingStyle { Number, NumberAndTitle, Title }

    public sealed record ExportOptions
    {
        public ExportFormat Format { get; init; } = ExportFormat.Docx;
        public ExportStyle Style { get; init; } = ExportStyle.StandardManuscript;
        public ChapterHeadingStyle ChapterHeadings { get; init; } = ChapterHeadingStyle.NumberAndTitle;

        /// <summary>Line between scenes in a chapter: "#", "* * *", or empty for a blank line.</summary>
        public string SceneBreak { get; init; } = "#";

        public bool IncludeSceneTitles { get; init; }
        public bool IncludeOutlineScenes { get; init; }

        /// <summary>Courier instead of a serif font (manuscript tradition).</summary>
        public bool TypewriterFont { get; init; }

        /// <summary>Chapters to export, by id; null or empty exports the whole book.</summary>
        public IReadOnlyCollection<string>? ChapterIds { get; init; }

        /// <summary>One file per chapter (delivered as a .zip) instead of one file.</summary>
        public bool FilePerChapter { get; init; }
    }

    // ── Model ────────────────────────────────────────────────────────────────

    /// <summary>A stretch of text with one formatting.</summary>
    public sealed record TextRun(string Text, bool Bold = false, bool Italic = false, bool Underline = false);

    public sealed record ManuscriptParagraph(IReadOnlyList<TextRun> Runs)
    {
        public string PlainText => string.Concat(Runs.Select(r => r.Text));
    }

    public sealed record ManuscriptScene(string Title, IReadOnlyList<ManuscriptParagraph> Paragraphs);

    /// <param name="Number">Position in the whole book (1-based), even when exporting a selection.</param>
    public sealed record ManuscriptChapter(int Number, string Title, string Heading, IReadOnlyList<ManuscriptScene> Scenes);

    /// <summary>
    /// A book (or some of its chapters) as clean, format-neutral text: comments removed,
    /// editor markup reduced to paragraphs and bold/italic/underline runs. Every writer
    /// reads this, so all formats agree on what's in the manuscript.
    /// </summary>
    public sealed record Manuscript(string Title, string Author, int WordCount, IReadOnlyList<ManuscriptChapter> Chapters);

    // ── Builder ──────────────────────────────────────────────────────────────

    public static class ManuscriptBuilder
    {
        public static Manuscript Build(Book book, ExportOptions options)
        {
            var wanted = options.ChapterIds is { Count: > 0 } ids ? ids.ToHashSet() : null;
            var chapters = new List<ManuscriptChapter>();
            int words = 0;

            for (int i = 0; i < book.Chapters.Count; i++)
            {
                var chapter = book.Chapters[i];
                if (wanted is not null && !wanted.Contains(chapter.Id)) continue;

                var scenes = new List<ManuscriptScene>();
                foreach (var scene in chapter.Scenes)
                {
                    if (scene.Status == SceneStatus.Outline && !options.IncludeOutlineScenes) continue;
                    var paragraphs = ParseScene(scene.Content ?? string.Empty);
                    if (paragraphs.Count == 0) continue;
                    scenes.Add(new ManuscriptScene(scene.Title, paragraphs));
                    words += Scene.ExtractWords(scene.Content ?? string.Empty).Count;
                }

                int number = i + 1;
                chapters.Add(new ManuscriptChapter(number, chapter.Title, Heading(number, chapter.Title, options.ChapterHeadings), scenes));
            }

            return new Manuscript(
                string.IsNullOrWhiteSpace(book.Title) ? "Untitled" : book.Title.Trim(),
                book.Author?.Trim() ?? string.Empty,
                words, chapters);
        }

        internal static string Heading(int number, string title, ChapterHeadingStyle style)
        {
            title = title?.Trim() ?? string.Empty;
            // A default title like "Chapter 4" says nothing more than the number
            if (Regex.IsMatch(title, @"^chapter\s+\d+$", RegexOptions.IgnoreCase)) title = string.Empty;
            return style switch
            {
                ChapterHeadingStyle.Number => $"Chapter {number}",
                ChapterHeadingStyle.Title when title.Length > 0 => title,
                ChapterHeadingStyle.NumberAndTitle when title.Length > 0 => $"Chapter {number}: {title}",
                _ => $"Chapter {number}"
            };
        }

        /// <summary>Word count rounded the way manuscripts state it ("about 87,000 words").</summary>
        public static int RoundedWordCount(int words) =>
            words < 1000 ? (int)Math.Round(words / 100.0, MidpointRounding.AwayFromZero) * 100
                         : (int)Math.Round(words / 1000.0, MidpointRounding.AwayFromZero) * 1000;

        // ── Scene HTML → paragraphs ──────────────────────────────────────────

        private static readonly Regex Token = new(@"<(/?)([a-zA-Z0-9]+)[^>]*>|[^<]+", RegexOptions.Compiled);
        private static readonly HashSet<string> BlockTags = new(StringComparer.OrdinalIgnoreCase)
            { "p", "div", "h1", "h2", "h3", "h4", "h5", "h6", "li", "blockquote", "br" };

        /// <summary>A character with the formatting it was written in.</summary>
        private readonly record struct Styled(char C, bool B, bool I, bool U);

        /// <summary>
        /// Splits editor HTML into paragraphs of formatted runs, then removes // line comments
        /// (to the end of their paragraph) and /* block comments */ (which may span
        /// paragraphs), matching how the editor leaves them out of the word count.
        /// </summary>
        internal static List<ManuscriptParagraph> ParseScene(string html)
        {
            var rawParagraphs = new List<List<Styled>> { new() };
            int bold = 0, italic = 0, underline = 0;

            foreach (Match m in Token.Matches(html))
            {
                if (!m.Groups[2].Success)
                {
                    var text = WebUtility.HtmlDecode(m.Value).Replace(' ', ' ');
                    foreach (var c in text)
                        if (c is '\r' or '\n') rawParagraphs[^1].Add(new Styled(' ', bold > 0, italic > 0, underline > 0));
                        else rawParagraphs[^1].Add(new Styled(c, bold > 0, italic > 0, underline > 0));
                    continue;
                }

                bool closing = m.Groups[1].Value == "/";
                var tag = m.Groups[2].Value.ToLowerInvariant();
                int delta = closing ? -1 : 1;
                switch (tag)
                {
                    case "b" or "strong": bold = Math.Max(0, bold + delta); break;
                    case "i" or "em": italic = Math.Max(0, italic + delta); break;
                    case "u": underline = Math.Max(0, underline + delta); break;
                    default:
                        if (BlockTags.Contains(tag) && rawParagraphs[^1].Count > 0)
                            rawParagraphs.Add(new List<Styled>());
                        break;
                }
            }

            var paragraphs = new List<ManuscriptParagraph>();
            bool inBlockComment = false;
            foreach (var raw in rawParagraphs)
            {
                var kept = new List<Styled>(raw.Count);
                for (int i = 0; i < raw.Count; i++)
                {
                    char c = raw[i].C, next = i + 1 < raw.Count ? raw[i + 1].C : '\0';
                    if (inBlockComment)
                    {
                        if (c == '*' && next == '/') { inBlockComment = false; i++; }
                        continue;
                    }
                    if (c == '/' && next == '*') { inBlockComment = true; i++; continue; }
                    if (c == '/' && next == '/') break;   // line comment: rest of the paragraph
                    kept.Add(raw[i]);
                }
                if (ToParagraph(kept) is { } paragraph) paragraphs.Add(paragraph);
            }
            return paragraphs;
        }

        /// <summary>Collapses whitespace and groups characters into runs; null when nothing is left.</summary>
        private static ManuscriptParagraph? ToParagraph(List<Styled> chars)
        {
            var runs = new List<TextRun>();
            var sb = new StringBuilder();
            Styled? style = null;
            bool lastWasSpace = true;   // trims leading spaces

            foreach (var ch in chars)
            {
                bool space = char.IsWhiteSpace(ch.C);
                if (space && lastWasSpace) continue;
                lastWasSpace = space;
                if (style is { } s && (s.B != ch.B || s.I != ch.I || s.U != ch.U))
                {
                    runs.Add(new TextRun(sb.ToString(), s.B, s.I, s.U));
                    sb.Clear();
                }
                style = ch;
                sb.Append(space ? ' ' : ch.C);
            }
            if (style is { } last && sb.Length > 0) runs.Add(new TextRun(sb.ToString(), last.B, last.I, last.U));

            // Trim the trailing space off the final run
            if (runs.Count > 0 && runs[^1].Text.EndsWith(' '))
                runs[^1] = runs[^1] with { Text = runs[^1].Text.TrimEnd() };
            runs.RemoveAll(r => r.Text.Length == 0);
            return runs.Count == 0 ? null : new ManuscriptParagraph(runs);
        }
    }
}
