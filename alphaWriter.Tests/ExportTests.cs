using System.IO.Compression;
using System.Text;
using System.Reflection;
using alphaWriter.Models;
using alphaWriter.Services;
using alphaWriter.Services.Export;
using alphaWriter.Services.Nlp;
using alphaWriter.ViewModels;
using Moq;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using DocumentFormat.OpenXml.Wordprocessing;
using Xunit;

namespace alphaWriter.Tests;

/// <summary>Exporting a book or chapters to Word, PDF, Markdown and plain text.</summary>
public class ExportTests
{
    private static Book SampleBook()
    {
        var book = new Book { Title = "The Long Winter: A Harbour Mystery", Author = "Ann Writer" };
        var one = new Chapter { Title = "Arrival" };
        one.Scenes.Add(new Scene
        {
            Title = "Station", Status = SceneStatus.Draft,
            Content = "<p>The clock had <b>stopped</b> at a quarter past four.</p>" +
                      "<p>“Late,” he said, <i>again</i>. // fix this line later</p>" +
                      "<p>Tom &amp; Maya <u>waited</u>.</p>"
        });
        one.Scenes.Add(new Scene
        {
            Title = "Platform", Status = SceneStatus.FirstEdit,
            Content = "<div>She waited /* cut this:</div><div>whole paragraph */ by the rail.</div>"
        });
        one.Scenes.Add(new Scene { Title = "Notes", Status = SceneStatus.Outline, Content = "<p>Outline: storm.</p>" });
        var two = new Chapter { Title = "The Docks" };
        two.Scenes.Add(new Scene { Title = "Fog", Status = SceneStatus.Draft, Content = "<p>Fog rolled in.</p>" });
        book.Chapters.Add(one);
        book.Chapters.Add(two);
        return book;
    }

    private static readonly Lazy<bool> Fonts = new(() =>
    {
        foreach (var face in PdfFonts.Files)
            PdfFonts.Add(face, File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fonts", face + ".ttf")));
        return true;
    });

    // ── Manuscript ───────────────────────────────────────────────────────────

    [Fact]
    public void Build_StripsCommentsAndKeepsFormatting()
    {
        var m = ManuscriptBuilder.Build(SampleBook(), new ExportOptions());
        var station = m.Chapters[0].Scenes[0].Paragraphs;

        Assert.Equal("The clock had stopped at a quarter past four.", station[0].PlainText);
        Assert.Contains(station[0].Runs, r => r.Text == "stopped" && r.Bold);
        Assert.Equal("“Late,” he said, again.", station[1].PlainText);   // // comment removed
        Assert.Contains(station[1].Runs, r => r.Text == "again" && r.Italic);
        Assert.Equal("Tom & Maya waited.", station[2].PlainText);
        Assert.Contains(station[2].Runs, r => r.Text == "waited" && r.Underline);

        // A block comment spanning two paragraphs leaves one paragraph's worth of text
        var platform = m.Chapters[0].Scenes[1].Paragraphs;
        Assert.Equal(["She waited", "by the rail."], platform.Select(p => p.PlainText));
    }

    [Fact]
    public void Build_SkipsOutlineScenesUnlessAsked()
    {
        Assert.Equal(2, ManuscriptBuilder.Build(SampleBook(), new ExportOptions()).Chapters[0].Scenes.Count);
        Assert.Equal(3, ManuscriptBuilder.Build(SampleBook(), new ExportOptions { IncludeOutlineScenes = true }).Chapters[0].Scenes.Count);
    }

    [Fact]
    public void Build_SelectedChapterKeepsItsBookNumber()
    {
        var book = SampleBook();
        var m = ManuscriptBuilder.Build(book, new ExportOptions { ChapterIds = [book.Chapters[1].Id] });
        var chapter = Assert.Single(m.Chapters);
        Assert.Equal(2, chapter.Number);
        Assert.Equal("Chapter 2: The Docks", chapter.Heading);
    }

    [Theory]
    [InlineData(ChapterHeadingStyle.Number, "Arrival", "Chapter 1")]
    [InlineData(ChapterHeadingStyle.Title, "Arrival", "Arrival")]
    [InlineData(ChapterHeadingStyle.Title, "", "Chapter 1")]
    [InlineData(ChapterHeadingStyle.NumberAndTitle, "Arrival", "Chapter 1: Arrival")]
    [InlineData(ChapterHeadingStyle.NumberAndTitle, "Chapter 1", "Chapter 1")]
    [InlineData(ChapterHeadingStyle.Title, "chapter 1", "Chapter 1")]
    public void Heading_FollowsTheStyle(ChapterHeadingStyle style, string title, string expected) =>
        Assert.Equal(expected, ManuscriptBuilder.Heading(1, title, style));

    [Theory]
    [InlineData(86_512, 87_000)]
    [InlineData(640, 600)]
    public void WordCount_IsRoundedForTheTitlePage(int words, int expected) =>
        Assert.Equal(expected, ManuscriptBuilder.RoundedWordCount(words));

    // ── Markdown and text ────────────────────────────────────────────────────

    [Fact]
    public void Markdown_HasHeadingsEmphasisAndEscapedSceneBreak()
    {
        var md = MarkdownWriter.Write(ManuscriptBuilder.Build(SampleBook(), new ExportOptions()), new ExportOptions());
        Assert.StartsWith("# The Long Winter: A Harbour Mystery", md);
        Assert.Contains("## Chapter 1: Arrival", md);
        Assert.Contains("The clock had **stopped** at", md);
        Assert.Contains("he said, *again*.", md);
        Assert.Contains("<u>waited</u>", md);
        Assert.Contains("\n\\#\n", md.Replace("\r\n", "\n"));   // "#" scene break isn't a heading
        Assert.DoesNotContain("fix this line", md);
    }

    [Fact]
    public void Text_IsPlain()
    {
        var text = PlainTextWriter.Write(ManuscriptBuilder.Build(SampleBook(), new ExportOptions()),
            new ExportOptions { SceneBreak = "* * *" });
        Assert.Contains("Chapter 2: The Docks", text);
        Assert.Contains("* * *", text);
        Assert.DoesNotContain("<", text);
        Assert.DoesNotContain("**", text);
    }

    // ── Word ─────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(ExportStyle.StandardManuscript)]
    [InlineData(ExportStyle.ReadingCopy)]
    public void Docx_IsValidOpenXml(ExportStyle style)
    {
        var options = new ExportOptions { Style = style };
        var bytes = DocxWriter.Write(ManuscriptBuilder.Build(SampleBook(), options), options);
        using var doc = WordprocessingDocument.Open(new MemoryStream(bytes), false);
        var errors = new OpenXmlValidator().Validate(doc).Select(e => $"{e.Path?.XPath}: {e.Description}").ToList();
        Assert.Empty(errors);
    }

    [Fact]
    public void Docx_ManuscriptHasTitlePageHeaderAndChapterBreaks()
    {
        var options = new ExportOptions();
        var bytes = DocxWriter.Write(ManuscriptBuilder.Build(SampleBook(), options), options);
        using var doc = WordprocessingDocument.Open(new MemoryStream(bytes), false);
        var body = doc.MainDocumentPart!.Document.Body!;
        var text = body.InnerText;

        Assert.Contains("about 0 words", text.Replace("about 100 words", "about 0 words"));   // tiny sample rounds down
        Assert.Contains("THE LONG WINTER: A HARBOUR MYSTERY", text);
        Assert.Equal(2, body.Descendants<PageBreakBefore>().Count());
        Assert.Contains(body.Descendants<Run>(), r => r.InnerText == "stopped" && r.RunProperties?.Bold is not null);

        var header = doc.MainDocumentPart.HeaderParts.Select(h => h.Header.InnerText).First(t => t.Length > 0);
        Assert.StartsWith("Writer / THE LONG WINTER / ", header);
        Assert.NotNull(body.Descendants<TitlePage>().FirstOrDefault());   // no header on the title page
    }

    [Theory]
    [InlineData("The Long Winter: A Harbour Mystery", "The Long Winter")]
    [InlineData("Salt", "Salt")]
    [InlineData("A Very Long Title Indeed", "A Very Long")]
    public void ShortTitle_ForTheRunningHeader(string title, string expected) =>
        Assert.Equal(expected, DocxWriter.ShortTitle(title));

    // ── PDF ──────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(ExportStyle.StandardManuscript, false)]
    [InlineData(ExportStyle.ReadingCopy, false)]
    [InlineData(ExportStyle.StandardManuscript, true)]
    public void Pdf_RendersWithBundledFonts(ExportStyle style, bool typewriter)
    {
        Assert.True(Fonts.Value);
        var options = new ExportOptions { Format = ExportFormat.Pdf, Style = style, TypewriterFont = typewriter };
        var bytes = PdfWriter.Write(ManuscriptBuilder.Build(SampleBook(), options), options);

        Assert.Equal("%PDF", Encoding.ASCII.GetString(bytes, 0, 4));
        using var pdf = PdfSharp.Pdf.IO.PdfReader.Open(new MemoryStream(bytes), PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import);
        Assert.Equal(3, pdf.PageCount);   // title page + one page per chapter
    }

    // ── Files ────────────────────────────────────────────────────────────────

    [Fact]
    public void Export_OneFilePerChapter_IsAZip()
    {
        var file = ExportService.Export(SampleBook(), new ExportOptions { Format = ExportFormat.Markdown, FilePerChapter = true });
        Assert.Equal("The Long Winter- A Harbour Mystery - chapters.zip", file.FileName);
        using var zip = new ZipArchive(new MemoryStream(file.Data));
        Assert.Equal(["01 Chapter 1- Arrival.md", "02 Chapter 2- The Docks.md"], zip.Entries.Select(e => e.FullName));
    }

    [Fact]
    public void Export_SingleChapter_IsNamedAfterIt()
    {
        var book = SampleBook();
        var file = ExportService.Export(book, new ExportOptions { Format = ExportFormat.Text, ChapterIds = [book.Chapters[0].Id] });
        Assert.Equal("The Long Winter- A Harbour Mystery - Chapter 1- Arrival.txt", file.FileName);
    }

    [Fact]
    public void Export_NoChapters_Throws() =>
        Assert.Throws<InvalidOperationException>(() => ExportService.Export(new Book { Title = "Empty" }, new ExportOptions()));

    // ── Export panel (view model) ────────────────────────────────────────────

    private static WriterViewModel ViewModelFor(Book book)
    {
        var vm = new WriterViewModel(new Mock<IBookService>().Object, new Mock<IImageService>().Object,
            new Mock<INlpAnalysisService>().Object, new Mock<INlpModelManager>().Object,
            new Mock<INerService>().Object, new Mock<IPosTaggingService>().Object,
            new Mock<ILocationHeuristicService>().Object, new Mock<INlpCacheService>().Object);
        typeof(WriterViewModel).GetField("selectedBook", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(vm, book);
        return vm;
    }

    [Fact]
    public void Panel_ChoicesBecomeExportOptions()
    {
        var vm = ViewModelFor(SampleBook());
        vm.ExportFormat = "PDF";
        vm.ExportStyle = "Reading copy";
        vm.ExportHeadings = "Title only";
        vm.ExportSceneBreak = "Blank line";
        vm.ExportTypewriterFont = true;

        var options = vm.BuildExportOptions();
        Assert.Equal(ExportFormat.Pdf, options.Format);
        Assert.Equal(ExportStyle.ReadingCopy, options.Style);
        Assert.Equal(ChapterHeadingStyle.Title, options.ChapterHeadings);
        Assert.Equal(string.Empty, options.SceneBreak);
        Assert.True(options.TypewriterFont);
        Assert.Null(options.ChapterIds);   // whole book
    }

    [Fact]
    public void Panel_ExportChapterTicksOnlyThatChapter()
    {
        var book = SampleBook();
        var vm = ViewModelFor(book);
        vm.ExportChapterCommand.Execute(book.Chapters[1]);

        Assert.Equal(Workspace.Book, vm.ActiveWorkspace);
        Assert.False(vm.ExportWholeBook);
        Assert.Equal([false, true], vm.ExportChapters.Select(c => c.IsSelected));
        Assert.Equal([book.Chapters[1].Id], vm.BuildExportOptions().ChapterIds);
        Assert.Equal("Chapter 2: The Docks", vm.ExportChapters[1].Label);
    }

    [Fact]
    public void Panel_StyleOnlyAppliesToWordAndPdf()
    {
        var vm = ViewModelFor(SampleBook());
        vm.ExportFormat = "Markdown";
        Assert.False(vm.IsExportStyleRelevant);
        vm.ExportFormat = "Word (.docx)";
        Assert.True(vm.IsExportStyleRelevant);
    }
}
