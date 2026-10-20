using System.Collections.Generic;

namespace alphaWriter.Models.Analysis
{
    /// <summary>A character range in a scene's normalized plain text (see SceneDocument.Text).</summary>
    public class TextSpan
    {
        public int Start { get; set; }
        public int Length { get; set; }

        /// <summary>Text of the sentence containing this span (see <see cref="NlpNote.AnchorText"/>).</summary>
        public string AnchorText { get; set; } = string.Empty;
        public int AnchorOccurrence { get; set; }
        public int AnchorOffset { get; set; }

        public TextSpan() { }
        public TextSpan(int start, int length) { Start = start; Length = length; }
    }

    public class NlpNote
    {
        public NlpNoteSeverity Severity { get; set; }
        public NlpNoteCategory Category { get; set; }
        public string SceneId { get; set; } = string.Empty;
        public string SceneTitle { get; set; } = string.Empty;
        public string ChapterTitle { get; set; } = string.Empty;
        public int? SentenceIndex { get; set; }
        public string Message { get; set; } = string.Empty;

        // ── Identity & location (Phase 0) ────────────────────────────────────

        /// <summary>Stable rule identifier, e.g. "style.passive". See RuleCatalog.</summary>
        public string RuleId { get; set; } = string.Empty;

        /// <summary>Primary span in the scene's normalized plain text, when the note points at specific words.</summary>
        public int? Start { get; set; }
        public int? Length { get; set; }

        /// <summary>The exact text the rule matched (e.g. the adverb, the echoed word).</summary>
        public string Evidence { get; set; } = string.Empty;

        /// <summary>
        /// The full text of the sentence containing the primary span. Used to re-locate the
        /// span after edits and to find it in the editor DOM.
        /// </summary>
        public string AnchorText { get; set; } = string.Empty;

        /// <summary>0-based occurrence of <see cref="AnchorText"/> among identical sentences in the scene.</summary>
        public int AnchorOccurrence { get; set; }

        /// <summary>Offset of the primary span within <see cref="AnchorText"/>.</summary>
        public int? AnchorOffset { get; set; }

        /// <summary>Additional highlighted spans (e.g. every occurrence of an echoed word).</summary>
        public List<TextSpan> ExtraSpans { get; set; } = [];

        /// <summary>One-click replacements for the primary span, best first.</summary>
        public List<string> Suggestions { get; set; } = [];

        /// <summary>
        /// Hash of rule + evidence + anchor sentence. Survives edits elsewhere in the scene,
        /// so a dismissed note stays dismissed across re-analysis.
        /// </summary>
        public string Fingerprint { get; set; } = string.Empty;

        public bool HasSpan => Start.HasValue && Length.HasValue;

        public string SeverityColor => Severity switch
        {
            NlpNoteSeverity.Issue => "#E06C75",
            NlpNoteSeverity.Warning => "#E5C07B",
            _ => "#61AFEF"
        };

        /// <summary>Matches the editor underline colour for this note's category (editor.html ::highlight).</summary>
        public string CategoryColor => Category switch
        {
            NlpNoteCategory.CopyEditor => "#61AFEF",
            NlpNoteCategory.LineEditor => "#E5C07B",
            _ => "#C678DD"
        };

        public string CategoryLabel => Category switch
        {
            NlpNoteCategory.CopyEditor => "Copy Editor",
            NlpNoteCategory.LineEditor => "Line Editor",
            NlpNoteCategory.DevelopmentalEditor => "Developmental Editor",
            _ => Category.ToString()
        };
    }
}
