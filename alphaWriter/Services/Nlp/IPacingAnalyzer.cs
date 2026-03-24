using alphaWriter.Models.Analysis;

namespace alphaWriter.Services.Nlp
{
    public interface IPacingAnalyzer
    {
        PacingMetrics Analyze(IReadOnlyList<string> sentences, IReadOnlyList<string> paragraphs,
            int sceneWordCount, double chapterAverageWordCount);

        List<NlpNote> DetectIssues(PacingMetrics metrics,
            string sceneId, string sceneTitle, string chapterTitle);
    }
}
