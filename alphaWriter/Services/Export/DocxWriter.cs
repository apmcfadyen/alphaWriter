using System.Globalization;

namespace alphaWriter.Services.Export
{
    // Imported inside the namespace so these names win over MAUI's project-wide usings
    // (e.g. FontSize, Font), which differ by platform and clash on Android.
    using DocumentFormat.OpenXml;
    using DocumentFormat.OpenXml.Packaging;
    using DocumentFormat.OpenXml.Wordprocessing;

    /// <summary>
    /// Word (.docx), which Google Docs and LibreOffice also open. Standard manuscript format:
    /// 12 pt, double-spaced, 0.5" indents, 1" margins, "Surname / TITLE / page" header,
    /// a title page with the word count, each chapter on a new page a third of the way down.
    /// </summary>
    public static class DocxWriter
    {
        private const int Twip = 1440;               // twips per inch
        private const int TextWidth = 6 * Twip + Twip / 2;   // Letter width minus 1" margins

        public static byte[] Write(Manuscript m, ExportOptions options)
        {
            bool manuscript = options.Style == ExportStyle.StandardManuscript;
            var font = options.TypewriterFont ? "Courier New" : manuscript ? "Times New Roman" : "Georgia";

            using var stream = new MemoryStream();
            using (var doc = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
            {
                var main = doc.AddMainDocumentPart();
                AddStyles(main, font, manuscript);
                var body = new Body();

                if (manuscript) AddManuscriptTitlePage(body, m);
                else AddReadingTitlePage(body, m);

                foreach (var chapter in m.Chapters)
                {
                    body.Append(Heading(chapter.Heading, manuscript));
                    for (int s = 0; s < chapter.Scenes.Count; s++)
                    {
                        var scene = chapter.Scenes[s];
                        if (s > 0) body.Append(Centered(options.SceneBreak));
                        if (options.IncludeSceneTitles) body.Append(Centered(scene.Title, italic: true));
                        for (int p = 0; p < scene.Paragraphs.Count; p++)
                        {
                            // Reading copies follow book convention: no indent after a heading or break
                            bool indent = manuscript || p > 0;
                            body.Append(BodyParagraph(scene.Paragraphs[p], indent));
                        }
                    }
                }

                body.Append(SectionProperties(main, m, manuscript));
                main.Document = new Document(body);
                main.Document.Save();
            }
            return stream.ToArray();
        }

        private static void AddStyles(MainDocumentPart main, string font, bool manuscript)
        {
            var styles = main.AddNewPart<StyleDefinitionsPart>();
            styles.Styles = new Styles(
                new DocDefaults(
                    new RunPropertiesDefault(new RunPropertiesBaseStyle(
                        new RunFonts { Ascii = font, HighAnsi = font, ComplexScript = font, EastAsia = font },
                        new FontSize { Val = manuscript ? "24" : "23" },
                        new Languages { Val = "en-US" })),
                    new ParagraphPropertiesDefault(new ParagraphPropertiesBaseStyle(
                        new SpacingBetweenLines
                        {
                            Before = "0", After = "0",
                            Line = manuscript ? "480" : "300", LineRule = LineSpacingRuleValues.Auto
                        }))),
                new Style(new StyleName { Val = "Normal" }, new PrimaryStyle())
                { Type = StyleValues.Paragraph, StyleId = "Normal", Default = true });
            styles.Styles.Save();
        }

        private static void AddManuscriptTitlePage(Body body, Manuscript m)
        {
            var words = $"about {ManuscriptBuilder.RoundedWordCount(m.WordCount).ToString("#,0", CultureInfo.InvariantCulture)} words";
            // Author on the left, word count on the right, on one line
            body.Append(new Paragraph(
                new ParagraphProperties(new Tabs(new TabStop { Val = TabStopValues.Right, Position = TextWidth })),
                Run(m.Author), new Run(new TabChar()), Run(words)));

            body.Append(new Paragraph(
                new ParagraphProperties(
                    new SpacingBetweenLines { Before = (3 * Twip).ToString(CultureInfo.InvariantCulture) },
                    new Justification { Val = JustificationValues.Center }),
                Run(m.Title.ToUpperInvariant())));
            if (m.Author.Length > 0) body.Append(Centered($"by {m.Author}"));
        }

        private static void AddReadingTitlePage(Body body, Manuscript m)
        {
            body.Append(new Paragraph(
                new ParagraphProperties(
                    new SpacingBetweenLines { Before = (3 * Twip).ToString(CultureInfo.InvariantCulture), After = "240" },
                    new Justification { Val = JustificationValues.Center }),
                new Run(new RunProperties(new FontSize { Val = "48" }), new Text(m.Title))));
            if (m.Author.Length > 0) body.Append(Centered(m.Author, italic: true));
        }

        private static Paragraph Heading(string text, bool manuscript) => new(
            new ParagraphProperties(
                new PageBreakBefore(),
                new SpacingBetweenLines
                {
                    Before = (manuscript ? 2 * Twip : Twip).ToString(CultureInfo.InvariantCulture),
                    After = manuscript ? "0" : "480"
                },
                new Justification { Val = JustificationValues.Center }),
            new Run(new RunProperties(manuscript ? new FontSize { Val = "24" } : new FontSize { Val = "32" }), new Text(text)));

        private static Paragraph Centered(string text, bool italic = false) => new(
            new ParagraphProperties(new Justification { Val = JustificationValues.Center }),
            italic ? new Run(new RunProperties(new Italic()), Text(text)) : Run(text));

        private static Paragraph BodyParagraph(ManuscriptParagraph p, bool indent)
        {
            var paragraph = new Paragraph(new ParagraphProperties(
                new Indentation { FirstLine = indent ? (Twip / 2).ToString(CultureInfo.InvariantCulture) : "0" }));
            foreach (var r in p.Runs)
            {
                var props = new RunProperties();
                if (r.Bold) props.Append(new Bold());
                if (r.Italic) props.Append(new Italic());
                if (r.Underline) props.Append(new Underline { Val = UnderlineValues.Single });
                paragraph.Append(props.HasChildren ? new Run(props, Text(r.Text)) : new Run(Text(r.Text)));
            }
            return paragraph;
        }

        private static Run Run(string text) => new(Text(text));

        private static Text Text(string text) => new(text) { Space = SpaceProcessingModeValues.Preserve };

        private static SectionProperties SectionProperties(MainDocumentPart main, Manuscript m, bool manuscript)
        {
            var section = new SectionProperties(
                new PageSize { Width = 12240, Height = 15840 },
                new PageMargin
                {
                    Top = Twip, Bottom = Twip, Left = (uint)Twip, Right = (uint)Twip,
                    Header = (uint)(Twip / 2), Footer = (uint)(Twip / 2), Gutter = 0
                });
            if (!manuscript) return section;

            // "Surname / TITLE / page", right-aligned, on every page but the title page
            var surname = m.Author.Split(' ', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? string.Empty;
            var label = (surname.Length > 0 ? $"{surname} / " : string.Empty) + $"{ShortTitle(m.Title).ToUpperInvariant()} / ";
            var header = main.AddNewPart<HeaderPart>();
            header.Header = new Header(new Paragraph(
                new ParagraphProperties(new Justification { Val = JustificationValues.Right }),
                Run(label),
                new SimpleField(new Run(Text("1"))) { Instruction = " PAGE " }));
            header.Header.Save();
            var firstPage = main.AddNewPart<HeaderPart>();
            firstPage.Header = new Header(new Paragraph());
            firstPage.Header.Save();

            section.InsertAt(new HeaderReference { Type = HeaderFooterValues.Default, Id = main.GetIdOfPart(header) }, 0);
            section.InsertAt(new HeaderReference { Type = HeaderFooterValues.First, Id = main.GetIdOfPart(firstPage) }, 1);
            section.Append(new TitlePage());
            return section;
        }

        /// <summary>Manuscript headers use a keyword or two of the title, not all of it.</summary>
        internal static string ShortTitle(string title)
        {
            var main = title.Split(':', 2)[0].Trim();
            var words = main.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return words.Length <= 3 ? main : string.Join(' ', words.Take(3));
        }
    }
}
