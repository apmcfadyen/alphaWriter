using System.Collections.Generic;
using alphaWriter.Models;
using alphaWriter.Models.Analysis;

namespace alphaWriter.Services.Nlp
{
    public interface ICharacterVoiceAnalyzer
    {
        /// <summary>
        /// Builds a voice profile for each character with a viewpoint role.
        /// When <paramref name="posService"/> is provided (and loaded), the profile
        /// is extended with POS-dependent metrics: PassiveVoiceRate, OpenerVariety,
        /// ClauseComplexity, and per-scene SceneSnapshots for the consistency timeline.
        /// </summary>
        List<CharacterVoiceProfile> BuildProfiles(List<SceneAnalysisResult> results, Book book,
            IPosTaggingService? posService = null);

        List<NlpNote> DetectVoiceAnomalies(List<CharacterVoiceProfile> profiles,
            List<SceneAnalysisResult> results, Book book);

        /// <summary>
        /// Builds a dialogue voice profile for each character with attributed speech lines.
        /// Uses speech tag heuristics to attribute dialogue to characters.
        /// </summary>
        List<DialogueVoiceProfile> BuildDialogueProfiles(List<SceneAnalysisResult> results, Book book,
            IPosTaggingService? posService = null);

        /// <summary>
        /// Detects cross-character voice similarity — flags pairs of characters whose
        /// dialogue fingerprints are too alike.
        /// </summary>
        List<NlpNote> DetectDialogueVoiceAnomalies(List<DialogueVoiceProfile> profiles);
    }
}
