using alphaWriter.Models;
using alphaWriter.Models.Analysis;
using alphaWriter.Services.Nlp.Parsing;
using alphaWriter.Services.Nlp.Rules;
using Xunit;

namespace alphaWriter.Tests;

/// <summary>Signature words: the author's word use against typical fiction.</summary>
public class SignatureWordsTests
{
    // Copied next to the test assembly by alphaWriter.Tests.csproj
    private static readonly Lazy<ReferenceFrequencies> Bundled = new(() =>
    {
        using var reader = new StreamReader(Path.Combine(AppContext.BaseDirectory, "lexicons", "fiction-frequencies.tsv"));
        return ReferenceFrequencies.Parse(reader);
    });

    /// <summary>Ordinary narrative filler plus <paramref name="extra"/>, as a word list.</summary>
    private static IEnumerable<string> Prose(string extra, int fillerRepeats = 40)
    {
        const string filler =
            "The evening was cold and the road ran down toward the river where the boats were tied. " +
            "She walked past the houses and thought about the letter in her pocket while the light faded. ";
        var text = string.Concat(Enumerable.Repeat(filler, fillerRepeats)) + extra;
        return text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(w => w.Trim('.', ','));
    }

    [Fact]
    public void BundledTable_IsComplete()
    {
        var reference = Bundled.Value;
        Assert.True(reference.Total > 10_000_000, $"total {reference.Total}");
        Assert.True(reference.CountOf(WordStemmer.Stem("the")) > 500_000);
        Assert.True(reference.CountOf(WordStemmer.Stem("glanced")) > 1_000);   // glance + glanced + glancing
    }

    [Fact]
    public void Find_SurfacesAnAuthorsHabit()
    {
        var habit = string.Concat(Enumerable.Repeat("He glanced at her. She glanced away. They murmured. ", 12));
        var found = SignatureWords.Find(Prose(habit), Bundled.Value);

        var glance = Assert.Single(found, s => s.Word == "glanced");
        Assert.Equal(24, glance.Count);
        Assert.True(glance.Ratio > 10, $"ratio {glance.Ratio}");
        Assert.Contains(found, s => s.Word == "murmured");
    }

    [Fact]
    public void Find_SkipsNamesInventedWordsAndAllowedWords()
    {
        var text = string.Concat(Enumerable.Repeat("Kaelen raised the wyrdstone. Maya glanced at Kaelen. ", 15));
        var excluded = new HashSet<string> { "kaelen", "maya", "glanced" };
        var found = SignatureWords.Find(Prose(text), Bundled.Value, excluded);

        Assert.DoesNotContain(found, s => s.Word is "kaelen" or "maya" or "glanced");
        Assert.DoesNotContain(found, s => s.Word == "wyrdstone");  // not in the reference: not a "habit"
    }

    [Fact]
    public void Find_OrdinaryWordsAtNormalRates_AreNotSignatures()
    {
        var found = SignatureWords.Find(Prose("", fillerRepeats: 3), Bundled.Value);
        Assert.Empty(found);
    }

    [Fact]
    public void Rule_ReportsSignatureWords()
    {
        // Light filler, so the filler's own words don't become "signature words"
        var habit = string.Join(" ", Prose(string.Concat(Enumerable.Repeat("He glanced at her. She glanced away. ", 12)), fillerRepeats: 3));
        var doc = SceneDocumentBuilder.FromPlainText(habit);
        var result = new SceneAnalysisResult
        {
            SceneId = "s1",
            Sentences = doc.Sentences.Select(s => new SentenceAnalysis { Index = s.Index, Text = s.Text, Start = s.Start }).ToList()
        };

        var notes = new ProseRuleEngine([], [new SignatureWordsRule()])
            .EvaluateBook(new BookRuleContext { Results = [result], Book = new Book(), Reference = Bundled.Value });

        var note = Assert.Single(notes);
        Assert.Contains("“glanced”", note.Message);
    }

    [Fact]
    public void Rule_WithoutReferenceTable_StaysQuiet()
    {
        var notes = new ProseRuleEngine([], [new SignatureWordsRule()])
            .EvaluateBook(new BookRuleContext { Results = [new SceneAnalysisResult()], Book = new Book() });
        Assert.Empty(notes);
    }
}
