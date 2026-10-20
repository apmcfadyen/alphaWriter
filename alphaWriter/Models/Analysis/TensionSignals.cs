namespace alphaWriter.Models.Analysis
{
    /// <summary>
    /// Raw, text-only ingredients of a scene's tension. They are combined into a score
    /// relative to the rest of the book (see TensionArc), so no single value means much alone.
    /// </summary>
    public class TensionSignals
    {
        /// <summary>Danger, conflict and urgency words per 100 words.</summary>
        public double DangerRate { get; set; }

        /// <summary>Share of narration sentences of eight words or fewer.</summary>
        public double ShortSentenceShare { get; set; }

        /// <summary>Share of paragraphs that are mostly physical action.</summary>
        public double ActionShare { get; set; }

        /// <summary>Questions and exclamations per dialogue sentence (arguments, alarm).</summary>
        public double HeatedDialogue { get; set; }

        public int Words { get; set; }
    }
}
