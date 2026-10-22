using System.Text;
using System.Text.RegularExpressions;

namespace alphaWriter.Services
{
    /// <summary>
    /// Moves scene HTML out of the editor WebView without string escaping. MAUI's
    /// EvaluateJavaScriptAsync on Windows sometimes returns a JSON string without its
    /// quotes, and decoding that by hand left <c>\"</c> in the text, so every save added
    /// backslashes before quotes. editor.html now returns base64, which has nothing to escape.
    /// </summary>
    public static class EditorContentCodec
    {
        /// <summary>
        /// Decodes a base64 (UTF-8) result from the editor. Null when the WebView returned
        /// nothing usable, so callers keep the scene's existing content rather than wiping it.
        /// </summary>
        public static string? DecodeBase64Result(string? raw)
        {
            if (raw is null) return null;
            var b64 = raw.Trim().Trim('"');
            if (b64.Length == 0) return string.Empty;
            try
            {
                return Encoding.UTF8.GetString(Convert.FromBase64String(b64));
            }
            catch (FormatException)
            {
                return null;
            }
        }

        private static readonly Regex BackslashesBeforeQuote = new(@"\\+(?="")", RegexOptions.Compiled);
        private static readonly Regex BackslashRuns = new(@"\\{2,}", RegexOptions.Compiled);

        /// <summary>
        /// Repairs scene HTML damaged by the old escaping bug: removes runs of backslashes
        /// in front of quotes (<c>\\\"Hello\\\"</c> → <c>"Hello"</c>), and collapses longer runs
        /// of backslashes, which each save had doubled, back to one.
        /// </summary>
        public static string RepairEscapedQuotes(string html)
        {
            if (string.IsNullOrEmpty(html) || !html.Contains('\\')) return html;
            var fixedQuotes = BackslashesBeforeQuote.Replace(html, string.Empty);
            return BackslashRuns.Replace(fixedQuotes, "\\");
        }

        /// <summary>Repairs every scene in the books; true when anything changed (and needs saving).</summary>
        public static bool RepairBooks(IEnumerable<Models.Book> books)
        {
            bool changed = false;
            foreach (var scene in books.SelectMany(b => b.Chapters).SelectMany(c => c.Scenes))
            {
                var repaired = RepairEscapedQuotes(scene.Content ?? string.Empty);
                if (repaired == (scene.Content ?? string.Empty)) continue;
                scene.Content = repaired;
                changed = true;
            }
            return changed;
        }
    }
}
