using System.Text;
using alphaWriter.Models;
using alphaWriter.Services;
using Xunit;

namespace alphaWriter.Tests;

/// <summary>Reading scene HTML back from the editor, and repairing the old escaping damage.</summary>
public class EditorContentCodecTests
{
    private static string B64(string s) => Convert.ToBase64String(Encoding.UTF8.GetBytes(s));

    [Theory]
    [InlineData("<p>\"Apply when you're old enough,\" he said.</p>")]
    [InlineData("<p>Back\\slash, “curly” quotes, émigré, 東京, 🙂</p>")]
    [InlineData("")]
    public void Decode_RoundTripsExactly(string html)
    {
        Assert.Equal(html, EditorContentCodec.DecodeBase64Result(B64(html)));
        // Some MAUI versions hand the result back as a quoted JSON string
        Assert.Equal(html, EditorContentCodec.DecodeBase64Result("\"" + B64(html) + "\""));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not base64!")]
    public void Decode_UnusableResult_IsNullSoContentIsKept(string? raw) =>
        Assert.Null(EditorContentCodec.DecodeBase64Result(raw));

    [Theory]
    [InlineData("<p>\\\"Hi,\\\" he said.</p>", "<p>\"Hi,\" he said.</p>")]
    [InlineData("<p>\\\\\\\\\\\\\\\"Apply when you're old enough,\\\\\\\\\\\\\\\" he said.</p>",
                "<p>\"Apply when you're old enough,\" he said.</p>")]
    [InlineData("<p class=\\\\\\\"x\\\\\\\">a</p>", "<p class=\"x\">a</p>")]
    [InlineData("<p>a\\\\\\\\b</p>", "<p>a\\b</p>")]
    [InlineData("<p>No damage, “curly” and \"straight\".</p>", "<p>No damage, “curly” and \"straight\".</p>")]
    public void Repair_RemovesAccumulatedBackslashes(string damaged, string expected) =>
        Assert.Equal(expected, EditorContentCodec.RepairEscapedQuotes(damaged));

    [Fact]
    public void RepairBooks_ReportsWhetherAnythingChanged()
    {
        var book = new Book();
        var chapter = new Chapter();
        chapter.Scenes.Add(new Scene { Content = "<p>\\\\\\\"Hi\\\\\\\"</p>" });
        chapter.Scenes.Add(new Scene { Content = "<p>Fine.</p>" });
        book.Chapters.Add(chapter);

        Assert.True(EditorContentCodec.RepairBooks([book]));
        Assert.Equal("<p>\"Hi\"</p>", chapter.Scenes[0].Content);
        Assert.False(EditorContentCodec.RepairBooks([book]));
    }
}
