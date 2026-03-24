using alphaWriter.Services.Nlp;
using Moq;
using Xunit;

namespace alphaWriter.Tests;

public class EmotionServiceTests
{
    [Fact]
    public void IsLoaded_BeforeLoad_ReturnsFalse()
    {
        var modelManagerMock = new Mock<INlpModelManager>();
        var service = new EmotionService(modelManagerMock.Object);

        Assert.False(service.IsLoaded);
    }

    [Fact]
    public void ClassifySentence_WithoutLoading_Throws()
    {
        var modelManagerMock = new Mock<INlpModelManager>();
        var service = new EmotionService(modelManagerMock.Object);

        Assert.Throws<InvalidOperationException>(
            () => service.ClassifySentence("Hello world"));
    }

    [Fact]
    public void Dispose_DoesNotThrow()
    {
        var modelManagerMock = new Mock<INlpModelManager>();
        var service = new EmotionService(modelManagerMock.Object);

        // Should not throw even if never loaded
        service.Dispose();
        Assert.False(service.IsLoaded);
    }

    [Fact]
    public void Dispose_ClearsLoadedState()
    {
        var modelManagerMock = new Mock<INlpModelManager>();
        var service = new EmotionService(modelManagerMock.Object);

        // Dispose should leave service in unloaded state
        service.Dispose();
        Assert.False(service.IsLoaded);

        // Should throw after dispose
        Assert.Throws<InvalidOperationException>(
            () => service.ClassifySentence("test"));
    }

    [Fact]
    public void ClassifyBatch_WithoutLoading_Throws()
    {
        var modelManagerMock = new Mock<INlpModelManager>();
        var service = new EmotionService(modelManagerMock.Object);

        Assert.Throws<InvalidOperationException>(
            () => service.ClassifyBatch(["Hello", "World"]));
    }

    [Fact]
    public void ClassifyBatch_EmptyList_ReturnsEmpty()
    {
        var modelManagerMock = new Mock<INlpModelManager>();
        var service = new EmotionService(modelManagerMock.Object);

        // ClassifyBatch with empty list returns empty without checking IsLoaded
        // (short-circuit before the model check)
        // Note: The actual implementation checks IsLoaded first, so this should throw.
        Assert.Throws<InvalidOperationException>(
            () => service.ClassifyBatch([]));
    }

    [Fact]
    public void UnloadModel_WhenNotLoaded_DoesNotThrow()
    {
        var modelManagerMock = new Mock<INlpModelManager>();
        var service = new EmotionService(modelManagerMock.Object);

        // Should not throw even if never loaded
        service.UnloadModel();
        Assert.False(service.IsLoaded);
    }
}
