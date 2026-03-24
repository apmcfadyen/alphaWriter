using alphaWriter.Services.Nlp;
using Xunit;

namespace alphaWriter.Tests;

public class NlpCacheServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly NlpCacheService _cache;

    public NlpCacheServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "alphawriter-test-cache-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
        _cache = new NlpCacheService(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void GetCachedEmbeddingsJson_NoCache_ReturnsNull()
    {
        var result = _cache.GetCachedEmbeddingsJson("book1", "scene1", "hash123");
        Assert.Null(result);
    }

    [Fact]
    public async Task SaveAndRetrieve_MatchingHash_ReturnsJson()
    {
        var json = """{"contentHash":"abc123","data":[1,2,3]}""";
        await _cache.SaveEmbeddingsJsonAsync("book1", "scene1", "abc123", json);

        var result = _cache.GetCachedEmbeddingsJson("book1", "scene1", "abc123");
        Assert.NotNull(result);
        Assert.Contains("abc123", result);
    }

    [Fact]
    public async Task SaveAndRetrieve_MismatchedHash_ReturnsNull()
    {
        var json = """{"contentHash":"abc123","data":[1,2,3]}""";
        await _cache.SaveEmbeddingsJsonAsync("book1", "scene1", "abc123", json);

        // Query with different hash
        var result = _cache.GetCachedEmbeddingsJson("book1", "scene1", "different_hash");
        Assert.Null(result);
    }

    [Fact]
    public async Task DeleteSceneCache_RemovesFile()
    {
        var json = """{"contentHash":"abc","data":[]}""";
        await _cache.SaveEmbeddingsJsonAsync("book1", "scene1", "abc", json);

        _cache.DeleteSceneCache("book1", "scene1");

        var result = _cache.GetCachedEmbeddingsJson("book1", "scene1", "abc");
        Assert.Null(result);
    }

    [Fact]
    public void ComputeContentHash_SameInput_SameHash()
    {
        var hash1 = NlpCacheService.ComputeHash("Hello world");
        var hash2 = NlpCacheService.ComputeHash("Hello world");
        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public void ComputeContentHash_DifferentInput_DifferentHash()
    {
        var hash1 = NlpCacheService.ComputeHash("Hello world");
        var hash2 = NlpCacheService.ComputeHash("Hello world!");
        Assert.NotEqual(hash1, hash2);
    }

    [Fact]
    public void ComputeContentHash_NullInput_DoesNotThrow()
    {
        var hash = NlpCacheService.ComputeHash(null!);
        Assert.NotNull(hash);
        Assert.NotEmpty(hash);
    }
}
