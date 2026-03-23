using alphaWriter.Models;
using Xunit;

namespace alphaWriter.Tests;

public class SceneTests
{
    // ── WordCount via Content setter ──────────────────────────────────────────

    [Fact]
    public void WordCount_EmptyContent_IsZero()
    {
        var scene = new Scene();
        scene.Content = string.Empty;
        Assert.Equal(0, scene.WordCount);
    }

    [Fact]
    public void WordCount_WhitespaceContent_IsZero()
    {
        var scene = new Scene();
        scene.Content = "   \t\n  ";
        Assert.Equal(0, scene.WordCount);
    }

    [Fact]
    public void WordCount_PlainTextFiveWords_IsFive()
    {
        var scene = new Scene();
        scene.Content = "one two three four five";
        Assert.Equal(5, scene.WordCount);
    }

    [Fact]
    public void WordCount_HtmlStripped_CountsPlainWords()
    {
        var scene = new Scene();
        scene.Content = "<p>Hello <b>world</b></p>";
        Assert.Equal(2, scene.WordCount);
    }

    [Fact]
    public void WordCount_HtmlEntity_DecodedAndCounted()
    {
        var scene = new Scene();
        // &nbsp; decodes to a space, so the two words are separated correctly
        scene.Content = "foo&nbsp;bar";
        Assert.Equal(2, scene.WordCount);
    }

    [Fact]
    public void WordCount_PunctuationAttached_WordStillCounted()
    {
        var scene = new Scene();
        scene.Content = "Hello, world.";
        // Both tokens clean to "hello" and "world" → 2 words (ComputeWordCount
        // uses a simpler split, ExtractWords uses CleanWord – but WordCount uses
        // the same pipeline minus the CleanWord step. The raw split still produces
        // two non-empty tokens, so count = 2.)
        Assert.Equal(2, scene.WordCount);
    }

    [Fact]
    public void WordCount_BlockTagsSplitWords()
    {
        // Without block-tag → newline conversion "foo" and "bar" inside a <div>
        // would be concatenated. Verify they are NOT merged.
        var scene = new Scene();
        scene.Content = "<div>foo</div><div>bar</div>";
        Assert.Equal(2, scene.WordCount);
    }

    [Fact]
    public void WordCount_LineCommentStripped()
    {
        var scene = new Scene();
        // After StripHtml the text is "\ncomment text\n". The "// comment" line
        // is stripped, leaving only "text".
        scene.Content = "<div>text</div><div>// comment</div>";
        Assert.Equal(1, scene.WordCount);
    }

    [Fact]
    public void WordCount_BlockCommentStripped()
    {
        var scene = new Scene();
        scene.Content = "before /* hidden words */ after";
        Assert.Equal(2, scene.WordCount);
    }

    [Fact]
    public void WordCount_UpdatesWhenContentSetAgain()
    {
        var scene = new Scene();
        scene.Content = "one two three";
        Assert.Equal(3, scene.WordCount);
        scene.Content = "just one";
        Assert.Equal(2, scene.WordCount);
    }

    [Fact]
    public void WordCount_PropertyChangedFiredOnContentChange()
    {
        var scene = new Scene();
        var fired = new List<string>();
        scene.PropertyChanged += (_, e) => fired.Add(e.PropertyName ?? "");
        scene.Content = "hello world";
        Assert.Contains("WordCount", fired);
        Assert.Contains("Content", fired);
    }

    [Fact]
    public void WordCount_SameContentAssigned_NoEventFired()
    {
        var scene = new Scene();
        scene.Content = "hello";
        var fired = new List<string>();
        scene.PropertyChanged += (_, e) => fired.Add(e.PropertyName ?? "");
        // Assigning identical value should be a no-op
        scene.Content = "hello";
        Assert.Empty(fired);
    }

    // ── ExtractWords static method ────────────────────────────────────────────

    [Fact]
    public void ExtractWords_NullInput_ReturnsEmpty()
    {
        var result = Scene.ExtractWords(null!);
        Assert.Empty(result);
    }

    [Fact]
    public void ExtractWords_WhitespaceInput_ReturnsEmpty()
    {
        var result = Scene.ExtractWords("   ");
        Assert.Empty(result);
    }

    [Fact]
    public void ExtractWords_PlainWords_LowercaseAndCleaned()
    {
        var result = Scene.ExtractWords("Hello World");
        Assert.Equal(new[] { "hello", "world" }, result);
    }

    [Fact]
    public void ExtractWords_TrailingPunctuation_Stripped()
    {
        var result = Scene.ExtractWords("Hello, world!");
        Assert.Equal(new[] { "hello", "world" }, result);
    }

    [Fact]
    public void ExtractWords_HtmlTags_Removed()
    {
        var result = Scene.ExtractWords("<p>Alpha <b>Beta</b></p>");
        Assert.Equal(new[] { "alpha", "beta" }, result);
    }

    [Fact]
    public void ExtractWords_BlockTagEmitsNewline_KeepsWordsSeparate()
    {
        // <div>foo</div><div>bar</div> → "\nfoo\n\nbar\n" after StripHtml
        var result = Scene.ExtractWords("<div>foo</div><div>bar</div>");
        Assert.Equal(new[] { "foo", "bar" }, result);
    }

    [Fact]
    public void ExtractWords_HtmlEntities_Decoded()
    {
        var result = Scene.ExtractWords("foo&amp;bar &lt;tag&gt;");
        // "foo&bar" and "<tag>" — CleanWord strips leading '<' and trailing '>'
        // leaving "tag"; "foo&bar" stays as "foo&bar" (& is middle, not stripped)
        Assert.Contains("tag", result);
        Assert.DoesNotContain("<tag>", result);
    }

    [Fact]
    public void ExtractWords_LineComment_Excluded()
    {
        var result = Scene.ExtractWords("<div>real</div><div>// ignored word</div>");
        Assert.Equal(new[] { "real" }, result);
    }

    [Fact]
    public void ExtractWords_BlockComment_Excluded()
    {
        var result = Scene.ExtractWords("start /* hidden */ end");
        Assert.Equal(new[] { "start", "end" }, result);
    }

    [Fact]
    public void ExtractWords_UnicodeEscape_Decoded()
    {
        // \u0048 = 'H', \u0069 = 'i'
        var result = Scene.ExtractWords(@"\u0048\u0069");
        Assert.Equal(new[] { "hi" }, result);
    }

    [Fact]
    public void ExtractWords_EmptyTagOnly_ReturnsEmpty()
    {
        var result = Scene.ExtractWords("<br/>");
        Assert.Empty(result);
    }
}
