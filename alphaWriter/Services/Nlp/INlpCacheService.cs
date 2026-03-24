using System.Threading.Tasks;

namespace alphaWriter.Services.Nlp
{
    public interface INlpCacheService
    {
        string? GetCachedEmbeddingsJson(string bookId, string sceneId, string contentHash);
        Task SaveEmbeddingsJsonAsync(string bookId, string sceneId, string contentHash, string json);
        void DeleteSceneCache(string bookId, string sceneId);
        static string ComputeContentHash(string content) => NlpCacheService.ComputeHash(content);
    }
}
