using System.Text;
using System.Text.RegularExpressions;
using EBookDashboard.Services.PdfExport;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

public static class FixtureFactory
{
    public static void EnsureAll(string corpusDir, string repoRoot, Action<string> log)
    {
        Directory.CreateDirectory(corpusDir);
        ExportPdfFontResolver.EnsureRegistered(Path.Combine(repoRoot, "wwwroot", "fonts", "pdf"));


        // Parts > Chapters hierarchy (synthetic from Pride text, enough body for ≥24 pages)
        var pride = Path.Combine(corpusDir, "pride-prejudice.txt");
        var partsPath = Path.Combine(corpusDir, "parts-chapters.txt");
        if (File.Exists(pride))
        {
            var text = StripGutenberg(File.ReadAllText(pride));
            var matches = Regex.Matches(text, @"(?m)^(CHAPTER\s+[IVXLC\d]+\.?)\s*$");
            var sb = new StringBuilder();
            sb.AppendLine("Part One").AppendLine();
            for (var i = 0; i < matches.Count; i++)
            {
                if (i == 20) sb.AppendLine().AppendLine("Part Two").AppendLine();
                if (i == 40) sb.AppendLine().AppendLine("Part Three").AppendLine();
                var start = matches[i].Index;
                var end = i + 1 < matches.Count ? matches[i + 1].Index : Math.Min(text.Length, start + 8000);
                var slice = text[start..end].Trim();
                if (slice.Length > 6000) slice = slice[..6000];
                sb.AppendLine(slice).AppendLine();
            }
            File.WriteAllText(partsPath, sb.ToString());
            log($"Wrote parts-chapters.txt ({new FileInfo(partsPath).Length} bytes)");
        }

        // Strip PG headers on cached plain texts used as manuscripts
        foreach (var name in new[] { "christmas-carol.txt", "pride-prejudice.txt", "leaves-of-grass.txt", "large-800.txt" })
        {
            var p = Path.Combine(corpusDir, name);
            if (!File.Exists(p)) continue;
            var raw = File.ReadAllText(p);
            var stripped = StripGutenberg(raw);
            if (stripped.Length < raw.Length - 500)
            {
                File.WriteAllText(p, stripped);
                log($"Stripped Gutenberg boilerplate: {name}");
            }
        }

        // Truncated War & Peace as ~40 fat chapters (≈800 pages at 11pt) — fewer batches, same volume.
        var war = Path.Combine(corpusDir, "war-and-peace.txt");
        var large = Path.Combine(corpusDir, "large-800.txt");
        if (File.Exists(war))
        {
            var t = StripGutenberg(File.ReadAllText(war));
            if (t.Length > 1_200_000) t = t[..1_200_000];
            // Split only on paragraph boundaries so chapter markers never bisect a word.
            var paras = Regex.Split(t.Replace("\r\n", "\n"), @"\n\s*\n")
                .Select(p => p.Trim())
                .Where(p => p.Length > 0)
                .ToList();
            var sbLarge = new StringBuilder();
            const int chapterCount = 40;
            var perChapter = Math.Max(1, (paras.Count + chapterCount - 1) / chapterCount);
            for (var i = 0; i < chapterCount; i++)
            {
                var slice = paras.Skip(i * perChapter).Take(perChapter).ToList();
                if (slice.Count == 0) break;
                sbLarge.AppendLine($"Chapter {i + 1}").AppendLine();
                var body = string.Join("\n\n", slice);
                body = Regex.Replace(body, @"(?m)^CHAPTER\s+[IVXLC\d]+\.?\s*$", "§");
                sbLarge.AppendLine(body.Trim()).AppendLine();
            }
            File.WriteAllText(large, sbLarge.ToString());
            log($"Wrote large-800.txt ({new FileInfo(large).Length} bytes, {chapterCount} chapters)");
        }

        // Oversized source (full W&P) — used to force >828 pages when fully rendered;
        // we render a dense subset that still exceeds via small font OR use preflight on synthetic.
        var oversized = Path.Combine(corpusDir, "oversized.txt");
        if (File.Exists(war) && !File.Exists(oversized))
        {
            // Keep enough text that at 11pt easily exceeds 828 pages (~3MB)
            File.Copy(war, oversized, overwrite: true);
            log("Wrote oversized.txt (full War and Peace)");
        }

        // Two-column PDF
        var twoCol = Path.Combine(corpusDir, "two-column.pdf");
        if (!File.Exists(twoCol))
        {
            WriteTwoColumnPdf(twoCol);
            log("Wrote two-column.pdf");
        }

        var carol = Path.Combine(corpusDir, "christmas-carol.txt");
        var calibre = Path.Combine(corpusDir, "calibre-export.pdf");
        // Always refresh generated PDFs so page-count ≥24
        {
            WriteSimpleTextPdf(calibre, "A Christmas Carol (calibre-like)",
                File.Exists(carol) ? File.ReadAllText(carol) : "Chapter 1\n\nIt was cold.");
            log("Wrote calibre-export.pdf");
        }

        var wordOut = Path.Combine(corpusDir, "word-export.pdf");
        {
            WriteSimpleTextPdf(wordOut, "Word-exported sample",
                File.Exists(carol) ? File.ReadAllText(carol) : "Chapter 1\n\nHello world.");
            log("Wrote word-export.pdf");
        }

        // Scanned image-only PDF
        var scanned = Path.Combine(corpusDir, "scanned.pdf");
        if (!File.Exists(scanned))
        {
            WriteScannedPdf(scanned);
            log("Wrote scanned.pdf");
        }

        EnsureOversizedPdf(corpusDir);
        log("Ensured oversized-900.pdf");

        // Copy Zero to One into corpus if available
        var z2oSrc = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Downloads", "Zero to One.pdf");
        var z2oDst = Path.Combine(corpusDir, "zero-to-one.pdf");
        if (File.Exists(z2oSrc) && !File.Exists(z2oDst))
        {
            File.Copy(z2oSrc, z2oDst, true);
            log("Copied zero-to-one.pdf");
        }
    }

    public static List<BookSpec> Describe(string corpusDir, string repoRoot)
    {
        string P(string name) => Path.Combine(corpusDir, name);
        return new List<BookSpec>
        {
            new()
            {
                Id = "short", Title = "A Christmas Carol", Author = "Charles Dickens",
                Path = P("christmas-carol.txt"), Ext = ".txt", MinChapters = 1,
                EstimatedPagesHint = 40
            },
            new()
            {
                Id = "novel-50ch", Title = "Pride and Prejudice", Author = "Jane Austen",
                Path = P("pride-prejudice.txt"), Ext = ".txt", MinChapters = 50,
                EstimatedPagesHint = 350
            },
            new()
            {
                Id = "nonfiction-images", Title = "Zero to One", Author = "Peter Thiel",
                Genre = "Business", Path = P("zero-to-one.pdf"), Ext = ".pdf",
                MinChapters = 10, ExpectImages = true, EstimatedPagesHint = 200
            },
            new()
            {
                Id = "parts-chapters", Title = "Pride and Prejudice (Parts)", Author = "Jane Austen",
                Path = P("parts-chapters.txt"), Ext = ".txt", MinChapters = 3,
                RequireParts = true, EstimatedPagesHint = 250
            },
            new()
            {
                Id = "poetry", Title = "Leaves of Grass", Author = "Walt Whitman",
                Path = P("leaves-of-grass.txt"), Ext = ".txt", MinChapters = 1,
                EstimatedPagesHint = 80, MaxChaptersForRender = 20
            },
            new()
            {
                Id = "large-800", Title = "War and Peace (truncated ~800pp)", Author = "Leo Tolstoy",
                Path = P("large-800.txt"), Ext = ".txt", MinChapters = 10,
                EstimatedPagesHint = 800
            },
            new()
            {
                Id = "two-column", Title = "Two-column source", Author = "Corpus",
                Path = P("two-column.pdf"), Ext = ".pdf", MinChapters = 1, ImportOnly = true
            },
            new()
            {
                Id = "word-pdf", Title = "Word-exported PDF", Author = "Corpus",
                Path = P("word-export.pdf"), Ext = ".pdf", MinChapters = 1, EstimatedPagesHint = 30
            },
            new()
            {
                Id = "calibre-pdf", Title = "Calibre-exported PDF", Author = "Corpus",
                Path = P("calibre-export.pdf"), Ext = ".pdf", MinChapters = 1, EstimatedPagesHint = 40
            },
            new()
            {
                Id = "epub", Title = "Pride and Prejudice (EPUB)", Author = "Jane Austen",
                Path = P("pride-prejudice.epub"), Ext = ".epub", MinChapters = 10,
                EstimatedPagesHint = 300
            },
            new()
            {
                Id = "scanned", Title = "Scanned PDF", Author = "Corpus",
                Path = P("scanned.pdf"), Ext = ".pdf", ExpectScannedReject = true
            },
            new()
            {
                // Synthetic 900-page PDF — proves KDP PAGE_COUNT preflight FAILs with clear guidance.
                Id = "oversized-limit", Title = "Synthetic 900-page interior", Author = "Corpus",
                Path = P("oversized-900.pdf"), Ext = ".pdf", MinChapters = 1,
                EstimatedPagesHint = 900, ExpectPreflightFail = true, ImportOnly = false
            },
        };
    }

    public static void EnsureOversizedPdf(string corpusDir)
    {
        var path = Path.Combine(corpusDir, "oversized-900.pdf");
        if (File.Exists(path) && new FileInfo(path).Length > 1000) return;
        using var doc = new PdfDocument();
        var font = new XFont("Cormorant Garamond", 10);
        for (var i = 0; i < 900; i++)
        {
            var page = doc.AddPage();
            page.Width = XUnit.FromInch(6);
            page.Height = XUnit.FromInch(9);
            using var gfx = XGraphics.FromPdfPage(page);
            gfx.DrawString($"Oversized page {i + 1} of 900 — KDP limit test.", font, XBrushes.Black, 40, 60);
        }
        doc.Save(path);
    }

    public static string StripGutenberg(string text)
    {
        text ??= "";
        text = Regex.Replace(text, @"\*\*\*\s*START OF[\s\S]*?\*\*\*", "", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"\*\*\*\s*END OF[\s\S]*", "", RegexOptions.IgnoreCase);
        text = Regex.Replace(text,
            @"Project Gutenberg(?:'s)?[\s\S]{0,4000}?(?=CHAPTER|Chapter|Stave|STAVE|BOOK|Book|I\.\s|1\.\s)",
            " ", RegexOptions.IgnoreCase);
        text = Regex.Replace(text,
            @"(?:This\s+ebook\s+is\s+for\s+the\s+use\s+of\s+anyone[\s\S]{0,8000})",
            " ", RegexOptions.IgnoreCase);
        return text.Trim();
    }

    static void WriteTwoColumnPdf(string path)
    {
        using var doc = new PdfDocument();
        var page = doc.AddPage();
        page.Width = XUnit.FromInch(8.5);
        page.Height = XUnit.FromInch(11);
        using var gfx = XGraphics.FromPdfPage(page);
        var font = new XFont("Cormorant Garamond", 11);
        double colW = page.Width.Point * 0.42;
        double leftX = 40;
        double rightX = page.Width.Point * 0.52;
        double yL = 50, yR = 50;
        for (var i = 0; i < 40; i++)
        {
            gfx.DrawString($"LEFTCOLUMN line {i + 1} alpha beta gamma.", font, XBrushes.Black,
                new XRect(leftX, yL, colW, 14), XStringFormats.TopLeft);
            yL += 14;
            gfx.DrawString($"RIGHTCOLUMN line {i + 1} delta epsilon zeta.", font, XBrushes.Black,
                new XRect(rightX, yR, colW, 14), XStringFormats.TopLeft);
            yR += 14;
        }
        doc.Save(path);
    }

    static void WriteSimpleTextPdf(string path, string title, string text)
    {
        using var doc = new PdfDocument();
        var font = new XFont("Cormorant Garamond", 11);
        var titleFont = new XFont("Cormorant Garamond", 16, XFontStyleEx.Bold);
        var paragraphs = Regex.Split(text.Replace("\r\n", "\n"), @"\n\s*\n")
            .Select(p => Regex.Replace(p, @"\s+", " ").Trim())
            .Where(p => p.Length > 0)
            .ToList();
        // Repeat content until the *source PDF itself* is ≥40 pages (Chromium re-render
        // with KDP margins/leading needs headroom over the 24-page minimum).
        while (paragraphs.Count < 600)
            paragraphs.AddRange(paragraphs.Take(Math.Min(paragraphs.Count, 80)).ToList());
        paragraphs = paragraphs.Take(800).ToList();

        PdfPage? page = null;
        XGraphics? gfx = null;
        double y = 0;
        void NewPage()
        {
            gfx?.Dispose();
            page = doc.AddPage();
            page.Width = XUnit.FromInch(6);
            page.Height = XUnit.FromInch(9);
            gfx = XGraphics.FromPdfPage(page);
            y = 50;
        }

        NewPage();
        gfx!.DrawString(title, titleFont, XBrushes.Black, 40, y);
        y += 28;
        foreach (var para in paragraphs)
        {
            var words = para.Split(' ');
            var line = "";
            foreach (var w in words)
            {
                var trial = string.IsNullOrEmpty(line) ? w : line + " " + w;
                if (gfx.MeasureString(trial, font).Width > page!.Width.Point - 80)
                {
                    if (y > page.Height.Point - 50) NewPage();
                    gfx!.DrawString(line, font, XBrushes.Black, 40, y);
                    y += 14;
                    line = w;
                }
                else line = trial;
            }
            if (!string.IsNullOrEmpty(line))
            {
                if (y > page!.Height.Point - 50) NewPage();
                gfx!.DrawString(line, font, XBrushes.Black, 40, y);
                y += 18;
            }
        }
        // Hard floor: source must be ≥40 pages so Chromium re-render clears KDP's 24-page min.
        var filler = 0;
        while (doc.PageCount < 40)
        {
            NewPage();
            for (var i = 0; i < 35 && y < page!.Height.Point - 50; i++)
            {
                gfx!.DrawString($"Padding paragraph {++filler} for KDP page-count headroom.", font, XBrushes.Black, 40, y);
                y += 16;
            }
        }
        gfx?.Dispose();
        doc.Save(path);
    }

    static void WriteScannedPdf(string path)
    {
        using var img = new Image<Rgba32>(600, 800);
        for (var y = 0; y < img.Height; y++)
        for (var x = 0; x < img.Width; x++)
            img[x, y] = new Rgba32((byte)(200 + (x + y) % 40), (byte)(195 + x % 30), (byte)(190 + y % 30));

        using var ms = new MemoryStream();
        img.SaveAsJpeg(ms);
        var jpeg = ms.ToArray();

        using var doc = new PdfDocument();
        for (var i = 0; i < 3; i++)
        {
            var page = doc.AddPage();
            page.Width = XUnit.FromInch(6);
            page.Height = XUnit.FromInch(9);
            using var gfx = XGraphics.FromPdfPage(page);
            using var ximg = XImage.FromStream(new MemoryStream(jpeg));
            gfx.DrawImage(ximg, 0, 0, page.Width.Point, page.Height.Point);
        }
        doc.Save(path);
    }
}
