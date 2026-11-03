using System.Text;
using System.Text.RegularExpressions;

namespace alphaWriter.Services.Export
{
    /// <summary>Markdown: # headings, *italic*, **bold**, &lt;u&gt;underline&lt;/u&gt;.</summary>
    public static class MarkdownWriter
    {
        public static string Write(Manuscript m, ExportOptions options)
        {
            var sb = new StringBuilder();
            sb.Append("# ").AppendLine(Escape(m.Title));
            if (m.Author.Length > 0) sb.AppendLine().Append("by ").AppendLine(Escape(m.Author));

            foreach (var chapter in m.Chapters)
            {
                sb.AppendLine().Append("## ").AppendLine(Escape(chapter.Heading));
                for (int s = 0; s < chapter.Scenes.Count; s++)
                {
                    var scene = chapter.Scenes[s];
                    if (s > 0) sb.AppendLine().AppendLine(SceneBreak(options.SceneBreak));
                    if (options.IncludeSceneTitles) sb.AppendLine().Append("### ").AppendLine(Escape(scene.Title));
                    foreach (var p in scene.Paragraphs)
                        sb.AppendLine().AppendLine(string.Concat(p.Runs.Select(Format)));
                }
            }
            return sb.ToString();
        }

        /// <summary>A lone "#" would become a heading, so it is escaped.</summary>
        private static string SceneBreak(string marker) =>
            marker.Length == 0 ? "&nbsp;" : Escape(marker);

        private static string Format(TextRun r)
        {
            // Keep spaces outside the markers: "** bold**" isn't bold in Markdown
            var text = Escape(r.Text.Trim());
            if (text.Length == 0) return r.Text;
            if (r.Underline) text = $"<u>{text}</u>";
            if (r.Bold && r.Italic) text = $"***{text}***";
            else if (r.Bold) text = $"**{text}**";
            else if (r.Italic) text = $"*{text}*";
            var lead = r.Text.Length - r.Text.TrimStart().Length;
            var trail = r.Text.Length - r.Text.TrimEnd().Length;
            return new string(' ', lead) + text + new string(' ', trail);
        }

        private static readonly Regex Special = new(@"([\\`*_\[\]<>#])", RegexOptions.Compiled);

        internal static string Escape(string text) => Special.Replace(text, @"\$1");
    }

    /// <summary>Plain text: headings on their own lines, a blank line between paragraphs.</summary>
    public static class PlainTextWriter
    {
        public static string Write(Manuscript m, ExportOptions options)
        {
            var sb = new StringBuilder();
            sb.AppendLine(m.Title);
            if (m.Author.Length > 0) sb.Append("by ").AppendLine(m.Author);

            foreach (var chapter in m.Chapters)
            {
                sb.AppendLine().AppendLine().AppendLine(chapter.Heading);
                for (int s = 0; s < chapter.Scenes.Count; s++)
                {
                    var scene = chapter.Scenes[s];
                    if (s > 0) sb.AppendLine().AppendLine(options.SceneBreak);
                    if (options.IncludeSceneTitles) sb.AppendLine().AppendLine(scene.Title);
                    foreach (var p in scene.Paragraphs)
                        sb.AppendLine().AppendLine(p.PlainText);
                }
            }
            return sb.ToString();
        }
    }
}
