using alphaWriter.Models.Analysis;

namespace alphaWriter.Services.Nlp
{
    public interface IStyleAnalyzer
    {
        /// <summary>
        /// Computes a StyleProfile from raw sentences, including ReadabilityScore.
        /// Does not require POS tagging.
        /// </summary>
        StyleProfile Analyze(IReadOnlyList<string> sentences);

        /// <summary>
        /// Computes a full StyleProfile including ReadabilityScore.
        /// When <paramref name="posService"/> is provided, also populates
        /// OpenerVariety and ClauseComplexity.
        /// </summary>
        StyleProfile AnalyzeExtended(IReadOnlyList<string> sentences, IPosTaggingService? posService);

        List<NlpNote> DetectAnomalies(StyleProfile sceneProfile, StyleProfile chapterProfile,
            string sceneId, string sceneTitle, string chapterTitle);

        /// <summary>
        /// Flags sentences that use passive voice (AUX be-verb + VERB pattern).
        /// Requires a loaded <paramref name="posService"/> for accurate VERB vs ADJ
        /// disambiguation — e.g. "was tired" (ADJ) is NOT flagged, "was defeated" (VERB) IS.
        /// </summary>
        List<NlpNote> DetectPassiveVoice(IReadOnlyList<string> sentences,
            IPosTaggingService posService,
            string sceneId, string sceneTitle, string chapterTitle);

        /// <summary>
        /// Flags sentences with ≥ 2 -ly adverbs, and emits a scene-level summary note
        /// when overall adverb density exceeds 3% of total words.
        /// </summary>
        List<NlpNote> DetectAdverbDensity(IReadOnlyList<string> sentences,
            string sceneId, string sceneTitle, string chapterTitle);

        /// <summary>
        /// Flags sentences that directly name a character's emotion rather than showing it
        /// through action or physical sensation ("felt angry" vs "her hands clenched").
        /// </summary>
        List<NlpNote> DetectShowDontTell(IReadOnlyList<string> sentences,
            string sceneId, string sceneTitle, string chapterTitle);

        /// <summary>
        /// Detects when the same content word recurs in 3+ sentences within a
        /// 5-sentence window — audible echo even when readers cannot name it.
        /// </summary>
        List<NlpNote> DetectProximityEchoes(IReadOnlyList<string> sentences,
            string sceneId, string sceneTitle, string chapterTitle);

        /// <summary>
        /// Flags when >60% of sentences in a scene open with the same grammatical
        /// category, creating a monotonous rhythmic pattern.
        /// Requires a loaded <paramref name="posService"/>.
        /// </summary>
        List<NlpNote> DetectSentenceOpenerMonotony(IReadOnlyList<string> sentences,
            IPosTaggingService posService,
            string sceneId, string sceneTitle, string chapterTitle);

        /// <summary>
        /// Flags sentences with an unusually high count of subordinating conjunctions
        /// and scenes where the structural complexity is consistently elevated.
        /// </summary>
        List<NlpNote> DetectSubordinateClauseDensity(IReadOnlyList<string> sentences,
            string sceneId, string sceneTitle, string chapterTitle);

        /// <summary>
        /// Returns the fraction of total words that are -ly adverbs.
        /// Used by CharacterVoiceAnalyzer to populate AdverbDensity.
        /// </summary>
        double ComputeAdverbDensity(IReadOnlyList<string> sentences);

        /// <summary>
        /// Returns the number of sentences that contain a passive construction.
        /// Used by CharacterVoiceAnalyzer to populate PassiveVoiceRate.
        /// </summary>
        int CountPassiveSentences(IReadOnlyList<string> sentences, IPosTaggingService posService);
    }
}
