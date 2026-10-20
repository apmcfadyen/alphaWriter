using alphaWriter.Services.Nlp;
using alphaWriter.Services.Nlp.Parsing;
using Moq;
using Xunit;

namespace alphaWriter.Tests;

/// <summary>
/// The shared scene document: paragraphs, sentences and tokens with offsets that
/// every rule (and the editor's highlight layer) relies on.
/// </summary>
public class SceneDocumentTests
{
    // ── Structure & offsets ──────────────────────────────────────────────────

    [Fact]
    public void FromHtml_PreservesParagraphs()
    {
        var doc = SceneDocumentBuilder.FromHtml("<p>First one. Second one.</p><p>Third.</p>");

        Assert.Equal(2, doc.Paragraphs.Count);
        Assert.Equal(3, doc.Sentences.Count);
        Assert.Equal(0, doc.Sentences[1].ParagraphIndex);
        Assert.Equal(1, doc.Sentences[2].ParagraphIndex);
        Assert.Equal("First one. Second one.\nThird.", doc.Text);
    }

    [Fact]
    public void SentencesNeverCrossParagraphs()
    {
        // No terminal punctuation at the end of the first paragraph
        var doc = SceneDocumentBuilder.FromHtml("<p>A heading without a period</p><p>Then the story starts.</p>");
        Assert.Equal(2, doc.Sentences.Count);
    }

    [Fact]
    public void EveryOffset_MapsBackToItsText()
    {
        var doc = SceneDocumentBuilder.FromHtml(
            "<p>“Get out!” she screamed. He didn’t   move.</p>" +
            "<p>Mr. Hale waited… Then the ice-cold wind rose. // a comment\n</p>" +
            "<p>\"Fine.\" She left.</p>");

        foreach (var s in doc.Sentences)
        {
            Assert.Equal(s.Text, doc.Text.Substring(s.Start, s.Length));
            foreach (var t in s.Tokens)
                Assert.Equal(t.Text, doc.Text.Substring(t.Start, t.Length));
        }
    }

    [Fact]
    public void ClosingQuoteAfterPeriod_StillEndsSentence()
    {
        // Previously "\"Fine.\" She left." was one sentence because the boundary
        // regex required the period to be the last character before the space.
        var doc = SceneDocumentBuilder.FromPlainText("\"Fine.\" She left. “Wait!” he called.");
        Assert.Equal(["\"Fine.\"", "She left.", "“Wait!” he called."],
            doc.Sentences.Select(s => s.Text).ToArray());
    }

    [Fact]
    public void Abbreviations_DoNotSplit()
    {
        var doc = SceneDocumentBuilder.FromPlainText("Dr. Smith went home. Mrs. Jones stayed.");
        Assert.Equal(2, doc.Sentences.Count);
    }

    [Fact]
    public void CommentsAreExcluded()
    {
        var doc = SceneDocumentBuilder.FromHtml("<p>She waited. /* fix this later */ He came.</p>");
        Assert.DoesNotContain("fix", doc.Text);
        Assert.Equal(2, doc.Sentences.Count);
    }

    // ── Quotes & paragraph kinds ─────────────────────────────────────────────

    [Fact]
    public void Tokens_KnowWhetherTheyAreSpoken()
    {
        var doc = SceneDocumentBuilder.FromPlainText("“Run quickly,” she said softly.");
        var words = doc.Tokens.Where(t => t.IsWord).ToDictionary(t => t.Text, t => t.InQuote);

        Assert.True(words["Run"]);
        Assert.True(words["quickly"]);
        Assert.False(words["she"]);
        Assert.False(words["softly"]);
    }

    [Fact]
    public void ParagraphKinds_AreClassified()
    {
        var doc = SceneDocumentBuilder.FromPlainText(
            "The rain fell.\n“Come in,” she said.\n“No. Not tonight.”");

        Assert.Equal(ParagraphKind.Narration, doc.Paragraphs[0].Kind);
        Assert.Equal(ParagraphKind.Mixed, doc.Paragraphs[1].Kind);
        Assert.Equal(ParagraphKind.Dialogue, doc.Paragraphs[2].Kind);
    }

    [Fact]
    public void CapitalizedMidSentence_IsMarked()
    {
        var doc = SceneDocumentBuilder.FromPlainText("Then Emily smiled.");
        var tokens = doc.Tokens.Where(t => t.IsWord).ToList();
        Assert.False(tokens[0].IsCapitalizedMidSentence); // "Then" — sentence start
        Assert.True(tokens[1].IsCapitalizedMidSentence);  // "Emily"
    }

    [Fact]
    public void OccurrenceOf_CountsIdenticalEarlierSentences()
    {
        var doc = SceneDocumentBuilder.FromPlainText("No. She shook her head. No.");
        Assert.Equal(0, doc.OccurrenceOf(doc.Sentences[0]));
        Assert.Equal(1, doc.OccurrenceOf(doc.Sentences[2]));
    }

    // ── POS alignment ────────────────────────────────────────────────────────

    [Fact]
    public void ApplyPos_AlignsTaggerTokensByOffset()
    {
        // Tagger splits "didn't" into "did" + "n't"; alignment must still work.
        var pos = new Mock<IPosTaggingService>();
        pos.SetupGet(p => p.IsLoaded).Returns(true);
        pos.Setup(p => p.TagSpans(It.IsAny<IReadOnlyList<string>>()))
            .Returns([[(0, 2, "PRON"), (3, 3, "AUX"), (6, 3, "PART"), (10, 4, "VERB"), (14, 1, "PUNCT")]]);

        var doc = SceneDocumentBuilder.FromPlainText("He didn't move.", pos.Object);

        Assert.True(doc.HasPos);
        var tags = doc.Tokens.Select(t => (t.Text, t.Pos)).ToList();
        Assert.Contains(("He", "PRON"), tags);
        Assert.Contains(("didn't", "AUX"), tags);
        Assert.Contains(("move", "VERB"), tags);
    }

    [Fact]
    public void ApplyPos_TaggerNotLoaded_LeavesTagsEmpty()
    {
        var pos = new Mock<IPosTaggingService>();
        pos.SetupGet(p => p.IsLoaded).Returns(false);
        var doc = SceneDocumentBuilder.FromPlainText("He left.", pos.Object);
        Assert.False(doc.HasPos);
    }

    // ── NlpTextExtractor now delegates to the same splitter ─────────────────

    [Fact]
    public void SplitSentences_UsesParagraphAwareSplitter()
    {
        var result = NlpTextExtractor.SplitSentences("A title\nIt began. \"Stop.\" He did.");
        Assert.Equal(["A title", "It began.", "\"Stop.\"", "He did."], result.ToArray());
    }
}

public class WordStemmerTests
{
    [Theory]
    [InlineData("walk", "walked", "walking", "walks")]
    [InlineData("smile", "smiled", "smiling", "smiles")]
    [InlineData("run", "running", "ran", "runs")]
    [InlineData("feel", "felt", "feeling", "feels")]
    [InlineData("glance", "glanced", "glancing", "glances")]
    public void WordFamilies_ShareAStem(params string[] forms)
    {
        var stems = forms.Select(WordStemmer.Stem).Distinct().ToList();
        Assert.Single(stems);
    }

    [Fact]
    public void DifferentWords_KeepDifferentStems()
    {
        Assert.NotEqual(WordStemmer.Stem("shadow"), WordStemmer.Stem("shallow"));
        Assert.NotEqual(WordStemmer.Stem("light"), WordStemmer.Stem("night"));
    }

    [Fact]
    public void Contractions_AndPossessives_AreHandled()
    {
        Assert.Equal(WordStemmer.Stem("Marcus"), WordStemmer.Stem("Marcus’s"));
        Assert.Equal("don't", WordStemmer.Stem("don't"));
    }
}
