using alphaWriter.Services.Nlp;
using Xunit;

namespace alphaWriter.Tests;

public class NlpModelManagerTests : IDisposable
{
    private readonly string _tempDir;
    private readonly NlpModelManager _manager;

    public NlpModelManagerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "alphawriter-test-models-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
        _manager = new NlpModelManager(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void AreModelsAvailable_EmptyDir_ReturnsFalse()
    {
        Assert.False(_manager.AreModelsAvailable);
        Assert.False(_manager.IsEmbeddingModelAvailable);
        Assert.False(_manager.IsEmotionModelAvailable);
    }

    [Fact]
    public void IsEmbeddingModelAvailable_WithFiles_ReturnsTrue()
    {
        var modelDir = Path.Combine(_tempDir, NlpModelManager.EmbeddingModelName);
        Directory.CreateDirectory(modelDir);
        File.WriteAllText(Path.Combine(modelDir, "model.onnx"), "dummy");
        File.WriteAllText(Path.Combine(modelDir, "vocab.txt"), "dummy");

        Assert.True(_manager.IsEmbeddingModelAvailable);
    }

    [Fact]
    public void IsEmbeddingModelAvailable_MissingFile_ReturnsFalse()
    {
        var modelDir = Path.Combine(_tempDir, NlpModelManager.EmbeddingModelName);
        Directory.CreateDirectory(modelDir);
        File.WriteAllText(Path.Combine(modelDir, "model.onnx"), "dummy");
        // vocab.txt is missing

        Assert.False(_manager.IsEmbeddingModelAvailable);
    }

    [Fact]
    public void GetModelPath_ReturnsCorrectPath()
    {
        var path = _manager.GetModelPath(NlpModelManager.EmbeddingModelName);
        Assert.EndsWith("model.onnx", path);
        Assert.Contains(NlpModelManager.EmbeddingModelName, path);
    }

    [Fact]
    public void GetTokenizerPath_EmbeddingModel_ReturnsVocabTxt()
    {
        var path = _manager.GetTokenizerPath(NlpModelManager.EmbeddingModelName);
        Assert.EndsWith("vocab.txt", path);
    }

    [Fact]
    public void GetTokenizerPath_EmotionModel_ReturnsVocabJson()
    {
        var path = _manager.GetTokenizerPath(NlpModelManager.EmotionModelName);
        Assert.EndsWith("vocab.json", path);
    }

    [Fact]
    public void AreModelsAvailable_BothModelsPresent_ReturnsTrue()
    {
        // Create embedding model files
        var embDir = Path.Combine(_tempDir, NlpModelManager.EmbeddingModelName);
        Directory.CreateDirectory(embDir);
        File.WriteAllText(Path.Combine(embDir, "model.onnx"), "dummy");
        File.WriteAllText(Path.Combine(embDir, "vocab.txt"), "dummy");

        // Create emotion model files
        var emoDir = Path.Combine(_tempDir, NlpModelManager.EmotionModelName);
        Directory.CreateDirectory(emoDir);
        File.WriteAllText(Path.Combine(emoDir, "model.onnx"), "dummy");
        File.WriteAllText(Path.Combine(emoDir, "vocab.json"), "dummy");
        File.WriteAllText(Path.Combine(emoDir, "merges.txt"), "dummy");

        Assert.True(_manager.AreModelsAvailable);
    }
}
