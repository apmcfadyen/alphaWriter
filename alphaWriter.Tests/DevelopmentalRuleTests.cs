using alphaWriter.Models;
using alphaWriter.Models.Analysis;
using alphaWriter.Services;
using alphaWriter.Services.Nlp;
using alphaWriter.Services.Nlp.Parsing;
using alphaWriter.Services.Nlp.Rules;
using alphaWriter.ViewModels;
using Moq;
using Xunit;

namespace alphaWriter.Tests;

/// <summary>Phase 2: beats, grounding, presence, continuity and chapter structure.</summary>
public class DevelopmentalRuleTests
{
    private static Book Cast()
    {
        var b = new Book { Title = "B" };
        b.Characters.Add(new Character { Name = "Maya", Pronouns = "she/her" });
        b.Characters.Add(new Character { Name = "Tomas", Pronouns = "he/him" });
        b.Characters.Add(new Character { Name = "Erik", Pronouns = "he/him" });
        b.Locations.Add(new Location { Name = "Customs House" });
        b.Items.Add(new Item { Name = "Brass Key", Aka = "the key" });
        return b;
    }

    private static string Id(Book b, string name) => b.Characters.Single(c => c.Name == name).Id;

    private static List<NlpNote> Run(ISceneRule rule, string text, Book? book = null,
        string[]? tagged = null, string? pov = null)
    {
        book ??= Cast();
        var doc = SceneDocumentBuilder.FromPlainText(text);
        var ctx = new SceneRuleContext
        {
            Document = doc,
            SceneId = "s1",
            Book = book,
            TaggedCharacterIds = (tagged ?? []).Select(n => Id(book, n)).ToList(),
            ViewpointCharacterIds = pov is null ? [] : [Id(book, pov)],
            Style = new StyleAnalyzer().Analyze(doc.Sentences.Select(s => s.Text).ToList())
        };
        return new ProseRuleEngine([rule], []).EvaluateScene(ctx);
    }

    /// <summary>Scenes as analysis results: (chapter, text) pairs in reading order.</summary>
    private static List<SceneAnalysisResult> Book(params (string Chapter, string Text)[] scenes) =>
        scenes.Select((sc, i) =>
        {
            var doc = SceneDocumentBuilder.FromPlainText(sc.Text);
            return new SceneAnalysisResult
            {
                SceneId = $"s{i}", SceneTitle = $"Scene {i}", ChapterTitle = sc.Chapter,
                Style = new StyleAnalyzer().Analyze(doc.Sentences.Select(s => s.Text).ToList()),
                Sentences = doc.Sentences.Select(s => new SentenceAnalysis
                { Index = s.Index, Text = s.Text, Start = s.Start, WordCount = s.WordCount }).ToList()
            };
        }).ToList();

    private static List<NlpNote> RunBook(IBookRule rule, Book book, params (string, string)[] scenes) =>
        new ProseRuleEngine([], [rule]).EvaluateBook(new BookRuleContext { Results = Book(scenes), Book = book });

    private static string Filler(int sentences) =>
        string.Join(" ", Enumerable.Range(0, sentences).Select(i => $"The tide moved along the shore in slow lines number {i}."));

    // ── Beats ────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("“Where were you?” “Nowhere.”", Beat.Dialogue)]
    [InlineData("She grabbed the rope and pulled. He jumped, landed, and ran for the gate.", Beat.Action)]
    [InlineData("She wondered whether he knew. Perhaps he had guessed. She hoped not.", Beat.Interiority)]
    [InlineData("Years ago, before the war, her father had owned the yard. He had built boats there and had sold them to men who had never paid.", Beat.Exposition)]
    [InlineData("Grey light lay on the water. Fog hung over the stone walls, damp and cold.", Beat.Description)]
    public void Beats_ClassifyParagraphs(string paragraph, Beat expected)
    {
        var doc = SceneDocumentBuilder.FromPlainText(paragraph);
        Assert.Equal(expected, BeatClassifier.Classify(doc, doc.Paragraphs[0]));
    }

    [Fact]
    public void InfoDump_ThreeBackstoryParagraphsInARow()
    {
        const string backstory = "Years ago her father had owned the yard, and he had built boats there that had never sold.";
        var text = "She opened the door.\n" + string.Join("\n", Enumerable.Repeat(backstory, 3)) + "\nShe stepped inside.";
        var note = Assert.Single(Run(new InfoDumpRule(), text));
        Assert.Contains("3 paragraphs of backstory", note.Message);
    }

    [Fact]
    public void InfoDump_SingleBackstoryParagraph_IsFine()
    {
        const string text = "She opened the door.\nYears ago her father had owned the yard and had built boats.\nShe stepped inside.";
        Assert.Empty(Run(new InfoDumpRule(), text));
    }

    // ── Grounding ────────────────────────────────────────────────────────────

    [Fact]
    public void Grounding_WhiteRoomOpening()
    {
        var text = "“You came back.” “I said I would.” " + string.Join(" ", Enumerable.Repeat("They argued about it again and again without stopping.", 40));
        var note = Assert.Single(Run(new SceneGroundingRule(), text));
        Assert.Contains("where we are", note.Message);
    }

    [Theory]
    [InlineData("The harbour was empty at dawn. ")]                  // place noun
    [InlineData("Maya waited outside the Customs House that night. ")] // Story Bible location
    public void Grounding_PlaceInTheOpening_IsFine(string opening) =>
        Assert.Empty(Run(new SceneGroundingRule(), opening + Filler(40)));

    // ── Presence ─────────────────────────────────────────────────────────────

    [Fact]
    public void Presence_UntaggedNarrationMention_IsLeftToAutoTagging() =>
        Assert.Empty(Run(new CharacterPresenceRule(), "Tomas waited. Then Tomas left.", tagged: []));

    [Fact]
    public void Presence_TaggedButOnlyTalkedAbout_OffersUntag()
    {
        var book = Cast();
        var note = Assert.Single(Run(new CharacterPresenceRule(),
            "“We should ask Tomas,” she said. “Tomas knows.”", book, tagged: ["Tomas"]));
        Assert.Equal($"untag-character:{Id(book, "Tomas")}", note.FixAction);
        Assert.Contains("only talked about", note.Message);
    }

    [Fact]
    public void Presence_TaggedAndInNarration_IsFine() =>
        Assert.Empty(Run(new CharacterPresenceRule(), "“Come on,” Tomas said. Tomas left.", tagged: ["Tomas"]));

    [Fact]
    public void Presence_TaggedButNeverNamed_OffersUntag()
    {
        var book = Cast();
        var note = Assert.Single(Run(new CharacterPresenceRule(), Filler(20), book, tagged: ["Erik"]));
        Assert.Equal($"untag-character:{Id(book, "Erik")}", note.FixAction);
    }

    [Fact]
    public void Presence_ViewpointCharacterNeedsNoTag() =>
        Assert.Empty(Run(new CharacterPresenceRule(), "Maya waited. Maya listened. " + Filler(20), pov: "Maya"));

    [Fact]
    public void FixAction_UntagSticksThroughAutoDetection()
    {
        var book = Cast();
        var chapter = new Chapter { Title = "C" };
        var scene = new Scene { Title = "S", Status = SceneStatus.Draft, Content = "<p>Tomas waited.</p>" };
        chapter.Scenes.Add(scene);
        book.Chapters.Add(chapter);

        var bookService = new Mock<IBookService>();
        bookService.Setup(s => s.SaveBooksAsync(It.IsAny<List<Book>>())).Returns(Task.CompletedTask);
        var vm = new WriterViewModel(bookService.Object, new Mock<IImageService>().Object,
            new Mock<INlpAnalysisService>().Object, new Mock<INlpModelManager>().Object,
            new Mock<INerService>().Object, new Mock<IPosTaggingService>().Object,
            new Mock<ILocationHeuristicService>().Object, new Mock<INlpCacheService>().Object);
        typeof(WriterViewModel).GetField("selectedBook", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .SetValue(vm, book);

        vm.ApplyFixAction($"tag-character:{Id(book, "Tomas")}", scene.Id);
        Assert.Contains(Id(book, "Tomas"), scene.CharacterIds);

        vm.ApplyFixAction($"untag-character:{Id(book, "Tomas")}", scene.Id);
        Assert.DoesNotContain(Id(book, "Tomas"), scene.CharacterIds);
        Assert.Contains(Id(book, "Tomas"), scene.UntaggedIds);   // auto-detection won't re-add

        typeof(WriterViewModel).GetField("selectedScene", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .SetValue(vm, scene);
        var autoDetect = typeof(WriterViewModel).GetMethod("AutoDetectSceneEntities",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        autoDetect.Invoke(vm, null);
        Assert.DoesNotContain(Id(book, "Tomas"), scene.CharacterIds);

        vm.ApplyFixAction($"tag-character:{Id(book, "Tomas")}", scene.Id);
        Assert.DoesNotContain(Id(book, "Tomas"), scene.UntaggedIds);
        scene.CharacterIds.Clear();
        autoDetect.Invoke(vm, null);
        Assert.Contains(Id(book, "Tomas"), scene.CharacterIds);
    }

    // ── Book-level continuity ────────────────────────────────────────────────

    [Fact]
    public void Absence_RecurringCharacterVanishes()
    {
        var scenes = Enumerable.Range(1, 12).Select(c => ($"Chapter {c}",
            c is 1 or 2 or 3 or 11 ? "Tomas walked to the quay." : "The tide came in.")).ToArray();
        var note = Assert.Single(RunBook(new CharacterAbsenceRule(), Cast(), scenes));
        Assert.Contains("Tomas disappears after “Chapter 3”", note.Message);
        Assert.Contains("(7 chapters)", note.Message);
    }

    [Fact]
    public void Attributes_ContradictoryEyeColour_GetsFixed()
    {
        var notes = RunBook(new AttributeContinuityRule(), Cast(),
            ("One", "Maya's blue eyes narrowed."),
            ("Two", "Maya turned. Her blue eyes were wet."),
            ("Three", "Maya's green eyes flashed."));

        var note = Assert.Single(notes);
        Assert.Equal("green", note.Evidence);
        Assert.Equal("blue", note.Suggestions[0]);
        Assert.Equal("s2", note.SceneId);
        Assert.Contains("eyes are green here, but blue elsewhere", note.Message);
    }

    [Fact]
    public void Attributes_PronounNeedsAMatchingCharacter()
    {
        // "her" after Tomas (he/him) can't be Tomas, so no claim is recorded for him
        var notes = RunBook(new AttributeContinuityRule(), Cast(),
            ("One", "Tomas's brown eyes narrowed."),
            ("Two", "Tomas looked at the girl. Her green eyes were wet."));
        Assert.Empty(notes);
    }

    [Fact]
    public void SetupPayoff_ItemNeverMentionedAgain()
    {
        var notes = RunBook(new SetupPayoffRule(), Cast(),
            ("Chapter 1", "He turned the brass key in his fingers."),
            ("Chapter 2", "The tide came in."), ("Chapter 3", "The tide went out."),
            ("Chapter 4", "Rain."), ("Chapter 5", "Wind."));
        var note = Assert.Single(notes);
        Assert.Contains("introduced in “Chapter 1” and never mentioned again", note.Message);
        Assert.Equal("brass key", note.Evidence);
    }

    [Fact]
    public void SetupPayoff_PaidOffItem_IsFine() =>
        Assert.Empty(RunBook(new SetupPayoffRule(), Cast(),
            ("Chapter 1", "He turned the brass key in his fingers."),
            ("Chapter 2", "Rain."), ("Chapter 3", "Wind."),
            ("Chapter 4", "At last the key turned in the lock.")));

    [Fact]
    public void RepeatedPhrases_AcrossChapters()
    {
        var notes = RunBook(new RepeatedPhraseRule(), Cast(),
            ("Chapter 1", "Light spilled across the wet stones of the quay."),
            ("Chapter 5", "Once again light spilled across the wet stones of the quay."));
        var note = Assert.Single(notes);
        Assert.Contains("spilled across the wet stones", note.Evidence);
        Assert.Equal("s1", note.SceneId);
    }

    [Fact]
    public void RepeatedPhrases_SameChapterOrDialogue_AreFine() =>
        Assert.Empty(RunBook(new RepeatedPhraseRule(), Cast(),
            ("Chapter 1", "Light spilled across the wet stones of the quay."),
            ("Chapter 1", "Light spilled across the wet stones of the quay."),
            ("Chapter 2", "“Light spilled across the wet stones of the quay,” she said.")));

    [Fact]
    public void TimeCues_ChapterOpeningWithout()
    {
        var notes = RunBook(new ChapterTimeCueRule(), Cast(),
            ("Chapter 1", "It began."),
            ("Chapter 2", "The next morning she left."),
            ("Chapter 3", "She opened the ledger."));
        var note = Assert.Single(notes);
        Assert.Contains("“Chapter 3” opens without a time cue", note.Message);
    }

    [Fact]
    public void ChapterEndings_SoftEndingsInARow_Escalate()
    {
        var notes = RunBook(new ChapterEndingRule(), Cast(),
            ("C1", "They talked late into the evening by the fire. At last she finally slept."),
            ("C2", "The day ended. She went home and smiled, safe at last."),
            ("C3", "It was quiet. They sat together in the warm kitchen until morning came."),
            ("C4", "Then someone knocked at the door."),
            ("C5", "The end."));
        Assert.Equal(["C1", "C2"], notes.Select(n => n.SceneId == "s0" ? "C1" : "C2").Take(2).ToArray());
        Assert.DoesNotContain(notes, n => n.Message.Contains("“C4”"));   // ends on a knock
        Assert.Contains(notes, n => n.Severity == NlpNoteSeverity.Warning);       // a run of soft endings
    }

    [Fact]
    public void ChapterEndings_QuestionEnding_IsStrong() =>
        Assert.Empty(RunBook(new ChapterEndingRule(), Cast(),
            ("C1", "She looked at the ledger. Who had written it?"),
            ("C2", "The door was locked. Then someone screamed."),
            ("C3", "It ended.")));
}
