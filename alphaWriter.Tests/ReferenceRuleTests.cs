using alphaWriter.Models;
using alphaWriter.Models.Analysis;
using alphaWriter.Services.Nlp;
using alphaWriter.Services.Nlp.Parsing;
using alphaWriter.Services.Nlp.Rules;
using Xunit;

namespace alphaWriter.Tests;

/// <summary>Phase 1 batch 2: pronouns, tense, gesture tics and the consistency sheet.</summary>
public class ReferenceRuleTests
{
    private static Book NewBook(params (string Name, string Pronouns)[] cast)
    {
        var b = new Book { Title = "B" };
        foreach (var (name, pronouns) in cast)
            b.Characters.Add(new Character { Name = name, Pronouns = pronouns });
        return b;
    }

    private static List<NlpNote> Run(ISceneRule rule, string text, Book book, string? pov = null)
    {
        var doc = SceneDocumentBuilder.FromPlainText(text);
        var ctx = new SceneRuleContext
        {
            Document = doc,
            SceneId = "s1",
            Book = book,
            ViewpointCharacterIds = pov is null ? [] : [book.Characters.Single(c => c.Name == pov).Id],
            Style = new StyleAnalyzer().Analyze(doc.Sentences.Select(s => s.Text).ToList())
        };
        return new ProseRuleEngine([rule], []).EvaluateScene(ctx);
    }

    private static List<SceneAnalysisResult> Results(params string[] scenes) =>
        scenes.Select((text, i) =>
        {
            var doc = SceneDocumentBuilder.FromPlainText(text);
            return new SceneAnalysisResult
            {
                SceneId = $"s{i}",
                SceneTitle = $"Scene {i}",
                ChapterTitle = $"Chapter {i / 2 + 1}",
                Style = new StyleAnalyzer().Analyze(doc.Sentences.Select(s => s.Text).ToList()),
                Sentences = doc.Sentences.Select(s => new SentenceAnalysis
                {
                    Index = s.Index, Text = s.Text, Start = s.Start, WordCount = s.WordCount
                }).ToList()
            };
        }).ToList();

    private static List<NlpNote> RunBook(IBookRule rule, Book book, params string[] scenes) =>
        new ProseRuleEngine([], [rule]).EvaluateBook(new BookRuleContext { Results = Results(scenes), Book = book });

    // ── Pronoun inference ────────────────────────────────────────────────────

    [Fact]
    public void Pronouns_ExplicitValueWins()
    {
        var book = NewBook(("Maya", "she/her"));
        Assert.Equal(PronounSet.She, PronounInference.For(book.Characters[0], book));
    }

    [Fact]
    public void Pronouns_AreGuessedFromText()
    {
        var book = NewBook(("Maya", ""), ("Tomas", ""));
        var chapter = new Chapter { Title = "C" };
        chapter.Scenes.Add(new Scene
        {
            Status = SceneStatus.Draft,
            Content = "<p>Maya turned, and her hands shook. Maya pushed her hair back. " +
                      "Maya opened her bag. Tomas lifted his lamp. Tomas rubbed his eyes. Tomas set his jaw.</p>"
        });
        book.Chapters.Add(chapter);

        Assert.Equal(PronounSet.She, PronounInference.Guess(book.Characters[0], book));
        Assert.Equal(PronounSet.He, PronounInference.Guess(book.Characters[1], book));
    }

    [Fact]
    public void Pronouns_TooLittleText_StaysUnknown()
    {
        var book = NewBook(("Maya", ""));
        var chapter = new Chapter();
        chapter.Scenes.Add(new Scene { Status = SceneStatus.Draft, Content = "<p>Maya opened her bag.</p>" });
        book.Chapters.Add(chapter);
        Assert.Equal(PronounSet.Unknown, PronounInference.For(book.Characters[0], book));
    }

    // ── Ambiguous pronouns ───────────────────────────────────────────────────

    [Fact]
    public void AmbiguousPronoun_TwoSameSetCandidates()
    {
        var book = NewBook(("Tomas", "he/him"), ("Erik", "he/him"), ("Maya", "she/her"));
        var note = Assert.Single(Run(new AmbiguousPronounRule(), "Tomas handed Erik the key. He turned it over.", book));
        Assert.Equal("He", note.Evidence);
        Assert.Contains("Tomas or Erik", note.Message);
    }

    [Theory]
    [InlineData("Tomas handed Maya the key. She turned it over.")]     // only one "she"
    [InlineData("Tomas handed Erik the key.\nHe turned it over.")]     // new paragraph resets
    [InlineData("“Tomas gave Erik the key. He kept it,” she said.")] // dialogue
    public void AmbiguousPronoun_DoesNotFire(string text)
    {
        var book = NewBook(("Tomas", "he/him"), ("Erik", "he/him"), ("Maya", "she/her"));
        Assert.Empty(Run(new AmbiguousPronounRule(), text, book));
    }

    [Fact]
    public void AmbiguousPronoun_UnknownPronouns_AreNotGuessedAt()
    {
        var book = NewBook(("Tomas", ""), ("Erik", ""));
        Assert.Empty(Run(new AmbiguousPronounRule(), "Tomas handed Erik the key. He turned it over.", book));
    }

    // ── Filter words now respect the viewpoint character's pronouns ─────────

    [Fact]
    public void FilterWords_SkipsPronounsThatCantBeTheViewpoint()
    {
        var book = NewBook(("Maya", "she/her"), ("Tomas", "he/him"));
        Assert.Empty(Run(new FilterWordsRule(), "He saw the boat come in.", book, pov: "Maya"));
        Assert.Single(Run(new FilterWordsRule(), "She saw the boat come in.", book, pov: "Maya"));
    }

    // ── Tense drift ──────────────────────────────────────────────────────────

    [Fact]
    public void TenseDrift_FlagsPresentInPastScene_WithFix()
    {
        var book = NewBook(("Maya", "she/her"));
        const string text =
            "She opened the door. She stepped inside. She waited. Maya listened. She counted. " +
            "She turned. She breathed. She walks to the window. She stopped.";
        var note = Assert.Single(Run(new TenseDriftRule(), text, book));
        Assert.Equal("She walks", note.Evidence);
        Assert.Equal("She walked", note.Suggestions[0]);
    }

    [Fact]
    public void TenseDrift_PresentTenseScene_FlagsThePastSlip()
    {
        var book = NewBook();
        const string text = "She opens the door. She steps inside. She waits. She counts. She turns. " +
                            "She breathes. She listens. She stopped. She sits.";
        var note = Assert.Single(Run(new TenseDriftRule(), text, book));
        Assert.Equal("She stopped", note.Evidence);
    }

    [Theory]
    [InlineData("She opened the door. She stepped inside. She waited. She counted. She turned. She breathed. She listened. She stopped.")]
    [InlineData("“She walks every day,” he said. She opened the door. She stepped inside. She waited. She counted. She turned. She breathed. She stopped.")]
    [InlineData("She walks. She waited.")]   // too few samples to judge
    public void TenseDrift_DoesNotFire(string text) =>
        Assert.Empty(Run(new TenseDriftRule(), text, NewBook()));

    [Theory]
    [InlineData("walks", "walked")]
    [InlineData("tries", "tried")]
    [InlineData("closes", "closed")]
    [InlineData("watches", "watched")]
    [InlineData("is", "was")]
    [InlineData("stops", null)]      // needs consonant doubling: no guess
    public void TenseDrift_ToPast(string present, string? past) =>
        Assert.Equal(past, TenseDriftRule.ToPast(present));

    // ── Gesture tics ─────────────────────────────────────────────────────────

    [Fact]
    public void GestureRepeat_WithinAScene()
    {
        var note = Assert.Single(Run(new GestureRepeatRule(),
            "She nodded. He nodded back. They both nodded at the door.", NewBook()));
        Assert.Equal("nods", note.Evidence);
        Assert.Equal(2, note.ExtraSpans.Count);
    }

    [Fact]
    public void GestureTics_AcrossTheBook_AttributedToCharacters()
    {
        var book = NewBook(("Tomas", "he/him"), ("Maya", "she/her"));
        var scene = string.Join(" ", Enumerable.Range(0, 6).Select(_ => "Tomas shrugged. Maya waited. She shrugged."));
        var notes = RunBook(new GestureTicsBookRule(), book, scene, "The tide came in.");

        var note = Assert.Single(notes);
        Assert.Contains("shrug 12 times", note.Message);
        Assert.Contains("Tomas 6", note.Message);
        Assert.Contains("Maya 6", note.Message);
    }

    // ── Consistency sheet ────────────────────────────────────────────────────

    [Fact]
    public void Consistency_MinoritySpellingGetsFixed()
    {
        var notes = RunBook(new ConsistencySheetRule(), NewBook(),
            "The colour of the sea. A grey colour.", "Colours everywhere. The color changed.");

        var summary = notes.First();
        Assert.Contains("colour: UK spelling", summary.Message);
        var fix = Assert.Single(notes.Skip(1));
        Assert.Equal("color", fix.Evidence);
        Assert.Equal("colour", fix.Suggestions[0]);
        Assert.Equal("s1", fix.SceneId);
    }

    [Fact]
    public void Consistency_Hyphenation()
    {
        var notes = RunBook(new ConsistencySheetRule(), NewBook(),
            "An ice-cold wind. The ice-cold sea.", "Her ice cold hands.");
        var fix = Assert.Single(notes.Skip(1));
        Assert.Equal("ice cold", fix.Evidence);
        Assert.Equal("ice-cold", fix.Suggestions[0]);
    }

    [Fact]
    public void Consistency_DashesAndEllipses()
    {
        var notes = RunBook(new ConsistencySheetRule(), NewBook(),
            "He waited—then left. She stayed—for now.", "It was--strange. Well… maybe. So… fine. Then... nothing.");

        Assert.Contains(notes, n => n.Evidence == "--" && n.Suggestions[0] == "—");
        Assert.Contains(notes, n => n.Evidence == "..." && n.Suggestions[0] == "…");
    }

    [Fact]
    public void Consistency_ConsistentBook_HasNoNotes() =>
        Assert.Empty(RunBook(new ConsistencySheetRule(), NewBook(),
            "The colour of the sea—grey.", "Another colour… an ice-cold one."));

    [Fact]
    public void Consistency_FixSpansPointAtTheWord()
    {
        var results = Results("The colour of the sea.", "The color changed.");
        var notes = new ProseRuleEngine([], [new ConsistencySheetRule()])
            .EvaluateBook(new BookRuleContext { Results = results, Book = NewBook() });
        var fix = notes.Single(n => n.Suggestions.Count > 0);
        var sentence = results[1].Sentences[fix.SentenceIndex!.Value];
        Assert.Equal("color", sentence.Text.Substring(fix.AnchorOffset!.Value, fix.Length!.Value));
    }
}
