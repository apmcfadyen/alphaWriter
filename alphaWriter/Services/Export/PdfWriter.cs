using System.Globalization;

namespace alphaWriter.Services.Export
{
    // Imported inside the namespace so these names win over MAUI's project-wide usings
    // (e.g. FontSize, Font), which differ by platform and clash on Android.
    using MigraDoc.DocumentObjectModel;
    using MigraDoc.Rendering;
    using PdfSharp.Fonts;

    /// <summary>
    /// PDF via MigraDoc, with the same two layouts as the Word export. Fonts are bundled
    /// (Literata and Courier Prime, both SIL Open Font License) so PDFs look the same on
    /// Windows and Android.
    /// </summary>
    public static class PdfWriter
    {
        public static byte[] Write(Manuscript m, ExportOptions options)
        {
            PdfFonts.EnsureResolver();
            bool manuscript = options.Style == ExportStyle.StandardManuscript;

            var document = new Document();
            document.Info.Title = m.Title;
            document.Info.Author = m.Author;

            var normal = document.Styles[StyleNames.Normal]!;
            normal.Font.Name = options.TypewriterFont ? PdfFonts.Typewriter : PdfFonts.Serif;
            normal.Font.Size = manuscript ? 12 : 11.5;
            normal.ParagraphFormat.LineSpacingRule = manuscript ? LineSpacingRule.Double : LineSpacingRule.OnePtFive;

            var section = document.AddSection();
            var page = section.PageSetup;
            page.PageFormat = PageFormat.Letter;
            page.TopMargin = page.BottomMargin = page.LeftMargin = page.RightMargin = Unit.FromInch(1);
            page.HeaderDistance = page.FooterDistance = Unit.FromInch(0.5);
            page.DifferentFirstPageHeaderFooter = true;

            if (manuscript)
            {
                var surname = m.Author.Split(' ', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? string.Empty;
                var header = section.Headers.Primary.AddParagraph();
                header.Format.Alignment = ParagraphAlignment.Right;
                header.Format.LineSpacingRule = LineSpacingRule.Single;
                header.AddText((surname.Length > 0 ? $"{surname} / " : string.Empty) +
                               $"{DocxWriter.ShortTitle(m.Title).ToUpperInvariant()} / ");
                header.AddPageField();
                AddManuscriptTitlePage(section, m);
            }
            else
            {
                var footer = section.Footers.Primary.AddParagraph();
                footer.Format.Alignment = ParagraphAlignment.Center;
                footer.AddPageField();
                AddReadingTitlePage(section, m);
            }

            foreach (var chapter in m.Chapters)
            {
                var heading = section.AddParagraph(chapter.Heading);
                heading.Format.PageBreakBefore = true;
                heading.Format.Alignment = ParagraphAlignment.Center;
                heading.Format.SpaceBefore = Unit.FromInch(manuscript ? 2 : 1);
                heading.Format.SpaceAfter = manuscript ? Unit.Zero : Unit.FromPoint(24);
                if (!manuscript) heading.Format.Font.Size = 16;

                for (int s = 0; s < chapter.Scenes.Count; s++)
                {
                    var scene = chapter.Scenes[s];
                    if (s > 0) Centered(section, options.SceneBreak.Length > 0 ? options.SceneBreak : " ");
                    if (options.IncludeSceneTitles) Centered(section, scene.Title).Format.Font.Italic = true;
                    for (int p = 0; p < scene.Paragraphs.Count; p++)
                    {
                        var paragraph = section.AddParagraph();
                        paragraph.Format.FirstLineIndent = manuscript || p > 0 ? Unit.FromInch(0.5) : Unit.Zero;
                        foreach (var run in scene.Paragraphs[p].Runs)
                        {
                            var text = paragraph.AddFormattedText(run.Text);
                            text.Bold = run.Bold;
                            text.Italic = run.Italic;
                            if (run.Underline) text.Underline = Underline.Single;
                        }
                    }
                }
            }

            var renderer = new PdfDocumentRenderer { Document = document };
            renderer.RenderDocument();
            using var stream = new MemoryStream();
            renderer.PdfDocument.Save(stream, false);
            return stream.ToArray();
        }

        private static void AddManuscriptTitlePage(Section section, Manuscript m)
        {
            var top = section.AddParagraph();
            top.Format.LineSpacingRule = LineSpacingRule.Single;
            top.Format.AddTabStop(Unit.FromInch(6.5), TabAlignment.Right);
            top.AddText(m.Author);
            top.AddTab();
            top.AddText($"about {ManuscriptBuilder.RoundedWordCount(m.WordCount).ToString("#,0", CultureInfo.InvariantCulture)} words");

            var title = Centered(section, m.Title.ToUpperInvariant());
            title.Format.SpaceBefore = Unit.FromInch(3);
            if (m.Author.Length > 0) Centered(section, $"by {m.Author}");
        }

        private static void AddReadingTitlePage(Section section, Manuscript m)
        {
            var title = Centered(section, m.Title);
            title.Format.SpaceBefore = Unit.FromInch(3);
            title.Format.Font.Size = 24;
            if (m.Author.Length > 0) Centered(section, m.Author).Format.Font.Italic = true;
        }

        private static MigraDoc.DocumentObjectModel.Paragraph Centered(Section section, string text)
        {
            var p = section.AddParagraph(text);
            p.Format.Alignment = ParagraphAlignment.Center;
            p.Format.FirstLineIndent = Unit.Zero;
            return p;
        }
    }

    /// <summary>
    /// The bundled PDF fonts. PDFsharp needs every font supplied explicitly (there are no
    /// system fonts on Android), so font files are registered by name before rendering.
    /// </summary>
    public static class PdfFonts
    {
        public const string Serif = "Literata";
        public const string Typewriter = "Courier Prime";

        /// <summary>Font file names under Resources/Raw/fonts, without extension.</summary>
        public static readonly string[] Files =
        [
            "Literata-Regular", "Literata-Bold", "Literata-Italic", "Literata-BoldItalic",
            "CourierPrime-Regular", "CourierPrime-Bold", "CourierPrime-Italic", "CourierPrime-BoldItalic"
        ];

        private static readonly Dictionary<string, byte[]> Faces = new(StringComparer.Ordinal);
        private static readonly object Gate = new();
        private static bool _resolverSet;

        public static bool IsLoaded { get { lock (Gate) return Faces.Count == Files.Length; } }

        /// <summary>Registers a font file's bytes by face name (e.g. "Literata-Italic").</summary>
        public static void Add(string face, byte[] data)
        {
            lock (Gate) Faces[face] = data;
        }

        /// <summary>Loads the bundled fonts from the app package (once).</summary>
        public static async Task LoadBundledAsync()
        {
            if (IsLoaded) return;
            foreach (var face in Files)
            {
                await using var stream = await FileSystem.OpenAppPackageFileAsync($"fonts/{face}.ttf");
                using var memory = new MemoryStream();
                await stream.CopyToAsync(memory);
                Add(face, memory.ToArray());
            }
        }

        internal static void EnsureResolver()
        {
            lock (Gate)
            {
                if (_resolverSet) return;
                GlobalFontSettings.FontResolver = new BundledFontResolver();
                _resolverSet = true;
            }
        }

        private sealed class BundledFontResolver : IFontResolver
        {
            public FontResolverInfo? ResolveTypeface(string familyName, bool bold, bool italic)
            {
                var family = familyName.Replace(" ", string.Empty, StringComparison.Ordinal)
                    .Equals("CourierPrime", StringComparison.OrdinalIgnoreCase) ? "CourierPrime" : "Literata";
                var style = (bold, italic) switch
                {
                    (true, true) => "BoldItalic",
                    (true, false) => "Bold",
                    (false, true) => "Italic",
                    _ => "Regular"
                };
                return new FontResolverInfo($"{family}-{style}");
            }

            public byte[]? GetFont(string faceName)
            {
                lock (Gate)
                    return Faces.TryGetValue(faceName, out var data)
                        ? data
                        : throw new InvalidOperationException($"PDF font '{faceName}' is not loaded.");
            }
        }
    }
}
