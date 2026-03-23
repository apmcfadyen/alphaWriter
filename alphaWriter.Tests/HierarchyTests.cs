using alphaWriter.Models;
using Xunit;

namespace alphaWriter.Tests;

/// <summary>
/// Tests for the Chapter→Book word count propagation chain and computed
/// Book properties (progress, summary, target).
/// </summary>
public class HierarchyTests
{
    // ── Chapter.WordCount ─────────────────────────────────────────────────────

    [Fact]
    public void Chapter_WordCount_ZeroWithNoScenes()
    {
        var chapter = new Chapter();
        Assert.Equal(0, chapter.WordCount);
    }

    [Fact]
    public void Chapter_WordCount_ExcludesOutlineScene()
    {
        var chapter = new Chapter();
        var outline = new Scene { Status = SceneStatus.Outline };
        outline.Content = "five words in this scene";
        chapter.Scenes.Add(outline);
        Assert.Equal(0, chapter.WordCount);
    }

    [Fact]
    public void Chapter_WordCount_IncludesDraftScene()
    {
        var chapter = new Chapter();
        var scene = new Scene { Status = SceneStatus.Draft };
        scene.Content = "one two three";
        chapter.Scenes.Add(scene);
        Assert.Equal(3, chapter.WordCount);
    }

    [Fact]
    public void Chapter_WordCount_IncludesFirstEditScene()
    {
        var chapter = new Chapter();
        var scene = new Scene { Status = SceneStatus.FirstEdit };
        scene.Content = "hello world";
        chapter.Scenes.Add(scene);
        Assert.Equal(2, chapter.WordCount);
    }

    [Fact]
    public void Chapter_WordCount_IncludesSecondEditScene()
    {
        var chapter = new Chapter();
        var scene = new Scene { Status = SceneStatus.SecondEdit };
        scene.Content = "a b c d";
        chapter.Scenes.Add(scene);
        Assert.Equal(4, chapter.WordCount);
    }

    [Fact]
    public void Chapter_WordCount_IncludesDoneScene()
    {
        var chapter = new Chapter();
        var scene = new Scene { Status = SceneStatus.Done };
        scene.Content = "x y z";
        chapter.Scenes.Add(scene);
        Assert.Equal(3, chapter.WordCount);
    }

    [Fact]
    public void Chapter_WordCount_SumsMultipleScenesExcludingOutline()
    {
        var chapter = new Chapter();
        var draft = new Scene { Status = SceneStatus.Draft };
        draft.Content = "one two";
        var done = new Scene { Status = SceneStatus.Done };
        done.Content = "three four five";
        var outline = new Scene { Status = SceneStatus.Outline };
        outline.Content = "ignored words here";
        chapter.Scenes.Add(draft);
        chapter.Scenes.Add(done);
        chapter.Scenes.Add(outline);
        Assert.Equal(5, chapter.WordCount);
    }

    [Fact]
    public void Chapter_WordCount_UpdatesWhenSceneContentChanges()
    {
        var chapter = new Chapter();
        var scene = new Scene { Status = SceneStatus.Draft };
        scene.Content = "one two";
        chapter.Scenes.Add(scene);
        Assert.Equal(2, chapter.WordCount);

        scene.Content = "one two three four";
        Assert.Equal(4, chapter.WordCount);
    }

    [Fact]
    public void Chapter_WordCount_UpdatesWhenSceneStatusChangesToOutline()
    {
        var chapter = new Chapter();
        var scene = new Scene { Status = SceneStatus.Draft };
        scene.Content = "one two three";
        chapter.Scenes.Add(scene);
        Assert.Equal(3, chapter.WordCount);

        scene.Status = SceneStatus.Outline;
        Assert.Equal(0, chapter.WordCount);
    }

    [Fact]
    public void Chapter_WordCount_UpdatesWhenSceneStatusChangesFromOutline()
    {
        var chapter = new Chapter();
        var scene = new Scene { Status = SceneStatus.Outline };
        scene.Content = "one two three";
        chapter.Scenes.Add(scene);
        Assert.Equal(0, chapter.WordCount);

        scene.Status = SceneStatus.Draft;
        Assert.Equal(3, chapter.WordCount);
    }

    [Fact]
    public void Chapter_WordCount_UpdatesWhenSceneAdded()
    {
        var chapter = new Chapter();
        var scene1 = new Scene { Status = SceneStatus.Draft };
        scene1.Content = "one two";
        chapter.Scenes.Add(scene1);
        Assert.Equal(2, chapter.WordCount);

        var scene2 = new Scene { Status = SceneStatus.Done };
        scene2.Content = "three";
        chapter.Scenes.Add(scene2);
        Assert.Equal(3, chapter.WordCount);
    }

    [Fact]
    public void Chapter_WordCount_UpdatesWhenSceneRemoved()
    {
        var chapter = new Chapter();
        var scene1 = new Scene { Status = SceneStatus.Draft };
        scene1.Content = "one two";
        var scene2 = new Scene { Status = SceneStatus.Done };
        scene2.Content = "three four five";
        chapter.Scenes.Add(scene1);
        chapter.Scenes.Add(scene2);
        Assert.Equal(5, chapter.WordCount);

        chapter.Scenes.Remove(scene2);
        Assert.Equal(2, chapter.WordCount);
    }

    [Fact]
    public void Chapter_PropertyChanged_WordCountFiredOnSceneContentChange()
    {
        var chapter = new Chapter();
        var scene = new Scene { Status = SceneStatus.Draft };
        scene.Content = "hello";
        chapter.Scenes.Add(scene);

        var fired = new List<string>();
        chapter.PropertyChanged += (_, e) => fired.Add(e.PropertyName ?? "");
        scene.Content = "hello world";
        Assert.Contains("WordCount", fired);
    }

    // ── Book.WordCount ────────────────────────────────────────────────────────

    [Fact]
    public void Book_WordCount_ZeroWithNoChapters()
    {
        var book = new Book();
        Assert.Equal(0, book.WordCount);
    }

    [Fact]
    public void Book_WordCount_SumsChapters()
    {
        var book = new Book();
        var ch1 = new Chapter();
        var s1 = new Scene { Status = SceneStatus.Draft };
        s1.Content = "one two";
        ch1.Scenes.Add(s1);

        var ch2 = new Chapter();
        var s2 = new Scene { Status = SceneStatus.Done };
        s2.Content = "three four five";
        ch2.Scenes.Add(s2);

        book.Chapters.Add(ch1);
        book.Chapters.Add(ch2);

        Assert.Equal(5, book.WordCount);
    }

    [Fact]
    public void Book_WordCount_UpdatesWhenChapterAdded()
    {
        var book = new Book();
        var ch = new Chapter();
        var s = new Scene { Status = SceneStatus.Draft };
        s.Content = "a b c";
        ch.Scenes.Add(s);
        Assert.Equal(0, book.WordCount);

        book.Chapters.Add(ch);
        Assert.Equal(3, book.WordCount);
    }

    [Fact]
    public void Book_WordCount_UpdatesWhenChapterRemoved()
    {
        var book = new Book();
        var ch = new Chapter();
        var s = new Scene { Status = SceneStatus.Draft };
        s.Content = "x y z";
        ch.Scenes.Add(s);
        book.Chapters.Add(ch);
        Assert.Equal(3, book.WordCount);

        book.Chapters.Remove(ch);
        Assert.Equal(0, book.WordCount);
    }

    [Fact]
    public void Book_WordCount_UpdatesWhenSceneContentChanges()
    {
        var book = new Book();
        var ch = new Chapter();
        var s = new Scene { Status = SceneStatus.Draft };
        s.Content = "one two";
        ch.Scenes.Add(s);
        book.Chapters.Add(ch);
        Assert.Equal(2, book.WordCount);

        s.Content = "one two three four";
        Assert.Equal(4, book.WordCount);
    }

    // ── Book.HasWordTarget ────────────────────────────────────────────────────

    [Fact]
    public void Book_HasWordTarget_FalseWhenZero()
    {
        var book = new Book { WordTarget = 0 };
        Assert.False(book.HasWordTarget);
    }

    [Fact]
    public void Book_HasWordTarget_TrueWhenPositive()
    {
        var book = new Book { WordTarget = 80_000 };
        Assert.True(book.HasWordTarget);
    }

    // ── Book.WordCountProgress ────────────────────────────────────────────────

    [Fact]
    public void Book_WordCountProgress_ZeroWithNoTarget()
    {
        var book = new Book { WordTarget = 0 };
        Assert.Equal(0.0, book.WordCountProgress);
    }

    [Fact]
    public void Book_WordCountProgress_HalfwayThere()
    {
        var book = new Book { WordTarget = 100 };
        var ch = new Chapter();
        // Add 50 words
        for (int i = 0; i < 5; i++)
        {
            var s = new Scene { Status = SceneStatus.Draft };
            s.Content = "a b c d e f g h i j"; // 10 words
            ch.Scenes.Add(s);
        }
        book.Chapters.Add(ch);
        Assert.Equal(0.5, book.WordCountProgress, precision: 4);
    }

    [Fact]
    public void Book_WordCountProgress_ClampedAtOne()
    {
        var book = new Book { WordTarget = 1 };
        var ch = new Chapter();
        var s = new Scene { Status = SceneStatus.Draft };
        s.Content = "one two three four five";
        ch.Scenes.Add(s);
        book.Chapters.Add(ch);
        Assert.Equal(1.0, book.WordCountProgress);
    }

    // ── Book.WordCountSummary ─────────────────────────────────────────────────

    [Fact]
    public void Book_WordCountSummary_NoTarget_ShowsWordCountOnly()
    {
        var book = new Book { WordTarget = 0 };
        // 0 words, no target
        Assert.Equal("0 words", book.WordCountSummary);
    }

    [Fact]
    public void Book_WordCountSummary_WithTarget_ShowsProgress()
    {
        var book = new Book { WordTarget = 100 };
        // 0 words so far
        var summary = book.WordCountSummary;
        Assert.Contains("100", summary);
        Assert.Contains("words", summary);
        Assert.Contains("%", summary);
    }

    [Fact]
    public void Book_PropertyChanged_WordCountFiredWhenChapterChanges()
    {
        var book = new Book();
        var ch = new Chapter();
        var s = new Scene { Status = SceneStatus.Draft };
        s.Content = "hello";
        ch.Scenes.Add(s);
        book.Chapters.Add(ch);

        var fired = new List<string>();
        book.PropertyChanged += (_, e) => fired.Add(e.PropertyName ?? "");
        s.Content = "hello world";
        Assert.Contains("WordCount", fired);
    }
}
