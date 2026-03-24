using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace alphaWriter.Services.Nlp
{
    public class NlpModelManager : INlpModelManager
    {
        public const string EmbeddingModelName = "all-MiniLM-L6-v2";
        public const string EmotionModelName = "roberta-go-emotions";

        private static readonly (string name, string[] files)[] ModelDefinitions =
        [
            (EmbeddingModelName, [
                "model.onnx",
                "vocab.txt"
            ]),
            (EmotionModelName, [
                "model.onnx",
                "vocab.json",
                "merges.txt"
            ])
        ];

        // HuggingFace CDN URLs for each model file
        private static readonly Dictionary<string, string> FileUrls = new()
        {
            // all-MiniLM-L6-v2 (ONNX export)
            [$"{EmbeddingModelName}/model.onnx"] =
                "https://huggingface.co/sentence-transformers/all-MiniLM-L6-v2/resolve/main/onnx/model.onnx",
            [$"{EmbeddingModelName}/vocab.txt"] =
                "https://huggingface.co/sentence-transformers/all-MiniLM-L6-v2/resolve/main/vocab.txt",

            // SamLowe/roberta-base-go_emotions-onnx (official ONNX export by same author)
            [$"{EmotionModelName}/model.onnx"] =
                "https://huggingface.co/SamLowe/roberta-base-go_emotions-onnx/resolve/main/onnx/model.onnx",
            [$"{EmotionModelName}/vocab.json"] =
                "https://huggingface.co/SamLowe/roberta-base-go_emotions-onnx/resolve/main/vocab.json",
            [$"{EmotionModelName}/merges.txt"] =
                "https://huggingface.co/SamLowe/roberta-base-go_emotions-onnx/resolve/main/merges.txt"
        };

        private readonly string _modelsDir;
        private readonly HttpClient _httpClient;

        public NlpModelManager()
        {
            _modelsDir = Path.Combine(FileSystem.AppDataDirectory, "models");
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("alphaWriter/1.0");
        }

        // Constructor for testing with custom path
        internal NlpModelManager(string modelsDirectory)
        {
            _modelsDir = modelsDirectory;
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("alphaWriter/1.0");
        }

        public string ModelsDirectory => _modelsDir;

        public bool AreModelsAvailable => IsEmbeddingModelAvailable && IsEmotionModelAvailable;

        public bool IsEmbeddingModelAvailable => IsModelAvailable(EmbeddingModelName);

        public bool IsEmotionModelAvailable => IsModelAvailable(EmotionModelName);

        public string GetModelPath(string modelName) =>
            Path.Combine(_modelsDir, modelName, "model.onnx");

        public string GetTokenizerPath(string modelName) =>
            modelName == EmbeddingModelName
                ? Path.Combine(_modelsDir, modelName, "vocab.txt")
                : Path.Combine(_modelsDir, modelName, "vocab.json");

        public string GetMergesPath(string modelName) =>
            Path.Combine(_modelsDir, modelName, "merges.txt");

        public async Task DownloadModelsAsync(IProgress<(string model, double percent)>? progress = null,
            CancellationToken ct = default)
        {
            foreach (var (modelName, files) in ModelDefinitions)
            {
                var modelDir = Path.Combine(_modelsDir, modelName);
                Directory.CreateDirectory(modelDir);

                for (int i = 0; i < files.Length; i++)
                {
                    ct.ThrowIfCancellationRequested();

                    var fileName = files[i];
                    var filePath = Path.Combine(modelDir, fileName);

                    if (File.Exists(filePath))
                    {
                        progress?.Report((modelName, (double)(i + 1) / files.Length * 100));
                        continue;
                    }

                    var urlKey = $"{modelName}/{fileName}";
                    if (!FileUrls.TryGetValue(urlKey, out var url))
                        throw new InvalidOperationException($"No download URL configured for {urlKey}");

                    progress?.Report((modelName, (double)i / files.Length * 100));

                    await DownloadFileAsync(url, filePath, ct);

                    progress?.Report((modelName, (double)(i + 1) / files.Length * 100));
                }
            }
        }

        private async Task DownloadFileAsync(string url, string filePath, CancellationToken ct)
        {
            var tmpPath = filePath + ".tmp";
            try
            {
                using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
                response.EnsureSuccessStatusCode();

                await using var stream = await response.Content.ReadAsStreamAsync(ct);
                await using var fileStream = new FileStream(tmpPath, FileMode.Create, FileAccess.Write, FileShare.None);
                await stream.CopyToAsync(fileStream, ct);
            }
            catch
            {
                // Clean up partial download
                if (File.Exists(tmpPath))
                    File.Delete(tmpPath);
                throw;
            }

            File.Move(tmpPath, filePath, overwrite: true);
        }

        private bool IsModelAvailable(string modelName)
        {
            var def = Array.Find(ModelDefinitions, d => d.name == modelName);
            if (def.files is null) return false;

            foreach (var file in def.files)
            {
                if (!File.Exists(Path.Combine(_modelsDir, modelName, file)))
                    return false;
            }
            return true;
        }
    }
}
