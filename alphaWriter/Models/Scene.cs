using System;
using System.ComponentModel;
using System.Text;

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

        private int ComputeWordCount()
        {
            if (string.IsNullOrWhiteSpace(_content)) return 0;
            var text = DecodeEntities(StripComments(StripHtml(_content)));
            return text.Split([' ', '\u00A0', '\n', '\r', '\t'],
                StringSplitOptions.RemoveEmptyEntries).Length;
        }

        // Converts HTML to plain text. Block-level elements (<br>, <div>, <p>)
        // emit \n so that // line comments have a newline to terminate against.
        private static string StripHtml(string html)
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
        private static string DecodeEntities(string text)
        {
            return text.Replace("&nbsp;", " ")
                       .Replace("&amp;", "&")
                       .Replace("&lt;", "<")
                       .Replace("&gt;", ">")
                       .Replace("&quot;", "\"");
        }

        // Strips /* block comments */ and // line comments from plain text.
        private static string StripComments(string text)
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
