using alphaWriter.Models;
using alphaWriter.Models.Analysis;
using alphaWriter.Services.Nlp;
using alphaWriter.Services.Nlp.Parsing;
using alphaWriter.Services.Nlp.Rules;
using Xunit;

namespace alphaWriter.Tests;

/// <summary>Phase 1 line- and copy-editing rules: what fires, what must not, and the fixes offered.</summary>
public class LineRuleTests
{
    private static readonly Book Book = MakeBook();

    private static Book MakeBook()
    {
        var b = new Book { Title = "B" };
        b.Characters.Add(new Character { Name = "Maya", FullName = "Maya Lindqvist" });
        b.Characters.Add(new Character { Name = "Tomas", FullName = "Tomas Ekberg", Aka = "Tom" });
        b.Characters.Add(new Character { Name = "Kaelen" });
        b.Locations.Add(new Location { Name = "Harbour Wall" });
        return b;
    }

    private static Character C(string name) => Book.Characters.Single(c => c.Name == name);

    private static List<NlpNote> Run(ISceneRule rule, string text, string? pov = null, EditorialStyleSheet? sheet = null)
    {
        var doc = SceneDocumentBuilder.FromPlainText(text);
        var ctx = new SceneRuleContext
        {
            Document = doc,
            SceneId = "s1",
            Book = Book,
            StyleSheet = sheet ?? new EditorialStyleSheet(),
            ViewpointCharacterIds = pov is null ? [] : [C(pov).Id],
            Style = new StyleAnalyzer().Analyze(doc.Sentences.Select(s => s.Text).ToList())
        };
        return new ProseRuleEngine([rule], []).EvaluateScene(ctx);
    }

    /// <summary>Applies a note's first suggestion to the text, as the editor popover would.</summary>
    private static string ApplyFix(string text, NlpNote note) =>
        SceneDocumentBuilder.FromPlainText(text).Text is var t
            ? t[..note.Start!.Value] + note.Suggestions[0] + t[(note.Start.Value + note.Length!.Value)..]
            : text;

    // ── Head-hopping ─────────────────────────────────────────────────────────

    [Fact]
    public void PovSlip_FlagsAnotherCharactersThoughts()
    {
        var note = Assert.Single(Run(new PovSlipRule(), "Maya turned away. Tomas knew she was lying.", pov: "Maya"));
        Assert.Equal("Tomas knew", note.Evidence);
        Assert.Contains("Maya", note.Message);
    }

    [Theory]
    [InlineData("Maya knew he was lying.")]                    // the viewpoint character may think
    [InlineData("“Tomas knew,” she said.")]           // dialogue
    [InlineData("Tomas looked away.")]                          // observable action
    public void PovSlip_DoesNotFire(string text) =>
        Assert.Empty(Run(new PovSlipRule(), text, pov: "Maya"));

    [Fact]
    public void PovSlip_NeedsASingleViewpoint() =>
        Assert.Empty(Run(new PovSlipRule(), "Tomas knew she was lying."));

    // ── Filter words ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData("She saw the door swing open.", "She saw")]
    [InlineData("She could hear the train.", "She could hear")]
    [InlineData("Maya noticed the lamp was out.", "Maya noticed")]
    [InlineData("The wind dropped, and she heard the bell.", "she heard")]
    public void FilterWords_Fire(string text, string evidence) =>
        Assert.Equal(evidence, Assert.Single(Run(new FilterWordsRule(), text, pov: "Maya")).Evidence);

    [Theory]
    [InlineData("She felt angry.")]                             // named emotion: a different rule
    [InlineData("“I saw it,” she said.")]             // dialogue
    [InlineData("The door swung open.")]
    [InlineData("She hated that he could see it.")]             // subordinate clause, not filtering
    public void FilterWords_DoNotFire(string text) =>
        Assert.Empty(Run(new FilterWordsRule(), text, pov: "Maya"));

    // ── Dialogue tag punctuation ─────────────────────────────────────────────

    [Fact]
    public void TagPunctuation_PeriodBeforeTag_FixesToComma()
    {
        const string text = "“Hello.” she said.";
        var note = Assert.Single(Run(new DialogueTagPunctuationRule(), text));
        Assert.Equal("“Hello,” she said.", ApplyFix(text, note));
    }

    [Fact]
    public void TagPunctuation_CapitalizedPronoun_FixesToLowercase()
    {
        const string text = "\"Really?\" She asked.";
        var note = Assert.Single(Run(new DialogueTagPunctuationRule(), text));
        Assert.Equal("\"Really?\" she asked.", ApplyFix(text, note));
    }

    [Theory]
    [InlineData("“Hello,” she said.")]
    [InlineData("“Go.” She left without another word.")]  // new sentence, not a tag
    [InlineData("“Wait!” I said.")]                      // "I" is always capitalized
    public void TagPunctuation_CorrectOrUnrelated_DoesNotFire(string text) =>
        Assert.Empty(Run(new DialogueTagPunctuationRule(), text));

    // ── Tag verbs ────────────────────────────────────────────────────────────

    [Fact]
    public void SaidBookism_OffersSaid()
    {
        const string text = "“Never,” she retorted.";
        var note = Assert.Single(Run(new DialogueTagVerbRule(), text));
        Assert.Equal("“Never,” she said.", ApplyFix(text, note));
    }

    [Fact]
    public void SaidBookism_Question_OffersAsked()
    {
        const string text = "“Where?” inquired Tomas.";
        var note = Assert.Single(Run(new DialogueTagVerbRule(), text));
        Assert.Equal("asked", note.Suggestions[0]);
    }

    [Fact]
    public void ActionTag_BecomesABeat()
    {
        const string text = "“Hello,” she smiled.";
        var note = Assert.Single(Run(new DialogueTagVerbRule(), text));
        Assert.Equal("“Hello.” She smiled.", ApplyFix(text, note));
        Assert.Contains("can’t smile words", note.Message);
        Assert.Contains("…Hello.” She smiled.", note.Message);
    }

    [Fact]
    public void PlainSaid_IsFine() =>
        Assert.Empty(Run(new DialogueTagVerbRule(), "“Hello,” she said."));

    [Fact]
    public void AdverbTag_OffersTheBareVerb()
    {
        const string text = "“Get out,” she said angrily.";
        var note = Assert.Single(Run(new DialogueAdverbTagRule(), text));
        Assert.Equal("“Get out,” she said.", ApplyFix(text, note));
    }

    // ── Talking heads ────────────────────────────────────────────────────────

    [Fact]
    public void TalkingHeads_LongUntaggedRun()
    {
        var lines = string.Join("\n", Enumerable.Range(1, 7).Select(i => $"“Line {i}.”"));
        var note = Assert.Single(Run(new TalkingHeadsRule(), "She waited.\n" + lines));
        Assert.Contains("7 lines", note.Message);
    }

    [Fact]
    public void TalkingHeads_TaggedRun_IsFine()
    {
        var lines = string.Join("\n", Enumerable.Range(1, 7).Select(i => $"“Line {i},” she said."));
        Assert.Empty(Run(new TalkingHeadsRule(), lines));
    }

    // ── Redundancy and weak verbs ────────────────────────────────────────────

    [Theory]
    [InlineData("He nodded his head slowly.", "He nodded slowly.")]
    [InlineData("She was nodding her head.", "She was nodding.")]
    [InlineData("She shrugged her shoulders.", "She shrugged.")]
    [InlineData("He thought to himself about it.", "He thought about it.")]
    [InlineData("Each and every lamp went out.", "Every lamp went out.")]
    [InlineData("She made a decision to stay.", "She decided to stay.")]
    [InlineData("He gave a nod.", "He nodded.")]
    [InlineData("They are making a decision now.", "They are deciding now.")]
    [InlineData("The train came to a stop.", "The train stopped.")]
    public void Redundancy_FixKeepsTense(string text, string expected)
    {
        var note = Assert.Single(Run(new RedundancyRule(), text));
        Assert.Equal(expected, ApplyFix(text, note));
    }

    [Fact]
    public void Redundancy_IgnoresDialogue() =>
        Assert.Empty(Run(new RedundancyRule(), "“I nodded my head,” she said."));

    // ── Clichés ──────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Her heart skipped a beat.")]
    [InlineData("A chill ran down his spine.")]
    [InlineData("She let out a breath she didn't know she was holding.")]
    [InlineData("They arrived in the nick of time.")]
    public void Cliche_Fires(string text) => Assert.Single(Run(new ClicheRule(), text));

    [Fact]
    public void Cliche_NotAcrossPunctuation() =>
        Assert.Empty(Run(new ClicheRule(), "Her heart, skipped a beat."));

    // ── Crutch words ─────────────────────────────────────────────────────────

    [Fact]
    public void CrutchWords_UnderlinesEveryUse()
    {
        var note = Assert.Single(Run(new CrutchWordsRule(),
            "She just waited. He just left. It was just cold. The door just opened."));
        Assert.Equal("just", note.Evidence);
        Assert.Equal(3, note.ExtraSpans.Count);
    }

    [Fact]
    public void CrutchWords_FewUses_AreFine() =>
        Assert.Empty(Run(new CrutchWordsRule(), "She just waited. The door opened."));

    [Fact]
    public void CrutchWords_AllowedWord_IsSkipped() =>
        Assert.Empty(Run(new CrutchWordsRule(), "She just waited. He just left. It was just cold.",
            sheet: new EditorialStyleSheet { AllowedWords = ["just"] }));

    // ── Names ────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Kaelan drew his sword.", "Kaelen drew his sword.")]
    [InlineData("She took Kaelan’s hand.", "She took Kaelen’s hand.")]
    [InlineData("Thomas waited by the wall.", "Tomas waited by the wall.")]
    public void NameSpelling_FixesToStoryBibleName(string text, string expected)
    {
        var note = Assert.Single(Run(new NameSpellingRule(), text));
        Assert.Equal(expected, ApplyFix(text, note));
    }

    [Theory]
    [InlineData("Kaelen drew his sword.")]          // correct
    [InlineData("Mayor Brandt arrived.")]           // not close enough to Maya
    [InlineData("Harbour lights burned. The harbour was quiet.")] // ordinary word
    public void NameSpelling_DoesNotFire(string text) =>
        Assert.Empty(Run(new NameSpellingRule(), text));

    [Fact]
    public void SimilarNames_FlagsLookAlikes()
    {
        var book = new Book();
        book.Characters.Add(new Character { Name = "Jon" });
        book.Characters.Add(new Character { Name = "Joan" });
        book.Characters.Add(new Character { Name = "Eleonora" });
        var notes = new ProseRuleEngine([], [new SimilarNamesRule()])
            .EvaluateBook(new BookRuleContext { Results = [], Book = book });
        var note = Assert.Single(notes);
        Assert.Contains("Jon", note.Message);
        Assert.Contains("Joan", note.Message);
    }

    [Theory]
    [InlineData("kaelen", "kaelan", 0.9)]
    [InlineData("martha", "marhta", 0.95)]
    public void JaroWinkler_KnownPairs(string a, string b, double atLeast) =>
        Assert.True(StringSimilarity.JaroWinkler(a, b) >= atLeast);

    [Fact]
    public void AllNewRules_AreInTheCatalogAndEngine()
    {
        var engine = ProseRuleEngine.CreateDefault();
        var ids = engine.SceneRules.Select(r => r.Descriptor.Id).Concat(engine.BookRules.Select(r => r.Descriptor.Id)).ToList();
        foreach (var id in new[] { "pov.head-hop", "style.filter-words", "dialogue.tag-punctuation", "dialogue.tag-verbs",
                                   "dialogue.adverb-tags", "dialogue.talking-heads", "style.redundancy", "style.cliche",
                                   "style.crutch-words", "consistency.name-spelling", "consistency.similar-names" })
        {
            Assert.Contains(id, ids);
            Assert.NotNull(RuleCatalog.Find(id));
        }
    }
}
