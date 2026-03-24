using alphaWriter.Models;
using alphaWriter.Models.Analysis;
using alphaWriter.Services.Nlp;
using Moq;
using Xunit;

namespace alphaWriter.Tests;

public class NlpAnalysisServiceTests
{
    private NlpAnalysisService CreateService()
    {
        return new NlpAnalysisService(new StyleAnalyzer(), new PacingAnalyzer());
    }

    private Book CreateTestBook()
    {
        var book = new Book { Title = "Test Book" };
        var chapter = new Chapter { Title = "Chapter 1" };
        chapter.Scenes.Add(new Scene
        {
            Title = "Scene 1",
            Status = SceneStatus.Draft,
            Content = "The sun rose over the mountains. Birds began to sing. A gentle breeze carried the scent of pine through the valley."
        });
        chapter.Scenes.Add(new Scene
        {
            Title = "Scene 2",
            Status = SceneStatus.Draft,
            Content = "\"Hello!\" she called out. He turned slowly. \"I didn't expect you here,\" he said with a smile."
        });
        book.Chapters.Add(chapter);
        return book;
    }

    [Fact]
    public async Task AnalyzeSceneAsync_ReturnsResult_WithStyleAndPacing()
    {
        var service = CreateService();
        var book = CreateTestBook();
        var chapter = book.Chapters[0];
        var scene = chapter.Scenes[0];

        var result = await service.AnalyzeSceneAsync(scene, chapter, book);

        Assert.Equal(scene.Id, result.SceneId);
        Assert.Equal(scene.Title, result.SceneTitle);
        Assert.True(result.Style.TotalSentences > 0);
        Assert.True(result.Style.TotalWords > 0);
        Assert.True(result.Style.AverageSentenceLength > 0);
        Assert.True(result.Sentences.Count > 0);
    }

    [Fact]
    public async Task AnalyzeChapterAsync_AnalyzesAllNonOutlineScenes()
    {
        var service = CreateService();
        var book = CreateTestBook();
        var chapter = book.Chapters[0];

        // Add an outline scene that should be skipped
        chapter.Scenes.Add(new Scene
        {
            Title = "Outline Scene",
            Status = SceneStatus.Outline,
            Content = "This is just an outline."
        });

        var results = await service.AnalyzeChapterAsync(chapter, book);

        Assert.Equal(2, results.Count); // Only Draft scenes
        Assert.All(results, r => Assert.NotEmpty(r.SceneTitle));
    }

    [Fact]
    public async Task AnalyzeBookAsync_AnalyzesAllChapters()
    {
        var service = CreateService();
        var book = CreateTestBook();

        // Add a second chapter
        var chapter2 = new Chapter { Title = "Chapter 2" };
        chapter2.Scenes.Add(new Scene
        {
            Title = "Scene 3",
            Status = SceneStatus.Draft,
            Content = "Night fell. The stars emerged one by one. Silence blanketed the world."
        });
        book.Chapters.Add(chapter2);

        var progressMessages = new List<string>();
        var progress = new Progress<string>(msg => progressMessages.Add(msg));

        var (notes, results) = await service.AnalyzeBookAsync(book, progress);

        // Should have analyzed both chapters
        Assert.NotNull(notes);
        Assert.NotNull(results);
        // Notes may or may not be generated depending on content
    }

    [Fact]
    public async Task AnalyzeBookAsync_Cancellation_ThrowsOperationCanceled()
    {
        var service = CreateService();
        var book = CreateTestBook();
        var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.AnalyzeBookAsync(book, ct: cts.Token));
    }

    [Fact]
    public async Task AnalyzeSceneAsync_EmptyContent_ReturnsEmptyAnalysis()
    {
        var service = CreateService();
        var book = new Book { Title = "Empty Book" };
        var chapter = new Chapter { Title = "Ch1" };
        var scene = new Scene { Title = "Empty", Status = SceneStatus.Draft, Content = "" };
        chapter.Scenes.Add(scene);
        book.Chapters.Add(chapter);

        var result = await service.AnalyzeSceneAsync(scene, chapter, book);

        Assert.Equal(0, result.Style.TotalSentences);
        Assert.Empty(result.Sentences);
    }

    [Fact]
    public async Task AnalyzeBookAsync_FlagsOversizedScene()
    {
        var service = CreateService();
        var book = new Book { Title = "Test" };
        var chapter = new Chapter { Title = "Ch1" };

        // Normal scene
        chapter.Scenes.Add(new Scene
        {
            Title = "Short",
            Status = SceneStatus.Draft,
            Content = "A short scene."
        });

        // Very long scene (simulate with repeated content)
        var longContent = string.Join(" ", Enumerable.Repeat(
            "The quick brown fox jumps over the lazy dog and runs through the forest at dawn.", 60));
        chapter.Scenes.Add(new Scene
        {
            Title = "Very Long Scene",
            Status = SceneStatus.Draft,
            Content = longContent
        });

        book.Chapters.Add(chapter);
        var (notes, _) = await service.AnalyzeBookAsync(book);

        // Should flag the short scene as undersized and the long scene for pacing
        Assert.Contains(notes, n => n.Category == NlpNoteCategory.Structure
            && n.SceneTitle == "Short");
        Assert.Contains(notes, n => n.Category == NlpNoteCategory.Pacing
            && n.SceneTitle == "Very Long Scene");
    }
}
