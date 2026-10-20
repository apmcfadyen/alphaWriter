using alphaWriter.Models.Analysis;
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

        /// <summary>Bumped whenever persisted analysis changes shape (spans, rule ids).</summary>
        public const int AnalysisFormatVersion = 2;

        // IncludeFields: per-sentence emotions are (Label, Confidence) value tuples,
        // whose members are fields — without this they persist as empty objects.
        private static readonly JsonSerializerOptions AnalysisJsonOptions = new()
        {
            WriteIndented = true,
            IncludeFields = true
        };

        public NlpCacheService()
        {
            _cacheDir = Path.Combine(AppPaths.DataDirectory, "nlp-cache");
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

        public PersistedAnalysisData? LoadAnalysisResults(string bookId)
        {
            var path = GetAnalysisPath(bookId);
            if (!File.Exists(path)) return null;

            try
            {
                var json = File.ReadAllText(path);
                var data = JsonSerializer.Deserialize<PersistedAnalysisData>(json, AnalysisJsonOptions);
                // Older formats carry sentence indices from a different splitter and no
                // spans; showing them would misplace highlights. Re-run analysis instead.
                return data?.FormatVersion == AnalysisFormatVersion ? data : null;
            }
            catch
            {
                return null;
            }
        }

        public async Task SaveAnalysisResultsAsync(string bookId, PersistedAnalysisData data)
        {
            var dir = Path.Combine(_cacheDir, bookId);
            Directory.CreateDirectory(dir);

            var path = GetAnalysisPath(bookId);
            var tmpPath = path + ".tmp";
            data.FormatVersion = AnalysisFormatVersion;
            var json = JsonSerializer.Serialize(data, AnalysisJsonOptions);
            await File.WriteAllTextAsync(tmpPath, json);
            File.Move(tmpPath, path, overwrite: true);
        }

        private string GetAnalysisPath(string bookId)
            => Path.Combine(_cacheDir, bookId, "analysis-results.json");

        private string GetCachePath(string bookId, string sceneId)
            => Path.Combine(_cacheDir, bookId, $"{sceneId}.json");

        internal static string ComputeHash(string content)
        {
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(content ?? string.Empty));
            return Convert.ToHexStringLower(bytes);
        }
    }
}
