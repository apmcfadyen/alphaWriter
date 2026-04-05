using alphaWriter.Models.Analysis;
using System.Threading.Tasks;

namespace alphaWriter.Services.Nlp
{
    public interface INlpCacheService
    {
        string? GetCachedEmbeddingsJson(string bookId, string sceneId, string contentHash);
        Task SaveEmbeddingsJsonAsync(string bookId, string sceneId, string contentHash, string json);
        void DeleteSceneCache(string bookId, string sceneId);
        PersistedAnalysisData? LoadAnalysisResults(string bookId);
        Task SaveAnalysisResultsAsync(string bookId, PersistedAnalysisData data);
        static string ComputeContentHash(string content) => NlpCacheService.ComputeHash(content);
    }
}
