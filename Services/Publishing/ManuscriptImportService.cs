using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using EBookDashboard.Models;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Exceptions;

namespace EBookDashboard.Services.Publishing;

/// <summary>One structural unit produced by manuscript import, prior to database persistence.</summary>
public sealed class ImportedSection
{
    public int OrderIndex { get; set; }

    /// <summary>Index (in the same result list) of the logical parent (e.g. a Heading2 under the preceding Heading1). Null = top-level.</summary>
    public int? ParentOrderIndex { get; set; }

    public MatterType MatterType { get; set; } = MatterType.Body;

    public SectionKind SectionKind { get; set; } = SectionKind.Chapter;

    public string? Title { get; set; }

    public string ContentHtml { get; set; } = string.Empty;

    public bool StartsOnRecto { get; set; }
}

/// <summary>Import statistics surfaced to the upload UI.</summary>
public sealed record ManuscriptImportStats(int ChapterCount, int WordCount, int ImageCount);

/// <summary>Full result of a manuscript import pass.</summary>
public sealed class ManuscriptImportResult
{
    public List<ImportedSection> Sections { get; set; } = new();

    public ManuscriptImportStats Stats { get; set; } = new(0, 0, 0);

    public ManuscriptSourceFormat SourceFormat { get; set; }
}

/// <summary>
/// Converts an uploaded DOCX/PDF manuscript into structured <see cref="ImportedSection"/> entries
/// (Heading1 → Chapter, Heading2 → Section, fallback regex/font-size heading detection),
/// preserving basic rich formatting as HTML and extracting embedded images to disk.
/// </summary>
public sealed class ManuscriptImportService
{
    private static readonly Regex ChapterHeadingRegex = new(
        @"^(Chapter|CHAPTER|Part)\s+([0-9]+|[IVXLC]+)\b",
        RegexOptions.Compiled);

    private static readonly string[] FrontMatterKeywords =
    [
        "title page", "half title", "copyright", "dedication", "epigraph",
        "table of contents", "contents", "foreword", "preface", "acknowledgments",
        "acknowledgements", "introduction"
    ];

    private static readonly string[] BackMatterKeywords =
    [
        "appendix", "glossary", "index", "bibliography", "about the author",
        "about author", "afterword", "author's note", "authors note", "references"
    ];

    private readonly ILogger<ManuscriptImportService> _logger;

    public ManuscriptImportService(ILogger<ManuscriptImportService> logger)
    {
        _logger = logger;
    }

    /// <summary>Imports a .docx manuscript, preserving heading structure, inline formatting, lists, and images.</summary>
    public async Task<ManuscriptImportResult> ImportDocxAsync(
        Stream stream,
        string fileName,
        int projectId,
        string storageRoot,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms, cancellationToken);
        ms.Position = 0;

        var (imagesDir, imagesUrlBase) = ResolveImageStorage(projectId, storageRoot);
        var imageCounter = 0;

        List<ImportedSection> sections;
        try
        {
            using var doc = WordprocessingDocument.Open(ms, false);
            var mainPart = doc.MainDocumentPart;
            var body = mainPart?.Document?.Body;
            if (mainPart == null || body == null)
                throw new InvalidOperationException("This Word file has no readable content. Re-save it as .docx and upload again.");

            var bodyFontSizePt = EstimateBodyFontSizePt(body);
            sections = BuildDocxSections(body, mainPart, projectId, imagesDir, imagesUrlBase, bodyFontSizePt, ref imageCounter);
        }
        catch (Exception ex) when (ex is FileFormatException or InvalidDataException or OpenXmlPackageException)
        {
            throw new InvalidOperationException(
                "This Word file could not be read. Re-save it as .docx in Microsoft Word or Google Docs, then upload again.");
        }

        var stats = ComputeStats(sections, imageCounter);
        return new ManuscriptImportResult
        {
            Sections = sections,
            Stats = stats,
            SourceFormat = ManuscriptSourceFormat.Docx
        };
    }

    /// <summary>
    /// Imports a .pdf manuscript using PdfPig reading-order text extraction. Headings are detected
    /// by relative font size plus the shared Chapter/Part regex. Password-protected and image-only
    /// (scanned) PDFs raise a friendly, actionable message instead of failing silently.
    /// </summary>
    public async Task<ManuscriptImportResult> ImportPdfAsync(
        Stream stream,
        string fileName,
        int projectId,
        string storageRoot,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms, cancellationToken);
        ms.Position = 0;
        var bytes = ms.ToArray();

        var (imagesDir, imagesUrlBase) = ResolveImageStorage(projectId, storageRoot);
        var imageCounter = 0;

        List<ImportedSection> sections;
        try
        {
            using var document = PdfDocument.Open(
                new MemoryStream(bytes, writable: false),
                new UglyToad.PdfPig.ParsingOptions { UseLenientParsing = true });

            var pages = document.GetPages().ToList();
            var totalLetters = pages.Sum(p => p.Letters?.Count ?? 0);
            if (totalLetters == 0)
            {
                throw new InvalidOperationException(
                    "This PDF appears to contain no extractable text — it's likely a scanned or image-only document. " +
                    "OCR is not supported for manuscript import yet. Please upload a text-based PDF or a Word (.docx) file instead.");
            }

            var medianFontSize = EstimateMedianFontSize(pages);
            sections = BuildPdfSections(pages, medianFontSize, projectId, imagesDir, imagesUrlBase, ref imageCounter, cancellationToken);
        }
        catch (PdfDocumentEncryptedException)
        {
            throw new InvalidOperationException(
                "This PDF is password-protected. Remove the password (or export an unprotected copy) and upload again.");
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "PDF manuscript import failed for {FileName}", fileName);
            throw new InvalidOperationException(
                "This PDF couldn't be read automatically — it may be scanned (image-only), encrypted, or use an unusual format. " +
                "Save it as a Word (.docx) file instead, or try a different export of the PDF.");
        }

        var stats = ComputeStats(sections, imageCounter);
        return new ManuscriptImportResult
        {
            Sections = sections,
            Stats = stats,
            SourceFormat = ManuscriptSourceFormat.Pdf
        };
    }

    // ───────────────────────────── DOCX ─────────────────────────────

    private List<ImportedSection> BuildDocxSections(
        Body body,
        MainDocumentPart mainPart,
        int projectId,
        string imagesDir,
        string imagesUrlBase,
        double bodyFontSizePt,
        ref int imageCounter)
    {
        var sections = new List<ImportedSection>();
        var blocks = new List<(bool IsListItem, string Html)>();
        string? currentTitle = null;
        int? currentParentIndex = null;
        var chapterNo = 0;
        var localImageCounter = imageCounter;

        void Flush()
        {
            if (blocks.Count == 0 && string.IsNullOrEmpty(currentTitle))
                return;

            var contentHtml = WrapListBlocks(blocks);
            blocks.Clear();
            if (contentHtml.Length == 0 && string.IsNullOrEmpty(currentTitle))
                return;

            chapterNo++;
            var title = string.IsNullOrWhiteSpace(currentTitle) ? $"Chapter {chapterNo}" : currentTitle!.Trim();
            var (matter, kind) = ClassifySection(title, isTopLevel: currentParentIndex == null);
            sections.Add(new ImportedSection
            {
                OrderIndex = sections.Count,
                ParentOrderIndex = currentParentIndex,
                MatterType = matter,
                SectionKind = kind,
                Title = title,
                ContentHtml = contentHtml,
                StartsOnRecto = currentParentIndex == null
            });
            currentTitle = null;
        }

        int? lastChapterSectionIndex = null;

        foreach (var para in body.Elements<Paragraph>())
        {
            var text = (para.InnerText ?? string.Empty).Trim();
            var styleId = para.ParagraphProperties?.ParagraphStyleId?.Val?.Value ?? string.Empty;

            var isHeading1 = IsHeading1(styleId, text, para, bodyFontSizePt);
            var isHeading2 = !isHeading1 && IsHeading2(styleId, text, para, bodyFontSizePt);

            if (isHeading1 && text.Length > 0)
            {
                Flush();
                currentTitle = text;
                currentParentIndex = null;
                continue;
            }

            if (isHeading2 && text.Length > 0)
            {
                // Close out any accumulated body text into the current chapter before starting a sub-section.
                Flush();
                lastChapterSectionIndex ??= sections.Count - 1;
                currentTitle = text;
                currentParentIndex = sections.Count - 1 >= 0 ? sections.Count - 1 : null;
                continue;
            }

            var (html, isList) = ConvertParagraphToHtml(para, mainPart, imagesDir, imagesUrlBase, ref localImageCounter);
            if (html.Length > 0)
                blocks.Add((isList, html));
        }

        Flush();
        imageCounter = localImageCounter;

        if (sections.Count == 0)
        {
            // No heading boundaries found at all — the whole document becomes chapter 1.
            var everything = WrapListBlocks(blocks);
            sections.Add(new ImportedSection
            {
                OrderIndex = 0,
                MatterType = MatterType.Body,
                SectionKind = SectionKind.Chapter,
                Title = "Chapter 1",
                ContentHtml = everything,
                StartsOnRecto = true
            });
        }

        return sections;
    }

    private static bool IsHeading1(string styleId, string text, Paragraph para, double bodyFontSizePt)
    {
        if (styleId.Equals("Heading1", StringComparison.OrdinalIgnoreCase) ||
            styleId.Equals("Title", StringComparison.OrdinalIgnoreCase))
            return true;

        if (text.Length > 0 && text.Length <= 120 && ChapterHeadingRegex.IsMatch(text))
            return true;

        var runSize = GetFirstRunFontSizePt(para);
        return runSize.HasValue && bodyFontSizePt > 0
            && runSize.Value >= bodyFontSizePt * 1.4
            && text.Length is > 0 and <= 100;
    }

    private static bool IsHeading2(string styleId, string text, Paragraph para, double bodyFontSizePt)
    {
        if (styleId.Equals("Heading2", StringComparison.OrdinalIgnoreCase))
            return true;

        var runSize = GetFirstRunFontSizePt(para);
        return runSize.HasValue && bodyFontSizePt > 0
            && runSize.Value >= bodyFontSizePt * 1.15
            && runSize.Value < bodyFontSizePt * 1.4
            && text.Length is > 0 and <= 100;
    }

    private static double? GetFirstRunFontSizePt(Paragraph para)
    {
        foreach (var run in para.Elements<Run>())
        {
            var szVal = run.RunProperties?.FontSize?.Val?.Value;
            if (szVal != null && double.TryParse(szVal, out var halfPoints))
                return halfPoints / 2.0;
        }
        return null;
    }

    private static double EstimateBodyFontSizePt(Body body)
    {
        var sizes = new Dictionary<double, int>();
        foreach (var run in body.Descendants<Run>())
        {
            var szVal = run.RunProperties?.FontSize?.Val?.Value;
            if (szVal != null && double.TryParse(szVal, out var halfPoints))
            {
                var pt = halfPoints / 2.0;
                sizes[pt] = sizes.GetValueOrDefault(pt) + (run.InnerText?.Length ?? 1);
            }
        }
        return sizes.Count == 0 ? 11.0 : sizes.OrderByDescending(kv => kv.Value).First().Key;
    }

    private static (string Html, bool IsListItem) ConvertParagraphToHtml(
        Paragraph para,
        MainDocumentPart mainPart,
        string imagesDir,
        string imagesUrlBase,
        ref int imageCounter)
    {
        var jVal = para.ParagraphProperties?.Justification?.Val;
        var alignStyle = "";
        if (jVal != null)
        {
            var j = jVal.Value;
            if (j == JustificationValues.Center) alignStyle = "text-align:center;";
            else if (j == JustificationValues.Right) alignStyle = "text-align:right;";
            else if (j == JustificationValues.Both) alignStyle = "text-align:justify;";
        }
        var isListItem = para.ParagraphProperties?.NumberingProperties != null;

        var sb = new StringBuilder();
        var localCounter = imageCounter;
        foreach (var run in para.Elements<Run>())
        {
            var imgHtml = ExtractRunImages(run, mainPart, imagesDir, imagesUrlBase, ref localCounter);
            if (imgHtml.Length > 0)
            {
                sb.Append(imgHtml);
                continue;
            }

            var text = run.InnerText;
            if (string.IsNullOrEmpty(text))
                continue;

            var encoded = WebUtility.HtmlEncode(text).Replace("\n", "<br/>");
            var props = run.RunProperties;
            if (props?.Bold != null && props.Bold.Val?.Value != false)
                encoded = $"<strong>{encoded}</strong>";
            if (props?.Italic != null && props.Italic.Val?.Value != false)
                encoded = $"<em>{encoded}</em>";
            if (props?.Underline != null && props.Underline.Val?.Value != UnderlineValues.None)
                encoded = $"<u>{encoded}</u>";
            sb.Append(encoded);
        }
        imageCounter = localCounter;

        var inner = sb.ToString();
        if (string.IsNullOrWhiteSpace(inner))
            return (string.Empty, false);

        var tag = isListItem ? "li" : "p";
        var style = alignStyle.Length > 0 ? $" style=\"{alignStyle}\"" : string.Empty;
        return ($"<{tag}{style}>{inner}</{tag}>", isListItem);
    }

    private static string ExtractRunImages(
        Run run,
        MainDocumentPart mainPart,
        string imagesDir,
        string imagesUrlBase,
        ref int imageCounter)
    {
        var sb = new StringBuilder();
        foreach (var blip in run.Descendants<DocumentFormat.OpenXml.Drawing.Blip>())
        {
            var relId = blip.Embed?.Value;
            if (string.IsNullOrEmpty(relId))
                continue;
            try
            {
                if (mainPart.GetPartById(relId) is not ImagePart part)
                    continue;
                using var stream = part.GetStream();
                using var imgMs = new MemoryStream();
                stream.CopyTo(imgMs);
                var bytes = imgMs.ToArray();
                if (bytes.Length == 0)
                    continue;

                imageCounter++;
                var ext = ExtensionFromContentType(part.ContentType);
                var savedFileName = $"img_{Guid.NewGuid():N}{ext}";
                Directory.CreateDirectory(imagesDir);
                File.WriteAllBytes(Path.Combine(imagesDir, savedFileName), bytes);
                var url = $"{imagesUrlBase}/{savedFileName}";
                sb.Append("<p class=\"manuscript-figure\" style=\"text-align:center;margin:1em 0;\">")
                  .Append("<img src=\"").Append(url).Append("\" style=\"max-width:100%;height:auto;\" alt=\"\" /></p>");
            }
            catch
            {
                // Skip unreadable embedded image; import continues.
            }
        }
        return sb.ToString();
    }

    private static string WrapListBlocks(List<(bool IsListItem, string Html)> blocks)
    {
        var sb = new StringBuilder();
        var inList = false;
        foreach (var (isListItem, html) in blocks)
        {
            if (isListItem && !inList)
            {
                sb.Append("<ul>");
                inList = true;
            }
            else if (!isListItem && inList)
            {
                sb.Append("</ul>");
                inList = false;
            }
            sb.Append(html);
        }
        if (inList)
            sb.Append("</ul>");
        return sb.ToString();
    }

    // ───────────────────────────── PDF ─────────────────────────────

    private List<ImportedSection> BuildPdfSections(
        List<Page> pages,
        double medianFontSize,
        int projectId,
        string imagesDir,
        string imagesUrlBase,
        ref int imageCounter,
        CancellationToken cancellationToken)
    {
        var sections = new List<ImportedSection>();
        var paragraphBuffer = new List<string>();
        string? currentTitle = null;
        var chapterNo = 0;
        var localImageCounter = imageCounter;

        void FlushParagraph(StringBuilder into)
        {
            if (paragraphBuffer.Count == 0)
                return;
            var text = string.Join(" ", paragraphBuffer).Trim();
            paragraphBuffer.Clear();
            if (text.Length == 0)
                return;
            into.Append("<p>").Append(WebUtility.HtmlEncode(text)).Append("</p>");
        }

        var contentBuilder = new StringBuilder();

        void FlushSection()
        {
            FlushParagraph(contentBuilder);
            var html = contentBuilder.ToString();
            contentBuilder.Clear();
            if (html.Length == 0 && string.IsNullOrEmpty(currentTitle))
                return;

            chapterNo++;
            var title = string.IsNullOrWhiteSpace(currentTitle) ? $"Chapter {chapterNo}" : currentTitle!.Trim();
            var (matter, kind) = ClassifySection(title, isTopLevel: true);
            sections.Add(new ImportedSection
            {
                OrderIndex = sections.Count,
                ParentOrderIndex = null,
                MatterType = matter,
                SectionKind = kind,
                Title = title,
                ContentHtml = html,
                StartsOnRecto = true
            });
            currentTitle = null;
        }

        foreach (var page in pages)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Best-effort embedded image extraction (position-agnostic — appended as figures per page image count only).
            try
            {
                foreach (var img in page.GetImages())
                {
                    if (img.TryGetPng(out var png) && png.Length > 0)
                    {
                        localImageCounter++;
                        Directory.CreateDirectory(imagesDir);
                        var savedFileName = $"img_{Guid.NewGuid():N}.png";
                        File.WriteAllBytes(Path.Combine(imagesDir, savedFileName), png);
                    }
                }
            }
            catch
            {
                // Non-fatal: some PDF image encodings are not convertible to PNG.
            }

            var lines = BuildReadingOrderLines(page);
            foreach (var (lineText, avgFontSize) in lines)
            {
                var text = lineText.Trim();
                if (text.Length == 0)
                    continue;

                var isH1 = (text.Length <= 120 && ChapterHeadingRegex.IsMatch(text))
                    || (medianFontSize > 0 && avgFontSize >= medianFontSize * 1.4 && text.Length <= 100);
                var isH2 = !isH1 && medianFontSize > 0
                    && avgFontSize >= medianFontSize * 1.15 && avgFontSize < medianFontSize * 1.4
                    && text.Length <= 100;

                if (isH1)
                {
                    FlushSection();
                    currentTitle = text;
                    continue;
                }

                if (isH2)
                {
                    // PdfPig text has no reliable parent linkage without heavier layout analysis;
                    // sub-headings are kept as a bold inline paragraph within the current chapter.
                    FlushParagraph(contentBuilder);
                    contentBuilder.Append("<p><strong>").Append(WebUtility.HtmlEncode(text)).Append("</strong></p>");
                    continue;
                }

                paragraphBuffer.Add(text);
            }
        }

        FlushSection();
        imageCounter = localImageCounter;

        if (sections.Count == 0)
        {
            sections.Add(new ImportedSection
            {
                OrderIndex = 0,
                MatterType = MatterType.Body,
                SectionKind = SectionKind.Chapter,
                Title = "Chapter 1",
                ContentHtml = contentBuilder.ToString(),
                StartsOnRecto = true
            });
        }

        return sections;
    }

    /// <summary>Reconstructs approximate reading-order lines from PdfPig words using vertical clustering.</summary>
    private static List<(string Text, double AvgFontSize)> BuildReadingOrderLines(Page page)
    {
        var words = page.GetWords().ToList();
        if (words.Count == 0)
            return new List<(string, double)>();

        var ordered = words.OrderByDescending(w => w.BoundingBox.Top).ThenBy(w => w.BoundingBox.Left).ToList();
        var lines = new List<List<Word>>();
        const double lineToleranceRatio = 0.4;

        foreach (var word in ordered)
        {
            var wordHeight = Math.Max(1.0, word.BoundingBox.Top - word.BoundingBox.Bottom);
            var line = lines.Count > 0 ? lines[^1] : null;
            if (line != null && Math.Abs(line[0].BoundingBox.Top - word.BoundingBox.Top) < wordHeight * lineToleranceRatio + 2)
            {
                line.Add(word);
            }
            else
            {
                lines.Add(new List<Word> { word });
            }
        }

        var result = new List<(string, double)>();
        foreach (var line in lines)
        {
            var sortedLine = line.OrderBy(w => w.BoundingBox.Left).ToList();
            var text = string.Join(" ", sortedLine.Select(w => w.Text));
            var sizes = sortedLine.SelectMany(w => w.Letters).Select(l => l.FontSize).Where(s => s > 0).ToList();
            var avgSize = sizes.Count > 0 ? sizes.Average() : 0.0;
            result.Add((text, avgSize));
        }
        return result;
    }

    private static double EstimateMedianFontSize(List<Page> pages)
    {
        var sizes = pages.SelectMany(p => p.Letters).Select(l => l.FontSize).Where(s => s > 0).OrderBy(s => s).ToList();
        if (sizes.Count == 0)
            return 0;
        return sizes[sizes.Count / 2];
    }

    // ───────────────────────────── Shared helpers ─────────────────────────────

    private static (MatterType Matter, SectionKind Kind) ClassifySection(string title, bool isTopLevel)
    {
        var lower = title.ToLowerInvariant();

        if (lower.Contains("title page")) return (MatterType.Front, SectionKind.TitlePage);
        if (lower.Contains("copyright")) return (MatterType.Front, SectionKind.Copyright);
        if (lower.Contains("dedication")) return (MatterType.Front, SectionKind.Dedication);
        if (lower.Contains("table of contents") || lower == "contents") return (MatterType.Front, SectionKind.TableOfContents);
        if (lower.Contains("appendix")) return (MatterType.Back, SectionKind.Appendix);
        if (lower.Contains("index")) return (MatterType.Back, SectionKind.Index);
        if (lower.Contains("about the author") || lower.Contains("about author")) return (MatterType.Back, SectionKind.AboutAuthor);

        if (FrontMatterKeywords.Any(k => lower.Contains(k)))
            return (MatterType.Front, SectionKind.Custom);
        if (BackMatterKeywords.Any(k => lower.Contains(k)))
            return (MatterType.Back, SectionKind.Custom);

        return (MatterType.Body, isTopLevel ? SectionKind.Chapter : SectionKind.Section);
    }

    private static (string Dir, string UrlBase) ResolveImageStorage(int projectId, string storageRoot)
    {
        var dir = Path.Combine(storageRoot, "uploads", "projects", projectId.ToString(), "images");
        Directory.CreateDirectory(dir);
        var urlBase = $"/uploads/projects/{projectId}/images";
        return (dir, urlBase);
    }

    private static string ExtensionFromContentType(string? contentType)
    {
        return (contentType ?? string.Empty).ToLowerInvariant() switch
        {
            var ct when ct.Contains("png") => ".png",
            var ct when ct.Contains("jpeg") || ct.Contains("jpg") => ".jpg",
            var ct when ct.Contains("gif") => ".gif",
            var ct when ct.Contains("bmp") => ".bmp",
            var ct when ct.Contains("tiff") => ".tiff",
            var ct when ct.Contains("webp") => ".webp",
            _ => ".png"
        };
    }

    private static ManuscriptImportStats ComputeStats(List<ImportedSection> sections, int imageCount)
    {
        var chapterCount = sections.Count(s => s.SectionKind == SectionKind.Chapter);
        var wordCount = sections.Sum(s => CountWords(s.ContentHtml));
        return new ManuscriptImportStats(chapterCount, wordCount, imageCount);
    }

    private static readonly Regex HtmlTagRegex = new("<[^>]+>", RegexOptions.Compiled);

    private static int CountWords(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return 0;
        var plain = HtmlTagRegex.Replace(html, " ");
        plain = WebUtility.HtmlDecode(plain);
        return plain.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
    }
}
