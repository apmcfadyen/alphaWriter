using System.Collections.Generic;
using alphaWriter.Models;
using alphaWriter.Models.Analysis;

namespace alphaWriter.Services.Nlp
{
    public interface ICharacterVoiceAnalyzer
    {
        List<CharacterVoiceProfile> BuildProfiles(List<SceneAnalysisResult> results, Book book);

        List<NlpNote> DetectVoiceAnomalies(List<CharacterVoiceProfile> profiles,
            List<SceneAnalysisResult> results, Book book);
    }
}
