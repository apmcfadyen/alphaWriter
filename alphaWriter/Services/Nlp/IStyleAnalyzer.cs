using alphaWriter.Models.Analysis;

namespace alphaWriter.Services.Nlp
{
    public interface IStyleAnalyzer
    {
        StyleProfile Analyze(IReadOnlyList<string> sentences);
        List<NlpNote> DetectAnomalies(StyleProfile sceneProfile, StyleProfile chapterProfile,
            string sceneId, string sceneTitle, string chapterTitle);
    }
}
