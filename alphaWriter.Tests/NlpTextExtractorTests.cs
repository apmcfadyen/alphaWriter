using alphaWriter.Services.Nlp;
using Xunit;

namespace alphaWriter.Tests;

public class NlpTextExtractorTests
{
    // ── ExtractPlainText ────────────────────────────────────────────────────

    [Fact]
    public void ExtractPlainText_EmptyInput_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, NlpTextExtractor.ExtractPlainText(""));
        Assert.Equal(string.Empty, NlpTextExtractor.ExtractPlainText(null!));
    }

    [Fact]
    public void ExtractPlainText_StripsHtmlTags()
    {
        var result = NlpTextExtractor.ExtractPlainText("<p>Hello <b>world</b></p>");
        Assert.Contains("Hello", result);
        Assert.Contains("world", result);
        Assert.DoesNotContain("<", result);
    }

    [Fact]
    public void ExtractPlainText_StripsComments()
    {
        var result = NlpTextExtractor.ExtractPlainText("visible text // hidden comment");
        Assert.Contains("visible text", result);
        Assert.DoesNotContain("hidden", result);
    }

    [Fact]
    public void ExtractPlainText_DecodesEntities()
    {
        var result = NlpTextExtractor.ExtractPlainText("foo&amp;bar");
        Assert.Contains("foo&bar", result);
    }

    // ── SplitSentences ──────────────────────────────────────────────────────

    [Fact]
    public void SplitSentences_EmptyInput_ReturnsEmptyList()
    {
        Assert.Empty(NlpTextExtractor.SplitSentences(""));
        Assert.Empty(NlpTextExtractor.SplitSentences(null!));
    }

    [Fact]
    public void SplitSentences_SingleSentence_ReturnsSingle()
    {
        var result = NlpTextExtractor.SplitSentences("The cat sat on the mat.");
        Assert.Single(result);
        Assert.Equal("The cat sat on the mat.", result[0]);
    }

    [Fact]
    public void SplitSentences_TwoSentences_SplitsCorrectly()
    {
        var result = NlpTextExtractor.SplitSentences("The cat sat. The dog ran.");
        Assert.Equal(2, result.Count);
        Assert.Equal("The cat sat.", result[0]);
        Assert.Equal("The dog ran.", result[1]);
    }

    [Fact]
    public void SplitSentences_PreservesAbbreviations()
    {
        var result = NlpTextExtractor.SplitSentences("Dr. Smith went to the store. He bought milk.");
        Assert.Equal(2, result.Count);
        Assert.StartsWith("Dr. Smith", result[0]);
        Assert.StartsWith("He bought", result[1]);
    }

    [Fact]
    public void SplitSentences_HandlesExclamationAndQuestion()
    {
        var result = NlpTextExtractor.SplitSentences("What happened? She screamed! Then silence.");
        Assert.Equal(3, result.Count);
    }

    [Fact]
    public void SplitSentences_HandlesQuotedDialogue()
    {
        var result = NlpTextExtractor.SplitSentences(
            "\u201CGet out!\u201D she screamed. He didn\u2019t move.");
        Assert.Equal(2, result.Count);
    }

    // ── SplitParagraphs ─────────────────────────────────────────────────────

    [Fact]
    public void SplitParagraphs_EmptyInput_ReturnsEmpty()
    {
        Assert.Empty(NlpTextExtractor.SplitParagraphs(""));
    }

    [Fact]
    public void SplitParagraphs_MultipleParagraphs_SplitsOnNewlines()
    {
        var result = NlpTextExtractor.SplitParagraphs("First paragraph.\nSecond paragraph.\n\nThird.");
        Assert.Equal(3, result.Count);
    }

    [Fact]
    public void SplitParagraphs_SkipsBlankLines()
    {
        var result = NlpTextExtractor.SplitParagraphs("One.\n\n\n\nTwo.");
        Assert.Equal(2, result.Count);
    }

    // ── IsDialogue ──────────────────────────────────────────────────────────

    [Fact]
    public void IsDialogue_StartsWithQuote_ReturnsTrue()
    {
        Assert.True(NlpTextExtractor.IsDialogue("\u201CGet out of here!\u201D"));
        Assert.True(NlpTextExtractor.IsDialogue("\"Hello,\" she said."));
    }

    [Fact]
    public void IsDialogue_ContainsSaidAfterQuote_ReturnsTrue()
    {
        Assert.True(NlpTextExtractor.IsDialogue("The words \u201Cstop it\u201D said the officer."));
    }

    [Fact]
    public void IsDialogue_PlainNarration_ReturnsFalse()
    {
        Assert.False(NlpTextExtractor.IsDialogue("The sun set over the mountains."));
    }

    // ── HasContraction ──────────────────────────────────────────────────────

    [Fact]
    public void HasContraction_WithContraction_ReturnsTrue()
    {
        Assert.True(NlpTextExtractor.HasContraction("She didn't know."));
        Assert.True(NlpTextExtractor.HasContraction("I'm here."));
    }

    [Fact]
    public void HasContraction_WithoutContraction_ReturnsFalse()
    {
        Assert.False(NlpTextExtractor.HasContraction("She did not know."));
    }
}
