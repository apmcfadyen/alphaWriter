using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;
using System.Text.RegularExpressions;

namespace alphaWriter.Models
{
    public class Scene : INotifyPropertyChanged
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

        private string _content = string.Empty;
        public string Content
        {
            get => _content;
            set
            {
                if (_content == value) return;
                _content = value;
                Notify(nameof(Content));
                WordCount = ComputeWordCount();
            }
        }

        private int _wordCount;
        public int WordCount
        {
            get => _wordCount;
            set
            {
                if (_wordCount == value) return;
                _wordCount = value;
                Notify(nameof(WordCount));
            }
        }

        // ── Scene Metadata ──────────────────────────────────────────────────

        private SceneStatus _status = SceneStatus.Outline;
        public SceneStatus Status
        {
            get => _status;
            set
            {
                if (_status == value) return;
                _status = value;
                Notify(nameof(Status));
            }
        }

        public List<string> ViewpointCharacterIds { get; set; } = new();

        // ── Scene entity participation ────────────────────────────────────────
        // All characters / locations / items that appear in the scene, whether
        // auto-detected from the text or manually linked by the writer.
        public List<string> CharacterIds { get; set; } = new();
        public List<string> LocationIds  { get; set; } = new();
        public List<string> ItemIds      { get; set; } = new();

        private string _goal = string.Empty;
        public string Goal
        {
            get => _goal;
            set
            {
                if (_goal == value) return;
                _goal = value;
                Notify(nameof(Goal));
            }
        }

        private string _conflict = string.Empty;
        public string Conflict
        {
            get => _conflict;
            set
            {
                if (_conflict == value) return;
                _conflict = value;
                Notify(nameof(Conflict));
            }
        }

        private string _outcome = string.Empty;
        public string Outcome
        {
            get => _outcome;
            set
            {
                if (_outcome == value) return;
                _outcome = value;
                Notify(nameof(Outcome));
            }
        }

        private string _notes = string.Empty;
        public string Notes
        {
            get => _notes;
            set
            {
                if (_notes == value) return;
                _notes = value;
                Notify(nameof(Notes));
            }
        }

        // Transient — populated after NLP analysis, not persisted
        private int _analysisNoteCount;
        [System.Text.Json.Serialization.JsonIgnore]
        public int AnalysisNoteCount
        {
            get => _analysisNoteCount;
            set
            {
                if (_analysisNoteCount == value) return;
                _analysisNoteCount = value;
                Notify(nameof(AnalysisNoteCount));
                Notify(nameof(HasAnalysisNotes));
            }
        }

        [System.Text.Json.Serialization.JsonIgnore]
        public bool HasAnalysisNotes => _analysisNoteCount > 0;

        private int ComputeWordCount()
        {
            if (string.IsNullOrWhiteSpace(_content)) return 0;
            var text = DecodeEntities(StripComments(StripHtml(DecodeUnicodeEscapes(_content))));
            return text.Split([' ', '\u00A0', '\n', '\r', '\t'],
                StringSplitOptions.RemoveEmptyEntries).Length;
        }

        /// <summary>
        /// Returns all words from the scene's HTML content as lowercase strings,
        /// with HTML, comments, and entities stripped. Used for statistics.
        /// </summary>
        public static IReadOnlyList<string> ExtractWords(string htmlContent)
        {
            if (string.IsNullOrWhiteSpace(htmlContent)) return [];
            var text = DecodeEntities(StripComments(StripHtml(DecodeUnicodeEscapes(htmlContent))));
            return text.Split([' ', '\u00A0', '\n', '\r', '\t'],
                    StringSplitOptions.RemoveEmptyEntries)
                .Select(w => CleanWord(w).ToLowerInvariant())
                .Where(w => w.Length > 0)
                .ToList();
        }

        // Decodes \uXXXX sequences that WebView2 may leave as literal text when
        // the JSON wrapper is stripped before reaching our code.
        private static readonly Regex _unicodeEscapeRx =
            new(@"\\u([0-9a-fA-F]{4})", RegexOptions.Compiled);

        internal static string DecodeUnicodeEscapes(string text) =>
            _unicodeEscapeRx.Replace(text,
                m => ((char)Convert.ToInt32(m.Groups[1].Value, 16)).ToString());

        // Strips leading/trailing punctuation from a token so "word," → "word".
        private static string CleanWord(string token)
        {
            int start = 0, end = token.Length - 1;
            while (start <= end && !char.IsLetterOrDigit(token[start])) start++;
            while (end >= start && !char.IsLetterOrDigit(token[end])) end--;
            return start > end ? string.Empty : token[start..(end + 1)];
        }

        // Converts HTML to plain text. Block-level elements (<br>, <div>, <p>)
        // emit \n so that // line comments have a newline to terminate against.
        internal static string StripHtml(string html)
        {
            var sb = new StringBuilder();
            var tagBuf = new StringBuilder();
            bool inTag = false;

            foreach (char c in html)
            {
                if (c == '<') { inTag = true; tagBuf.Clear(); continue; }
                if (c == '>')
                {
                    inTag = false;
                    // Tag name: strip leading '/' (closing tags) and attributes
                    var raw = tagBuf.ToString().Trim();
                    var name = raw.TrimStart('/').Split(' ', '\t')[0].ToLowerInvariant();
                    if (name is "br" or "div" or "p" or "li" or "tr")
                        sb.Append('\n');
                    tagBuf.Clear();
                    continue;
                }
                if (inTag) tagBuf.Append(c);
                else sb.Append(c);
            }
            return sb.ToString();
        }

        // Decodes common HTML entities to match the JS-side computeWordCount.
        internal static string DecodeEntities(string text)
        {
            return text.Replace("&nbsp;", " ")
                       .Replace("&amp;", "&")
                       .Replace("&lt;", "<")
                       .Replace("&gt;", ">")
                       .Replace("&quot;", "\"");
        }

        // Strips /* block comments */ and // line comments from plain text.
        internal static string StripComments(string text)
        {
            var sb = new StringBuilder(text.Length);
            int i = 0;
            while (i < text.Length)
            {
                // Block comment: /* ... */
                if (i + 1 < text.Length && text[i] == '/' && text[i + 1] == '*')
                {
                    i += 2;
                    while (i + 1 < text.Length && !(text[i] == '*' && text[i + 1] == '/'))
                        i++;
                    i += 2; // skip closing */
                    continue;
                }

                // Line comment: // ... \n
                if (i + 1 < text.Length && text[i] == '/' && text[i + 1] == '/')
                {
                    i += 2;
                    while (i < text.Length && text[i] != '\n' && text[i] != '\r')
                        i++;
                    continue; // \n itself is preserved on next iteration
                }

                sb.Append(text[i]);
                i++;
            }
            return sb.ToString();
        }
    }
}
