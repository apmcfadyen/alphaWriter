using alphaWriter.Services.Nlp;
using Xunit;

namespace alphaWriter.Tests;

public class EmbeddingServiceTests
{
    [Fact]
    public void CosineSimilarity_IdenticalVectors_ReturnsOne()
    {
        float[] a = [1f, 2f, 3f];
        float[] b = [1f, 2f, 3f];
        float sim = EmbeddingService.CosineSimilarityStatic(a, b);
        Assert.Equal(1.0f, sim, precision: 4);
    }

    [Fact]
    public void CosineSimilarity_OrthogonalVectors_ReturnsZero()
    {
        float[] a = [1f, 0f, 0f];
        float[] b = [0f, 1f, 0f];
        float sim = EmbeddingService.CosineSimilarityStatic(a, b);
        Assert.Equal(0.0f, sim, precision: 4);
    }

    [Fact]
    public void CosineSimilarity_OppositeVectors_ReturnsNegativeOne()
    {
        float[] a = [1f, 0f, 0f];
        float[] b = [-1f, 0f, 0f];
        float sim = EmbeddingService.CosineSimilarityStatic(a, b);
        Assert.Equal(-1.0f, sim, precision: 4);
    }

    [Fact]
    public void CosineSimilarity_SimilarVectors_HighSimilarity()
    {
        float[] a = [1f, 2f, 3f, 4f];
        float[] b = [1.1f, 2.1f, 3.1f, 4.1f];
        float sim = EmbeddingService.CosineSimilarityStatic(a, b);
        Assert.True(sim > 0.99f);
    }

    [Fact]
    public void CosineSimilarity_ZeroVector_ReturnsZero()
    {
        float[] a = [0f, 0f, 0f];
        float[] b = [1f, 2f, 3f];
        float sim = EmbeddingService.CosineSimilarityStatic(a, b);
        Assert.Equal(0.0f, sim);
    }

    [Fact]
    public void CosineSimilarity_DifferentLengths_Throws()
    {
        float[] a = [1f, 2f];
        float[] b = [1f, 2f, 3f];
        Assert.Throws<ArgumentException>(() => EmbeddingService.CosineSimilarityStatic(a, b));
    }

    [Fact]
    public void ComputeEmbedding_WithoutLoading_Throws()
    {
        var mockManager = new Moq.Mock<INlpModelManager>();
        var service = new EmbeddingService(mockManager.Object);
        Assert.Throws<InvalidOperationException>(() => service.ComputeEmbedding("test"));
    }

    [Fact]
    public void ComputeEmbeddings_WithoutLoading_Throws()
    {
        var mockManager = new Moq.Mock<INlpModelManager>();
        var service = new EmbeddingService(mockManager.Object);
        Assert.Throws<InvalidOperationException>(() => service.ComputeEmbeddings(["test1", "test2"]));
    }

    [Fact]
    public void UnloadModel_WhenNotLoaded_DoesNotThrow()
    {
        var mockManager = new Moq.Mock<INlpModelManager>();
        var service = new EmbeddingService(mockManager.Object);

        // Should not throw even if never loaded
        service.UnloadModel();
        Assert.False(service.IsLoaded);
    }
}
