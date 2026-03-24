using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace alphaWriter.Services.Nlp
{
    public class NlpCacheService : INlpCacheService
    {
        private readonly string _cacheDir;

        public NlpCacheService()
        {
            _cacheDir = Path.Combine(FileSystem.AppDataDirectory, "nlp-cache");
        }

        // Constructor for testing
        internal NlpCacheService(string cacheDirectory)
        {
            _cacheDir = cacheDirectory;
        }

        public string? GetCachedEmbeddingsJson(string bookId, string sceneId, string contentHash)
        {
            var path = GetCachePath(bookId, sceneId);
            if (!File.Exists(path)) return null;

            try
            {
                var json = File.ReadAllText(path);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (root.TryGetProperty("contentHash", out var hashProp)
                    && hashProp.GetString() == contentHash)
                {
                    return json;
                }
                // Hash mismatch — stale cache
                return null;
            }
            catch
            {
                return null;
            }
        }

        public async Task SaveEmbeddingsJsonAsync(string bookId, string sceneId, string contentHash, string json)
        {
            var dir = Path.Combine(_cacheDir, bookId);
            Directory.CreateDirectory(dir);

            var path = GetCachePath(bookId, sceneId);
            var tmpPath = path + ".tmp";
            await File.WriteAllTextAsync(tmpPath, json);
            File.Move(tmpPath, path, overwrite: true);
        }

        public void DeleteSceneCache(string bookId, string sceneId)
        {
            var path = GetCachePath(bookId, sceneId);
            if (File.Exists(path))
                File.Delete(path);
        }

        private string GetCachePath(string bookId, string sceneId)
            => Path.Combine(_cacheDir, bookId, $"{sceneId}.json");

        internal static string ComputeHash(string content)
        {
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(content ?? string.Empty));
            return Convert.ToHexStringLower(bytes);
        }
    }
}
