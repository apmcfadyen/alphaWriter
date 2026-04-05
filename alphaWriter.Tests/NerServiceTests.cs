using alphaWriter.Models;
using alphaWriter.Services.Nlp;
using Moq;
using Xunit;

namespace alphaWriter.Tests;

public class NerServiceTests
{
    [Fact]
    public void IsLoaded_BeforeLoad_ReturnsFalse()
    {
        var svc = new NerService();
        Assert.False(svc.IsLoaded);
    }

    [Fact]
    public void ExtractEntities_WithoutLoading_Throws()
    {
        var svc = new NerService();
        Assert.Throws<InvalidOperationException>(() =>
            svc.ExtractEntities(["The quick brown fox."]));
    }

    [Fact]
    public void UnloadModel_WhenNotLoaded_DoesNotThrow()
    {
        var svc = new NerService();
        var ex = Record.Exception(() => svc.UnloadModel());
        Assert.Null(ex);
    }

    [Fact]
    public void Dispose_DoesNotThrow()
    {
        var svc = new NerService();
        var ex = Record.Exception(() => svc.Dispose());
        Assert.Null(ex);
    }
}
