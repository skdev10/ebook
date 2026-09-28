using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using HtmlAgilityPack;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Exceptions;

namespace EBookDashboard.Services;

/// <summary>Reads plain text from uploaded chapter documents for AI Writer import.</summary>
public static class ChapterDocumentImportService
{
    private static readonly string[] SupportedExtensions = [".pdf", ".txt", ".text", ".md", ".markdown", ".docx"];

    private const int MaxImportedChars = 2_000_000;

    public static bool IsSupportedExtension(string ext)
        => SupportedExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase);

    public static string SupportedFormatsMessage()
        => "Supported formats: PDF, Word (.docx), plain text (.txt), and Markdown (.md).";

    /// <summary>Guess extension from filename, content type, or file signature.</summary>
    public static string ResolveExtension(string? fileName, string? contentType, byte[] bytes)
    {
        var ext = Path.GetExtension(fileName ?? string.Empty).ToLowerInvariant();
        if (!string.IsNullOrEmpty(ext))
            return ext;

        var ct = contentType ?? string.Empty;
        if (ct.Contains("pdf", StringComparison.OrdinalIgnoreCase))
            return ".pdf";
        if (ct.Contains("wordprocessingml", StringComparison.OrdinalIgnoreCase)
            || ct.Contains("msword", StringComparison.OrdinalIgnoreCase))
            return ".docx";
        if (ct.StartsWith("text/", StringComparison.OrdinalIgnoreCase)
            || ct.Contains("markdown", StringComparison.OrdinalIgnoreCase))
            return ".txt";

        if (bytes.Length >= 4 && bytes[0] == 0x25 && bytes[1] == 0x50 && bytes[2] == 0x44 && bytes[3] == 0x46)
            return ".pdf";
        if (bytes.Length >= 4 && bytes[0] == 0x50 && bytes[1] == 0x4B)
            return ".docx";

        return string.Empty;
    }

    /// <summary>Extract plain text from uploaded bytes based on resolved extension.</summary>
    public static string ExtractText(byte[] bytes, string ext, CancellationToken cancellationToken = default)
    {
        ext = ext.ToLowerInvariant();
        if (ext == ".doc")
            throw new InvalidOperationException("Old Word .doc files are not supported. Open the file in Word and Save As .docx, then upload again.");

        if (ext == ".pdf")
            return ExtractPdfTextAsPlain(bytes, cancellationToken);
        if (ext == ".docx")
            return ExtractDocxTextAsPlain(bytes);
        if (ext is ".txt" or ".text" or ".md" or ".markdown")
            return DecodeTextFile(bytes);

        throw new InvalidOperationException(SupportedFormatsMessage());
    }

    /// <summary>
    /// Import a manuscript for Writer/Formatting: keep preface, chapter 1, in-body headings,
    /// and embedded figures (DOCX + PDF). Plain-text split is only a fallback.
    /// </summary>
    public static (string PlainText, List<ImportedChapter> Chapters) ImportUploadedDocument(
        byte[] bytes,
        string ext,
        CancellationToken cancellationToken = default)
    {
        ext = (ext ?? string.Empty).ToLowerInvariant();
        if (ext == ".docx")
        {
            var docxChapters = ExtractDocxChapters(bytes, out var docxPlain);
            var text = SanitizeImportedText(docxPlain);
            var fallback = SplitIntoChapters(
                string.IsNullOrWhiteSpace(text) ? ExtractDocxTextAsPlain(bytes) : text);
            return (text, CoalesceSectionHeadingChapters(PreferRicherChapterSplit(docxChapters, fallback)));
        }

        if (ext == ".pdf")
        {
            var pdfChapters = ExtractPdfChapters(bytes, out var pdfPlain, cancellationToken);
            var text = SanitizeImportedText(pdfPlain);
            var fallback = SplitIntoChapters(
                string.IsNullOrWhiteSpace(text) ? ExtractPdfTextAsPlain(bytes, cancellationToken) : text);
            return (text, CoalesceSectionHeadingChapters(PreferRicherChapterSplit(pdfChapters, fallback)));
        }

        var plain = SanitizeImportedText(ExtractText(bytes, ext, cancellationToken));
        return (plain, CoalesceSectionHeadingChapters(SplitIntoChapters(plain)));
    }

    /// <summary>Exact PDF page count from the file catalog — not a word estimate.</summary>
    public static int CountPdfPages(byte[] bytes)
    {
        if (bytes == null || bytes.Length < 8)
            return 0;
        try
        {
            using var document = PdfDocument.Open(
                new MemoryStream(bytes, writable: false),
                new ParsingOptions { UseLenientParsing = true });
            return document.NumberOfPages;
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>Decode uploaded text/markdown bytes (UTF-8/16, BOM, common Windows encodings).</summary>
    public static string DecodeTextFile(byte[] bytes)
    {
        if (bytes.Length == 0)
            return string.Empty;

        if (bytes.Length >= 2)
        {
            if (bytes[0] == 0xFF && bytes[1] == 0xFE)
                return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
            if (bytes[0] == 0xFE && bytes[1] == 0xFF)
                return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
        }

        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);

        if (LooksLikeUtf16LeText(bytes))
            return Encoding.Unicode.GetString(bytes);

        var utf8 = Encoding.UTF8.GetString(bytes);
        if (!utf8.Contains('\uFFFD', StringComparison.Ordinal))
            return utf8;

        TryRegisterCodePages();
        try
        {
            return Encoding.GetEncoding(1252).GetString(bytes);
        }
        catch
        {
            return Encoding.Latin1.GetString(bytes);
        }
    }

    /// <summary>Extract readable text from a PDF byte array.</summary>
    public static string ExtractPdfTextAsPlain(byte[] bytes, CancellationToken cancellationToken = default)
    {
        try
        {
            using var document = PdfDocument.Open(
                new MemoryStream(bytes, writable: false),
                new ParsingOptions { UseLenientParsing = true });

            var pages = document.GetPages().ToList();
            var medianFont = EstimatePdfMedianFontSize(pages);
            var sb = new StringBuilder();
            foreach (var page in pages)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var lines = BuildPdfReadingOrderLines(page);
                    if (lines.Count > 0)
                    {
                        foreach (var (lineText, avgFont, isBold, isCentered, _) in lines)
                        {
                            var text = RepairPdfImportText(lineText);
                            if (text.Length == 0)
                                continue;
                            if (LooksLikePdfHeading(text, avgFont, medianFont, isBold, isCentered, out var mark))
                                sb.Append(mark).Append(' ').Append(text).AppendLine();
                            else
                                sb.AppendLine(text);
                        }
                        continue;
                    }

                    var pageText = RepairPdfImportText(page.Text ?? string.Empty);
                    if (pageText.Length > 0)
                    {
                        sb.AppendLine(pageText);
                        continue;
                    }

                    if (page.Letters is { Count: > 0 })
                    {
                        foreach (var letter in page.Letters)
                            sb.Append(letter.Value);
                        sb.AppendLine();
                    }
                }
                catch
                {
                    // Never drop a page silently — first-half loss (Preface + Ch 1–7) came from this.
                    try
                    {
                        var fallback = RepairPdfImportText(page.Text ?? string.Empty);
                        if (fallback.Length > 0)
                            sb.AppendLine(fallback);
                        else if (page.Letters is { Count: > 0 })
                        {
                            foreach (var letter in page.Letters)
                                sb.Append(letter.Value);
                            sb.AppendLine();
                        }
                    }
                    catch
                    {
                        /* page truly unreadable */
                    }
                }
            }

            return sb.ToString();
        }
        catch (PdfDocumentEncryptedException)
        {
            throw new InvalidOperationException("This PDF is password-protected. Export a copy without a password or paste the text instead.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            // Some PDFs (scanned, damaged, or built with unusual encoders) defeat even lenient
            // parsing. Surface a clear, actionable message instead of a dead-end generic error.
            throw new InvalidOperationException(
                "This PDF couldn't be read automatically — it may be scanned (image-only) or use an unusual format. "
                + "Save it as a Word (.docx) or .txt file, or paste the text instead.");
        }
    }

    /// <summary>Extract paragraph text from a Word .docx file.</summary>
    public static string ExtractDocxTextAsPlain(byte[] bytes)
    {
        try
        {
            using var ms = new MemoryStream(bytes, writable: false);
            using var doc = WordprocessingDocument.Open(ms, false);
            var body = doc.MainDocumentPart?.Document?.Body;
            if (body == null)
                return string.Empty;

            var sb = new StringBuilder();
            foreach (var para in body.Descendants<Paragraph>())
            {
                var line = para.InnerText?.Trim();
                if (!string.IsNullOrEmpty(line))
                    sb.AppendLine(line);
            }

            return sb.ToString();
        }
        catch (Exception ex) when (ex is FileFormatException or InvalidDataException or OpenXmlPackageException)
        {
            throw new InvalidOperationException("This Word file could not be read. Re-save it as .docx in Microsoft Word or Google Docs, then upload again.");
        }
    }

    /// <summary>
    /// Rich .docx extraction: returns chapters whose <see cref="ImportedChapter.Body"/> is HTML
    /// (paragraphs + inline base64 images), split on Word "Heading" styles or "Chapter N" lines.
    /// Embedded images are kept at their position so they survive into preview + PDF export.
    /// </summary>
    public static List<ImportedChapter> ExtractDocxChapters(byte[] bytes, out string combinedPlainText)
    {
        combinedPlainText = string.Empty;
        var chapters = new List<ImportedChapter>();
        try
        {
            using var ms = new MemoryStream(bytes, writable: false);
            using var doc = WordprocessingDocument.Open(ms, false);
            var mainPart = doc.MainDocumentPart;
            var body = mainPart?.Document?.Body;
            if (mainPart == null || body == null)
                return chapters;

            var bodyFontPt = EstimateDocxBodyFontSizePt(body);
            var plain = new StringBuilder();
            var curTitle = string.Empty;
            var curBody = new StringBuilder();
            var curNo = 0;
            var sawBookTitle = false;

            var items = new List<(string Text, int? Level, string Imgs, string StyleId)>();
            foreach (var para in body.Descendants<Paragraph>())
            {
                var text = (para.InnerText ?? string.Empty).Trim();
                var headingLevel = ResolveDocxHeadingLevel(para, mainPart, bodyFontPt);
                var imgs = ExtractParagraphImagesHtml(para, mainPart);
                var styleId = para.ParagraphProperties?.ParagraphStyleId?.Val?.Value ?? string.Empty;
                if (text.Length == 0 && imgs.Length == 0)
                    continue;
                items.Add((text, headingLevel, imgs, styleId));
            }

            void Flush()
            {
                var html = PromoteHeadingParagraphs(curBody.ToString().Trim());
                if (html.Length == 0 && string.IsNullOrEmpty(curTitle))
                    return;
                curNo++;
                var title = string.IsNullOrWhiteSpace(curTitle)
                    ? (chapters.Count == 0 && items.Any(it =>
                        IsDocxChapterBoundary(it.StyleId, it.Text) || IsExplicitChapterMarker(it.Text) || IsFrontMatterMarker(it.Text))
                        ? "Preface"
                        : $"Chapter {curNo}")
                    : curTitle;
                chapters.Add(new ImportedChapter(curNo, TruncateSuggestedTitle(title), html));
                curBody.Clear();
                curTitle = string.Empty;
            }

            foreach (var (text, headingLevel, imgs, styleId) in items)
            {
                // Book title (Word Title style) is not a chapter — skip it once at the top.
                if (!sawBookTitle
                    && headingLevel == 1
                    && IsTitleStyleId(styleId)
                    && !IsChapterHeadingLine(text)
                    && text.Length > 0)
                {
                    sawBookTitle = true;
                    plain.AppendLine("# " + text);
                    continue;
                }

                var isChapterStart = text.Length > 0 && (
                    IsDocxChapterBoundary(styleId, text)
                    || (LooksLikeChapterTitle(text) && curBody.Length >= 280));

                if (isChapterStart)
                {
                    if (curBody.Length > 0 || !string.IsNullOrEmpty(curTitle))
                        Flush();
                    curTitle = text;
                    plain.AppendLine("# " + text);
                    if (imgs.Length > 0) curBody.Append(imgs);
                    continue;
                }

                if (headingLevel is >= 1 and <= 6 && text.Length > 0)
                {
                    var lvl = headingLevel == 1 ? 2 : headingLevel.Value;
                    curBody.Append("<h").Append(lvl)
                        .Append(" class=\"manuscript-heading manuscript-h").Append(lvl).Append("\">")
                        .Append(System.Net.WebUtility.HtmlEncode(text))
                        .Append("</h").Append(lvl).Append('>');
                    plain.Append('#', lvl).Append(' ').AppendLine(text);
                    if (imgs.Length > 0) curBody.Append(imgs);
                    continue;
                }

                if (text.Length > 0)
                {
                    curBody.Append("<p>").Append(System.Net.WebUtility.HtmlEncode(text)).Append("</p>");
                    plain.AppendLine(text);
                }
                if (imgs.Length > 0)
                    curBody.Append(imgs);
            }

            Flush();
            if (chapters.Count == 1)
            {
                var resplit = ResplitSingleChapterOnHeadings(chapters[0]);
                if (resplit.Count >= 2)
                {
                    chapters.Clear();
                    chapters.AddRange(resplit);
                }
            }
            combinedPlainText = plain.ToString();
        }
        catch (Exception ex) when (ex is FileFormatException or InvalidDataException or OpenXmlPackageException)
        {
            throw new InvalidOperationException("This Word file could not be read. Re-save it as .docx in Microsoft Word or Google Docs, then upload again.");
        }

        return chapters;
    }

    /// <summary>
    /// Rich PDF extraction: reading-order text + inline figures, then split so preface,
    /// chapter 1, section headings, and diagrams all survive into preview/PDF.
    /// </summary>
    public static List<ImportedChapter> ExtractPdfChapters(
        byte[] bytes,
        out string combinedPlainText,
        CancellationToken cancellationToken = default)
    {
        combinedPlainText = string.Empty;
        try
        {
            using var document = PdfDocument.Open(
                new MemoryStream(bytes, writable: false),
                new ParsingOptions { UseLenientParsing = true });

            var pages = document.GetPages().ToList();
            var medianFont = EstimatePdfMedianFontSize(pages);
            var html = new StringBuilder();
            var plain = new StringBuilder();

            foreach (var page in pages)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var blocks = new List<(double Top, int Tie, string Html, string Plain)>();
                var tie = 0;

                try
                {
                    foreach (var img in page.GetImages())
                    {
                        if (!TryDecodePdfImage(img, out var imgBytes, out var contentType))
                            continue;
                        blocks.Add((img.BoundingBox.Top, tie++, ToFigureHtml(imgBytes, contentType), "[figure]\n"));
                    }
                }
                catch
                {
                    /* some PDF image encodings are not readable */
                }

                try
                {
                    var lines = BuildPdfReadingOrderLines(page);
                    foreach (var (lineText, avgFont, isBold, isCentered, top) in lines)
                    {
                        var text = RepairPdfImportText(lineText);
                        if (text.Length == 0)
                            continue;
                        if (LooksLikeNumberedChapterBanner(text) || LooksLikePdfHeading(text, avgFont, medianFont, isBold, isCentered, out var mark))
                        {
                            if (!LooksLikePdfHeading(text, avgFont, medianFont, isBold, isCentered, out mark))
                                mark = "#";
                            if (LooksLikeNumberedChapterBanner(text))
                                mark = "#";
                            var lvl = mark == "#" ? 1 : Math.Clamp(mark.Length, 2, 6);
                            var headingHtml = "<h" + lvl + " class=\"manuscript-heading manuscript-h" + lvl + "\">"
                                              + System.Net.WebUtility.HtmlEncode(text)
                                              + "</h" + lvl + ">";
                            blocks.Add((top, tie++, headingHtml, mark + " " + text + "\n"));
                        }
                        else
                        {
                            blocks.Add((
                                top,
                                tie++,
                                "<p class=\"manuscript-p\">" + System.Net.WebUtility.HtmlEncode(text) + "</p>",
                                text + "\n"));
                        }
                    }
                }
                catch
                {
                    var fallback = RepairPdfImportText(page.Text ?? string.Empty);
                    if (fallback.Length > 0)
                        blocks.Add((0, tie++, "<p class=\"manuscript-p\">" + System.Net.WebUtility.HtmlEncode(fallback) + "</p>", fallback + "\n"));
                }

                if (blocks.Count == 0)
                    continue;

                foreach (var block in blocks.OrderByDescending(b => b.Top).ThenBy(b => b.Tie))
                {
                    html.Append(block.Html);
                    plain.Append(block.Plain);
                }
            }

            combinedPlainText = RepairPdfImportText(plain.ToString());
            var htmlDoc = PromoteHeadingParagraphs(html.ToString());
            var fromHtml = SplitHtmlDocumentIntoChapters(htmlDoc);
            if (fromHtml.Count > 0)
                return CoalesceSectionHeadingChapters(fromHtml);

            return CoalesceSectionHeadingChapters(SplitIntoChapters(combinedPlainText));
        }
        catch (PdfDocumentEncryptedException)
        {
            throw new InvalidOperationException("This PDF is password-protected. Export a copy without a password or paste the text instead.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception)
        {
            throw new InvalidOperationException(
                "This PDF couldn't be read automatically — it may be scanned (image-only) or use an unusual format. "
                + "Save it as a Word (.docx) or .txt file, or paste the text instead.");
        }
    }

    internal static string ToFigureHtml(byte[] bytes, string contentType)
    {
        var ct = string.IsNullOrWhiteSpace(contentType) ? "image/png" : contentType;
        bytes = ShrinkImportedImage(bytes, ref ct);
        return "<p class=\"manuscript-figure\" style=\"text-align:center;margin:1em 0;page-break-inside:avoid;\">"
               + "<img src=\"data:" + ct + ";base64," + Convert.ToBase64String(bytes)
               + "\" style=\"max-width:100%;height:auto;page-break-inside:avoid;\" alt=\"\" /></p>";
    }

    /// <summary>Downscale import figures so a 90-page PDF with diagrams does not hang the upload or browser.</summary>
    public static byte[] ShrinkImportedImage(byte[] bytes, ref string contentType)
    {
        if (bytes == null || bytes.Length < 800)
            return bytes ?? Array.Empty<byte>();
        try
        {
            using var image = Image.Load(bytes);
            const int maxEdge = 1200;
            if (image.Width > maxEdge || image.Height > maxEdge)
            {
                image.Mutate(x => x.Resize(new ResizeOptions
                {
                    Mode = ResizeMode.Max,
                    Size = new Size(maxEdge, maxEdge)
                }));
            }

            using var ms = new MemoryStream();
            image.SaveAsJpeg(ms, new JpegEncoder { Quality = 72 });
            if (ms.Length > 400 && ms.Length < bytes.Length)
            {
                contentType = "image/jpeg";
                return ms.ToArray();
            }
        }
        catch
        {
            /* keep original bytes */
        }

        return bytes;
    }

    internal static bool TryDecodePdfImage(IPdfImage img, out byte[] bytes, out string contentType)
    {
        bytes = Array.Empty<byte>();
        contentType = "image/png";
        if (img == null || img.IsImageMask)
            return false;

        if (img.WidthInSamples > 0 && img.HeightInSamples > 0
            && img.WidthInSamples < 32 && img.HeightInSamples < 32)
            return false;

        try
        {
            if (img.TryGetPng(out var png) && png is { Length: > 800 })
            {
                bytes = png;
                contentType = "image/png";
                return true;
            }
        }
        catch
        {
            /* JPEG and some filters cannot become PNG */
        }

        try
        {
            var raw = img.RawBytes;
            if (raw.Length > 800 && raw.Length >= 3 && raw[0] == 0xFF && raw[1] == 0xD8)
            {
                bytes = raw.ToArray();
                contentType = "image/jpeg";
                return true;
            }
        }
        catch
        {
            /* ignore */
        }

        return false;
    }

    /// <summary>Write data-URI figures to disk so large-book chapter HTML stays small enough to save.</summary>
    public static int MaterializeDataUriImages(List<ImportedChapter> chapters, string webRootPath, string relativeUrlDir)
    {
        if (chapters == null || chapters.Count == 0 || string.IsNullOrWhiteSpace(webRootPath))
            return 0;

        var rel = (relativeUrlDir ?? "").Replace('\\', '/').Trim('/');
        if (string.IsNullOrEmpty(rel))
            return 0;

        var absDir = Path.Combine(webRootPath, rel.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(absDir);

        var rx = new Regex(@"src=""data:(image/[^;""]+);base64,([A-Za-z0-9+/=]+)""",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        var saved = 0;
        for (var i = 0; i < chapters.Count; i++)
        {
            var body = chapters[i].Body ?? "";
            if (body.IndexOf("data:image", StringComparison.OrdinalIgnoreCase) < 0)
                continue;

            var rewritten = rx.Replace(body, m =>
            {
                try
                {
                    var bytes = Convert.FromBase64String(m.Groups[2].Value);
                    if (bytes.Length < 400)
                        return m.Value;
                    saved++;
                    var name = $"fig-{saved:0000}.jpg";
                    File.WriteAllBytes(Path.Combine(absDir, name), bytes);
                    return "src=\"/" + rel + "/" + name + "\"";
                }
                catch
                {
                    return m.Value;
                }
            });
            chapters[i] = chapters[i] with { Body = rewritten };
        }

        return saved;
    }

    public static List<ImportedChapter> SplitHtmlDocumentIntoChapters(string? html)
    {
        var result = new List<ImportedChapter>();
        if (string.IsNullOrWhiteSpace(html))
            return result;

        try
        {
            var doc = new HtmlDocument();
            doc.LoadHtml("<div id=\"root\">" + html + "</div>");
            var root = doc.GetElementbyId("root");
            if (root == null)
                return result;

            var nodes = root.ChildNodes.Where(n => n.NodeType == HtmlNodeType.Element).ToList();
            if (nodes.Count == 0)
                return result;

            string? currentTitle = null;
            var body = new StringBuilder();

            void Flush()
            {
                var content = body.ToString().Trim();
                body.Clear();
                if (content.Length == 0 && string.IsNullOrWhiteSpace(currentTitle))
                    return;
                var title = string.IsNullOrWhiteSpace(currentTitle)
                    ? (result.Count == 0 ? "Preface" : $"Chapter {result.Count + 1}")
                    : currentTitle!;
                result.Add(new ImportedChapter(result.Count + 1, TruncateSuggestedTitle(title), FormatChapterBodyHtml(content, title)));
                currentTitle = null;
            }

            foreach (var node in nodes)
            {
                var text = System.Net.WebUtility.HtmlDecode(node.InnerText ?? string.Empty).Trim();
                var isHeading = node.Name.Length == 2
                    && node.Name[0] is 'h' or 'H'
                    && char.IsDigit(node.Name[1]);
                var startsChapter = isHeading && text.Length > 0 && (
                    IsExplicitChapterMarker(text)
                    || IsFrontMatterMarker(text)
                    || LooksLikeNumberedChapterBanner(text)
                    || (node.Name.Equals("h1", StringComparison.OrdinalIgnoreCase)
                        && LooksLikeChapterTitle(text)
                        && !IsAllCapsSectionTitle(text)
                        && body.Length >= 280));

                if (startsChapter)
                {
                    Flush();
                    currentTitle = IsExplicitChapterMarker(text) || IsFrontMatterMarker(text)
                        ? ExtractExplicitChapterTitle(text)
                        : text;
                    continue;
                }

                body.Append(node.OuterHtml);
            }

            Flush();
        }
        catch
        {
            return result;
        }

        return result;
    }

    /// <summary>Reads inline images from a paragraph and returns centered, responsive base64 &lt;img&gt; blocks.</summary>
    private static string ExtractParagraphImagesHtml(Paragraph para, MainDocumentPart mainPart)
    {
        var sb = new StringBuilder();
        foreach (var blip in para.Descendants<DocumentFormat.OpenXml.Drawing.Blip>())
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
                var imageBytes = imgMs.ToArray();
                if (imageBytes.Length == 0)
                    continue;
                var contentType = string.IsNullOrWhiteSpace(part.ContentType) ? "image/png" : part.ContentType;
                var b64 = Convert.ToBase64String(imageBytes);
                sb.Append("<p class=\"manuscript-figure\" style=\"text-align:center;margin:1em 0;\">")
                  .Append("<img src=\"data:").Append(contentType).Append(";base64,").Append(b64)
                  .Append("\" style=\"max-width:100%;height:auto;\" alt=\"\" /></p>");
            }
            catch
            {
                /* skip unreadable image */
            }
        }
        return sb.ToString();
    }

    /// <summary>Normalize imported text for JSON + preview (strip nulls, invalid Unicode, cap length).</summary>
    public static string SanitizeImportedText(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        var cleaned = text.Replace("\0", string.Empty, StringComparison.Ordinal);
        cleaned = RemoveInvalidXmlChars(cleaned);

        try
        {
            cleaned = cleaned.Normalize(NormalizationForm.FormC);
        }
        catch (ArgumentException)
        {
            cleaned = new string(cleaned.Where(ch => !char.IsSurrogate(ch)).ToArray());
        }

        cleaned = cleaned.Trim();
        if (cleaned.Length > MaxImportedChars)
            cleaned = cleaned.Substring(0, MaxImportedChars) + "\n...[truncated]";
        return cleaned;
    }

    public static string SuggestBookTitleFromFileName(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return string.Empty;
        var baseName = Path.GetFileNameWithoutExtension(fileName.Trim());
        if (string.IsNullOrWhiteSpace(baseName))
            return string.Empty;
        baseName = Regex.Replace(baseName.Replace('_', ' '), @"\s+", " ").Trim();
        return baseName.Length > 200 ? baseName.Substring(0, 197) + "..." : baseName;
    }

    /// <summary>
    /// Best book-title guess, in priority order: .docx core metadata Title → first Title/Heading1
    /// paragraph → a Markdown "# Title" line → cleaned file name (last resort). Chapter markers
    /// (e.g. "Chapter 1") are never used as the book title.
    /// </summary>
    public static string ResolveSuggestedBookTitle(byte[]? bytes, string? ext, string? fileName, string? plainText)
    {
        if (!string.IsNullOrWhiteSpace(ext)
            && ext.Equals(".docx", StringComparison.OrdinalIgnoreCase)
            && bytes is { Length: > 0 })
        {
            var fromDoc = SuggestBookTitleFromDocx(bytes);
            if (!string.IsNullOrWhiteSpace(fromDoc))
                return TruncateSuggestedTitle(fromDoc);
        }

        var fromText = SuggestBookTitleFromText(plainText);
        if (!string.IsNullOrWhiteSpace(fromText))
            return TruncateSuggestedTitle(fromText);

        return SuggestBookTitleFromFileName(fileName);
    }

    private static string SuggestBookTitleFromDocx(byte[] bytes)
    {
        try
        {
            using var ms = new MemoryStream(bytes, writable: false);
            using var doc = WordprocessingDocument.Open(ms, false);

            var metaTitle = doc.PackageProperties?.Title?.Trim();
            if (!string.IsNullOrWhiteSpace(metaTitle) && !IsChapterHeadingLine(metaTitle!))
                return CleanCandidateTitle(metaTitle!);

            var body = doc.MainDocumentPart?.Document?.Body;
            if (body != null)
            {
                foreach (var para in body.Elements<Paragraph>())
                {
                    var styleId = para.ParagraphProperties?.ParagraphStyleId?.Val?.Value ?? string.Empty;
                    var text = (para.InnerText ?? string.Empty).Trim();
                    if (text.Length == 0)
                        continue;
                    var isTitleStyle = styleId.Equals("Title", StringComparison.OrdinalIgnoreCase)
                        || styleId.Equals("Heading1", StringComparison.OrdinalIgnoreCase);
                    if (isTitleStyle && text.Length <= 200 && !IsChapterHeadingLine(text))
                        return CleanCandidateTitle(text);
                    // Stop at the first body paragraph — the title, if any, leads the document.
                    if (!isTitleStyle && !styleId.StartsWith("Heading", StringComparison.OrdinalIgnoreCase))
                        break;
                }
            }
        }
        catch
        {
            /* fall through to other strategies */
        }
        return string.Empty;
    }

    private static string SuggestBookTitleFromText(string? plainText)
    {
        if (string.IsNullOrWhiteSpace(plainText))
            return string.Empty;
        var lines = plainText.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);
        var scanned = 0;
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0)
                continue;
            if (++scanned > 8)
                break;
            var md = Regex.Match(line, @"^#\s+(.{2,200})$");
            if (md.Success)
            {
                var t = md.Groups[1].Value;
                if (!IsChapterHeadingLine(t))
                    return CleanCandidateTitle(t);
            }
        }
        return string.Empty;
    }

    private static bool IsChapterHeadingLine(string? text) =>
        Regex.IsMatch(text ?? string.Empty,
            @"^(?:#+\s*)?(?:Chapter|CHAPTER|Part|PART|Prologue|Epilogue|Introduction|Conclusion|Section)\b",
            RegexOptions.IgnoreCase)
        || LooksLikeNumberedChapterBanner(text);

    /// <summary>
    /// Fix print-PDF artifacts from Zero-to-One style books: "billion- dollar" → "billion-dollar",
    /// drop-caps "S TART" / "A S" → "START" / "AS".
    /// </summary>
    public static string RepairPdfImportText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;
        var s = text.Replace("\u00AD", "").Trim();
        s = Regex.Replace(s, @"(\p{L})-\s+(\p{L})", "$1-$2");
        // Drop-cap only at the start of a line/paragraph — not every "A WORD".
        s = Regex.Replace(s, @"(?m)^([A-Z])\s+([A-Z]{2,})\b", "$1$2");
        s = Regex.Replace(s, @"(?m)^([A-Z])\s+([A-Z])\b(?=\s+[A-Z])", "$1$2");
        return s.Trim();
    }

    /// <summary>
    /// True only for a real chapter/part banner. Numbered points ("1. Setup") and
    /// words like Section/Introduction do not start a new chapter.
    /// </summary>
    internal static bool IsExplicitChapterMarker(string? text)
    {
        var t = Regex.Replace((text ?? string.Empty).Trim(), @"^#+\s*", string.Empty);
        if (t.Length == 0 || LooksLikePointHeading(t))
            return false;
        return Regex.IsMatch(t,
                   @"^(?:Chapter|CHAPTER)\s+(?:[0-9]+|[IVXLC]+|One|Two|Three|Four|Five|Six|Seven|Eight|Nine|Ten|Eleven|Twelve)\b",
                   RegexOptions.IgnoreCase)
               || Regex.IsMatch(t, @"^(?:Part|PART)\s+(?:[0-9]+|[IVXLC]+)\b", RegexOptions.IgnoreCase)
               || Regex.IsMatch(t, @"^(?:Prologue|Epilogue)\b", RegexOptions.IgnoreCase)
               || IsFrontMatterMarker(t)
               || LooksLikeNumberedChapterBanner(t);
    }

    /// <summary>Print-book banners like "10 THE MECHANICS OF MAFIA" (not a lone page number).</summary>
    internal static bool LooksLikeNumberedChapterBanner(string? text)
    {
        var t = Regex.Replace((text ?? string.Empty).Trim(), @"^#+\s*", string.Empty);
        if (t.Length == 0 || LooksLikePointHeading(t))
            return false;
        return Regex.IsMatch(t, @"^\d{1,2}\s+[A-Z][A-Z0-9\s,'’\-]{6,}$");
    }

    /// <summary>Preface / foreword / introduction banners must become their own chapters, not get dropped.</summary>
    internal static bool IsFrontMatterMarker(string? text)
    {
        var t = Regex.Replace((text ?? string.Empty).Trim(), @"^#+\s*", string.Empty);
        if (t.Length == 0)
            return false;
        return Regex.IsMatch(
            t,
            @"^(Preface|Foreword|Introduction|Acknowledgments?|Acknowledgements?|Dedication|Contents|Table of Contents)\b",
            RegexOptions.IgnoreCase);
    }

    /// <summary>Imported TOC dump titles — app builds Contents; never persist/export these as body chapters.</summary>
    internal static bool IsImportedContentsTitle(string? title)
    {
        var t = Regex.Replace((title ?? "").Trim().ToLowerInvariant(), @"\s+", " ");
        return t is "contents" or "table of contents" or "toc";
    }

    private static string ExtractExplicitChapterTitle(string line)
    {
        var t = Regex.Replace((line ?? string.Empty).Trim(), @"^#+\s*", string.Empty);
        var m = Regex.Match(
            t,
            @"^(?:Chapter|CHAPTER|Part|PART)\s+(?:[0-9]+|[IVXLC]+|One|Two|Three|Four|Five|Six|Seven|Eight|Nine|Ten|Eleven|Twelve)\s*[:\.\-–—]?\s*(.*)$",
            RegexOptions.IgnoreCase);
        if (!m.Success)
            return t;
        var rest = m.Groups[1].Value.Trim();
        return string.IsNullOrEmpty(rest) ? t : rest;
    }

    private static bool LooksLikePointHeading(string? text)
        => Regex.IsMatch((text ?? string.Empty).Trim(), @"^\d{1,2}[\.\)\]]\s+\S");

    /// <summary>ALL-CAPS section titles ("STARTUP THINKING") are in-chapter headings, not chapters.</summary>
    internal static bool IsAllCapsSectionTitle(string? text)
    {
        var t = Regex.Replace((text ?? string.Empty).Trim(), @"^#+\s*", string.Empty);
        if (IsExplicitChapterMarker(t) || LooksLikeNumberedChapterBanner(t) || IsFrontMatterMarker(t))
            return false;
        var letters = t.Where(char.IsLetter).ToArray();
        return letters.Length >= 4 && letters.All(char.IsUpper);
    }

    /// <summary>
    /// Fold short ALL-CAPS section dumps back into the previous chapter so a 14-chapter
    /// book does not become 70+ fake chapters (and then "No chapters yet").
    /// </summary>
    public static List<ImportedChapter> CoalesceSectionHeadingChapters(List<ImportedChapter>? chapters)
    {
        if (chapters == null || chapters.Count == 0)
            return new List<ImportedChapter>();
        if (chapters.Count == 1)
            return chapters;

        var merged = new List<ImportedChapter>();
        foreach (var ch in chapters)
        {
            var title = (ch.Title ?? string.Empty).Trim();
            var body = ch.Body ?? string.Empty;
            var plain = Regex.Replace(body, "<[^>]+>", " ");
            plain = Regex.Replace(plain, @"\s+", " ").Trim();

            if (merged.Count > 0 && Regex.IsMatch(title, @"^\d{1,2}$") && plain.Length < 80)
            {
                // Lone "1" banner — keep as opener of the next real title if possible.
                merged.Add(ch);
                continue;
            }

            if (merged.Count > 0
                && Regex.IsMatch(merged[^1].Title ?? "", @"^\d{1,2}$")
                && (IsAllCapsSectionTitle(title) || LooksLikeChapterTitle(title)))
            {
                var prev = merged[^1];
                var combinedTitle = TruncateSuggestedTitle(prev.Title + " " + title);
                merged[^1] = new ImportedChapter(prev.ChapterNo, combinedTitle, prev.Body + body);
                continue;
            }

            var isReal = IsExplicitChapterMarker(title)
                || LooksLikeNumberedChapterBanner(title)
                || IsFrontMatterMarker(title);
            if (!isReal && merged.Count > 0 && IsAllCapsSectionTitle(title) && plain.Length < 700)
            {
                var prev = merged[^1];
                var heading = "<h2 class=\"manuscript-heading manuscript-h2\">"
                              + System.Net.WebUtility.HtmlEncode(title) + "</h2>";
                merged[^1] = new ImportedChapter(prev.ChapterNo, prev.Title, prev.Body + heading + body);
                continue;
            }

            merged.Add(ch);
        }

        return merged
            .Select((c, i) => new ImportedChapter(i + 1, c.Title, c.Body))
            .ToList();
    }

    /// <summary>
    /// A chapter-sized title such as "Disadvantages of Technology" — not a numbered point.
    /// </summary>
    internal static bool LooksLikeChapterTitle(string? text)
    {
        var t = (text ?? string.Empty).Trim();
        if (LooksLikePointHeading(t) || !LooksLikeStandaloneHeading(t))
            return false;
        var words = Regex.Split(t, @"\s+").Where(w => w.Length > 0).ToArray();
        return words.Length is >= 2 and <= 8;
    }

    private static bool IsChapterBoundaryLine(string line, string[] lines, int index, List<(int lineIdx, string title)> boundaries)
    {
        if (line.Length > 120)
            return false;
        if (LooksLikePointHeading(line))
            return false;
        if (IsExplicitChapterMarker(line))
            return true;
        if (!LooksLikeChapterTitle(line) || !IsPrecededByBreak(lines, index))
            return false;

        var prevIdx = boundaries.Count > 0 ? boundaries[^1].lineIdx : -1;
        var charsSince = 0;
        for (var i = prevIdx + 1; i < index; i++)
            charsSince += lines[i].Trim().Length;
        // Need a real preceding chapter body so scene-less opening titles stay put.
        return charsSince >= 280;
    }

    private static bool IsDocxChapterBoundary(string styleId, string text)
    {
        if (LooksLikePointHeading(text))
            return false;
        if (IsExplicitChapterMarker(text))
            return true;
        if (IsTitleStyleId(styleId) || IsSubtitleStyleId(styleId))
            return false;
        return HeadingLevelFromStyleName(styleId) == 1;
    }

    private static string CleanCandidateTitle(string? text) =>
        Regex.Replace((text ?? string.Empty).Replace('_', ' '), @"\s+", " ").Trim().TrimStart('#').Trim();

    public static (int chapterNo, string chapterTitle) SuggestChapterFromBodyText(string text)
    {
        var chapterNo = 1;
        var chapterTitle = string.Empty;
        if (string.IsNullOrWhiteSpace(text))
            return (chapterNo, "Imported chapter");

        var lines = text.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0)
                continue;

            var hm = Regex.Match(line, @"^(#{1,6})\s+(.+)$");
            if (hm.Success)
            {
                chapterTitle = TruncateSuggestedTitle(hm.Groups[2].Value.Trim());
                break;
            }

            var chMatch = Regex.Match(line, @"^(?:Chapter|CHAPTER)\s+(\d+)\s*[:\.\-]?\s*(.*)$", RegexOptions.IgnoreCase);
            if (chMatch.Success)
            {
                if (int.TryParse(chMatch.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && n > 0)
                    chapterNo = n;
                var rest = chMatch.Groups[2].Value.Trim();
                if (!string.IsNullOrEmpty(rest))
                    chapterTitle = TruncateSuggestedTitle(rest);
                break;
            }

            if (line.Length <= 120)
            {
                chapterTitle = TruncateSuggestedTitle(line);
                break;
            }

            break;
        }

        if (string.IsNullOrEmpty(chapterTitle))
            chapterTitle = "Imported chapter";
        return (chapterNo, chapterTitle);
    }

    /// <summary>A single chapter detected inside an imported document.</summary>
    public sealed record ImportedChapter(int ChapterNo, string Title, string Body);

    /// <summary>
    /// Insert newlines before mid-paragraph chapter headings. PdfPig often returns page text
    /// with almost no line breaks, so line-based splitters would see only one chapter.
    /// </summary>
    public static string NormalizeInlineChapterHeadings(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return text ?? "";
        // Break before "Chapter 2" / "PART III" when stuck in the middle of a run of text.
        return Regex.Replace(
            text,
            @"(?<![\n\r])\s+((?:Chapter|CHAPTER|Part|PART)\s+(?:[0-9]+|[IVXLC]+|One|Two|Three|Four|Five|Six|Seven|Eight|Nine|Ten|Eleven|Twelve)\b)",
            "\n\n$1",
            RegexOptions.None);
    }

    /// <summary>
    /// Split imported text into chapters by detecting headings:
    /// markdown headings (<c># ...</c>), "Chapter N[: Title]" lines, or short ALL-CAPS / Title-Case
    /// heading-like lines. Returns a single chapter when no reliable boundaries are found.
    /// </summary>
    public static List<ImportedChapter> SplitIntoChapters(string text)
    {
        var result = new List<ImportedChapter>();
        if (string.IsNullOrWhiteSpace(text))
            return result;

        text = NormalizeInlineChapterHeadings(text);
        var lines = text.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');

        // Collect indices of lines that look like a chapter heading.
        var boundaries = new List<(int lineIdx, string title)>();
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (line.Length == 0)
                continue;

            if (IsChapterBoundaryLine(line, lines, i, boundaries))
            {
                var title = IsExplicitChapterMarker(line)
                    ? ExtractExplicitChapterTitle(line)
                    : line;
                boundaries.Add((i, TruncateSuggestedTitle(title)));
            }
        }

        // Fallback: scan full string for chapter markers when line-based detection found < 2.
        if (boundaries.Count < 2)
        {
            var inline = SplitByInlineChapterMarkers(text);
            if (inline.Count >= 2)
                return inline;

            var (no, title) = SuggestChapterFromBodyText(text);
            result.Add(new ImportedChapter(no, title, FormatChapterBodyHtml(text.Trim(), title)));
            var resplit = ResplitSingleChapterOnHeadings(result[0]);
            return resplit.Count >= 2 ? resplit : result;
        }

        // Keep text before the first heading (title page, preface) instead of dropping it.
        // Never fold leading matter into a Contents/TOC chapter — export skips Contents and that
        // used to delete the preface (and sometimes chapter 1 text stuck in the TOC dump).
        var chapterNo = 0;
        string? leadingMatter = null;
        if (boundaries[0].lineIdx > 0)
        {
            var lead = new StringBuilder();
            for (var l = 0; l < boundaries[0].lineIdx; l++)
                lead.AppendLine(lines[l]);
            var leadText = lead.ToString().Trim();
            if (leadText.Length > 0)
                leadingMatter = leadText;
        }

        var firstTitle = boundaries[0].title ?? "";
        if (leadingMatter != null
            && (IsImportedContentsTitle(firstTitle) || !IsFrontMatterMarker(firstTitle)))
        {
            chapterNo++;
            result.Add(new ImportedChapter(chapterNo, "Preface", FormatChapterBodyHtml(leadingMatter, "Preface")));
            leadingMatter = null;
        }

        for (var b = 0; b < boundaries.Count; b++)
        {
            var headingLineIdx = boundaries[b].lineIdx;
            var endLine = b + 1 < boundaries.Count ? boundaries[b + 1].lineIdx : lines.Length;
            var bodyBuilder = new StringBuilder();
            var chapterTitle = boundaries[b].title ?? "";

            // Skip imported Contents/TOC — formatter builds its own table of contents.
            if (IsImportedContentsTitle(chapterTitle))
            {
                leadingMatter = null;
                continue;
            }

            if (b == 0 && leadingMatter != null)
            {
                bodyBuilder.AppendLine(leadingMatter);
                leadingMatter = null;
            }

            // PDF/import often puts "Chapter 1 Title … body…" on ONE line. Include trailing
            // text after the heading token — but never re-inject the chapter title itself
            // (preview already renders fmt-chapter-opener with that title).
            var headingLine = lines[headingLineIdx];
            var afterHeading = BodyRemainderAfterChapterTitle(StripChapterHeadingPrefix(headingLine), chapterTitle);
            if (!string.IsNullOrWhiteSpace(afterHeading))
                bodyBuilder.AppendLine(afterHeading);

            for (var l = headingLineIdx + 1; l < endLine; l++)
                bodyBuilder.AppendLine(lines[l]);

            var body = bodyBuilder.ToString().Trim();
            if (body.Length == 0)
            {
                // Keep a titled chapter even if the next banner is immediate —
                // otherwise "Chapter 2: Title" can be dropped.
                if (string.IsNullOrWhiteSpace(boundaries[b].title))
                    continue;
                body = "";
            }

            chapterNo++;
            var title = boundaries[b].title;
            if (string.IsNullOrWhiteSpace(title))
                title = $"Chapter {chapterNo}";
            result.Add(new ImportedChapter(chapterNo, title, FormatChapterBodyHtml(body, title)));
        }

        if (result.Count == 0)
        {
            var (no, title) = SuggestChapterFromBodyText(text);
            result.Add(new ImportedChapter(no, title, FormatChapterBodyHtml(text.Trim(), title)));
        }

        if (result.Count == 1)
        {
            var resplit = ResplitSingleChapterOnHeadings(result[0]);
            if (resplit.Count >= 2)
                return CollapseEmptyDuplicateChapterShells(resplit);
        }

        return CollapseEmptyDuplicateChapterShells(result);
    }

    /// <summary>
    /// Drop TOC listing shells (title only) when a later chapter with the same title has real body text.
    /// This is what turned ~213 real chapters + TOC lines into ~95 exportable chapters.
    /// </summary>
    internal static List<ImportedChapter> CollapseEmptyDuplicateChapterShells(List<ImportedChapter>? chapters)
    {
        if (chapters == null || chapters.Count == 0)
            return new List<ImportedChapter>();

        static string TitleKey(string? title)
        {
            var t = NormalizeTitleKey(title);
            t = Regex.Replace(t, @"^(?:chapter|ch\.?|part)\s*[0-9ivxlcdm]+\s*[:.\-\u2013\u2014]?\s*", "", RegexOptions.IgnoreCase);
            return t.Trim();
        }

        static int PlainLen(string? body)
        {
            var plain = Regex.Replace(body ?? "", "<[^>]+>", " ");
            return Regex.Replace(plain, @"\s+", " ").Trim().Length;
        }

        var keep = new List<ImportedChapter>(chapters.Count);
        for (var i = 0; i < chapters.Count; i++)
        {
            var ch = chapters[i];
            if (IsImportedContentsTitle(ch.Title))
                continue;

            var plain = PlainLen(ch.Body);
            if (plain < 48)
            {
                var key = TitleKey(ch.Title);
                if (!string.IsNullOrEmpty(key)
                    && chapters.Skip(i + 1).Any(c => TitleKey(c.Title) == key && PlainLen(c.Body) >= 48))
                    continue;
            }

            keep.Add(ch);
        }

        return keep
            .Select((c, idx) => new ImportedChapter(idx + 1, c.Title, c.Body))
            .ToList();
    }

    /// <summary>Format import body and strip a leading heading that duplicates the chapter title.</summary>
    private static string FormatChapterBodyHtml(string? body, string? title)
    {
        var html = FormatImportedBodyAsHtml(body);
        html = BookManuscriptHtmlFormatter.StripRedundantChapterOpenings(html, title);
        return BookManuscriptHtmlFormatter.StripEmptyHtmlBlocks(html);
    }

    /// <summary>
    /// Keep same-line body after a chapter banner, but drop text that is only the chapter title
    /// (otherwise every upload shows title twice: opener + body &lt;h2&gt;).
    /// </summary>
    internal static string? BodyRemainderAfterChapterTitle(string? afterHeading, string? chapterTitle)
    {
        var rem = (afterHeading ?? "").Trim();
        if (string.IsNullOrEmpty(rem)) return null;

        var title = (chapterTitle ?? "").Trim();
        if (string.IsNullOrEmpty(title)) return rem;

        if (string.Equals(NormalizeTitleKey(rem), NormalizeTitleKey(title), StringComparison.Ordinal))
            return null;

        if (rem.StartsWith(title, StringComparison.OrdinalIgnoreCase))
        {
            var rest = rem[title.Length..].TrimStart(' ', '\t', ':', '-', '–', '—', '.', '—');
            return string.IsNullOrWhiteSpace(rest) ? null : rest;
        }

        return rem;
    }

    private static string NormalizeTitleKey(string? s) =>
        Regex.Replace((s ?? "").Trim().ToLowerInvariant(), @"\s+", " ");


    /// <summary>
    /// Pick the richer chapter split from Word structure vs plain-text fallback
    /// so headings survive even when Word styles are missing.
    /// </summary>
    public static List<ImportedChapter> PreferRicherChapterSplit(List<ImportedChapter> structured, List<ImportedChapter> fallback)
    {
        if (structured == null || structured.Count == 0)
            return fallback ?? new List<ImportedChapter>();
        if (fallback == null || fallback.Count == 0)
            return structured;

        var structuredHasHeadings = structured.Any(c =>
            (c.Body ?? "").Contains("manuscript-heading", StringComparison.OrdinalIgnoreCase)
            || (c.Body ?? "").Contains("<h", StringComparison.OrdinalIgnoreCase));
        var structuredHasImages = structured.Any(c =>
            (c.Body ?? "").Contains("<img", StringComparison.OrdinalIgnoreCase));
        var fallbackHasImages = fallback.Any(c =>
            (c.Body ?? "").Contains("<img", StringComparison.OrdinalIgnoreCase));
        // Never throw away extracted diagrams just because the text-only split found more chapters.
        if (structuredHasImages && !fallbackHasImages)
            return structured;
        var fallbackLooksLikeRealChapters = fallback.Count(c =>
            IsExplicitChapterMarker(c.Title ?? "") || LooksLikeNumberedChapterBanner(c.Title ?? "")) >= 2;
        var fallbackOverSplit = fallback.Count > Math.Max(20, structured.Count * 2)
            && fallback.Count(c => IsExplicitChapterMarker(c.Title ?? "")) < 3;
        static int PlainLen(ImportedChapter c) =>
            Regex.Replace(Regex.Replace(c.Body ?? "", "<[^>]+>", " "), @"\s+", " ").Trim().Length;
        var fallbackMostlyEmpty = fallback.Count >= 10
            && fallback.Count(c => PlainLen(c) < 40) > fallback.Count / 2;
        // Prefer a real Chapter 2 split over one blob that only has in-body headings —
        // but never take a 50+ heading dump (or mostly empty shells) over a tighter structured book.
        if (fallbackMostlyEmpty && structured.Count >= 2)
            return structured;
        if (fallbackLooksLikeRealChapters && fallback.Count > structured.Count && !structuredHasImages && !fallbackOverSplit)
            return fallback;
        if (structuredHasHeadings && fallback.Count > structured.Count && !fallbackLooksLikeRealChapters)
            return structured;
        if (!structuredHasHeadings && fallback.Count > structured.Count)
            return fallback;

        static int Score(List<ImportedChapter> list)
        {
            var score = 0;
            foreach (var c in list)
            {
                var body = c.Body ?? "";
                if (body.Contains("<h", StringComparison.OrdinalIgnoreCase)) score += 8;
                if (body.Contains("manuscript-heading", StringComparison.OrdinalIgnoreCase)) score += 10;
                if (body.Contains("<img", StringComparison.OrdinalIgnoreCase)) score += 20;
                if (IsExplicitChapterMarker(c.Title ?? "")) score += 6;
                else if (!string.IsNullOrWhiteSpace(c.Title)
                    && !Regex.IsMatch(c.Title, @"^Chapter\s+\d+$", RegexOptions.IgnoreCase))
                    score += 2;
            }
            return score;
        }

        return Score(structured) >= Score(fallback) ? structured : fallback;
    }

    /// <summary>Removes a leading Chapter/Part/Section heading from a line, leaving the body text.</summary>
    private static string StripChapterHeadingPrefix(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return "";
        var m = Regex.Match(
            line.Trim(),
            @"^(?:Chapter|CHAPTER|Part|PART|Section|SECTION)\s+(?:[0-9]+|[IVXLC]+|[A-Za-z]+)\s*[:\.\-–—]?\s*(.*)$",
            RegexOptions.IgnoreCase);
        if (!m.Success) return line.Trim();
        return (m.Groups[1].Value ?? "").Trim();
    }

    /// <summary>Split on every <c>Chapter N</c> / <c>Part N</c> occurrence in the full text (PDF-friendly).</summary>
    private static List<ImportedChapter> SplitByInlineChapterMarkers(string text)
    {
        var result = new List<ImportedChapter>();
        var rx = new Regex(
            @"\b((?:Chapter|Part)\s+(?:[0-9]+|[IVXLC]+))\b(?:\s*[:\.\-–—]\s*([^\r\n]{0,80}))?",
            RegexOptions.IgnoreCase);
        var matches = rx.Matches(text);
        if (matches.Count < 2)
            return result;

        // Drop markers that are too close together (< 40 chars) — usually false positives.
        var starts = new List<(int Index, string Title)>();
        foreach (Match m in matches)
        {
            if (starts.Count > 0 && m.Index - starts[^1].Index < 40)
                continue;
            var rest = m.Groups[2].Success ? m.Groups[2].Value.Trim() : "";
            var title = TruncateSuggestedTitle(string.IsNullOrEmpty(rest) ? m.Groups[1].Value.Trim() : rest);
            starts.Add((m.Index, title));
        }

        if (starts.Count < 2)
            return result;

        if (starts[0].Index > 0)
        {
            var preface = text[..starts[0].Index].Trim();
            if (preface.Length > 0)
                result.Add(new ImportedChapter(1, "Preface", FormatChapterBodyHtml(preface, "Preface")));
        }

        for (var i = 0; i < starts.Count; i++)
        {
            var contentStart = starts[i].Index;
            // Skip the heading itself — body starts after the match line-ish end.
            var headingEnd = contentStart;
            var nl = text.IndexOf('\n', contentStart);
            if (nl > contentStart && nl - contentStart < 120)
                headingEnd = nl + 1;
            else
            {
                // No newline: advance past the matched heading token + optional title.
                var m = rx.Match(text, contentStart);
                headingEnd = m.Success ? m.Index + m.Length : contentStart;
            }

            var end = i + 1 < starts.Count ? starts[i + 1].Index : text.Length;
            if (headingEnd > end) headingEnd = contentStart;
            var body = headingEnd < end ? text[headingEnd..end].Trim() : "";
            // Keep Chapter 1 even when the banner sits on the same line as the next heading.
            result.Add(new ImportedChapter(result.Count + 1, starts[i].Title, FormatChapterBodyHtml(body, starts[i].Title)));
        }

        return result;
    }

    public static string MapImportExceptionMessage(Exception ex)
    {
        if (ex is OperationCanceledException)
            return "Upload was cancelled or timed out. Try a smaller file or paste the text instead.";
        if (ex is InvalidOperationException ioe && !string.IsNullOrWhiteSpace(ioe.Message))
            return ioe.Message;
        if (ex is PdfDocumentEncryptedException)
            return "This PDF is password-protected. Remove the password or paste the text instead.";

        var msg = ex.Message ?? string.Empty;
        if (msg.Contains("password", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("encrypted", StringComparison.OrdinalIgnoreCase))
            return "This PDF is password-protected. Remove the password or paste the text instead.";
        if (msg.Contains("not a PDF", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("Invalid PDF", StringComparison.OrdinalIgnoreCase))
            return "This file does not look like a valid PDF. Try exporting as .txt or paste the text.";
        if (msg.Contains("OpenXml", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("zip", StringComparison.OrdinalIgnoreCase))
            return "This Word file could not be read. Re-save it as .docx and upload again.";

        return "Could not read this file. Please try uploading a different export or paste the text instead.";
    }

    /// <summary>Turn remaining markdown / isolated heading lines into HTML the formatter already renders.</summary>
    public static string FormatImportedBodyAsHtml(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return string.Empty;
        var raw = body.Trim();
        if (raw.Contains("<p", StringComparison.OrdinalIgnoreCase)
            || raw.Contains("<h", StringComparison.OrdinalIgnoreCase))
            return PromoteHeadingParagraphs(raw);

        var lines = raw.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
        var sb = new StringBuilder();
        var para = new StringBuilder();

        void FlushPara()
        {
            var t = para.ToString().Trim();
            para.Clear();
            if (t.Length == 0) return;
            sb.Append("<p class=\"manuscript-p\">").Append(System.Net.WebUtility.HtmlEncode(t)).Append("</p>");
        }

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (line.Length == 0)
            {
                FlushPara();
                continue;
            }

            var hm = Regex.Match(line, @"^(#{1,6})\s+(.+?)\s*#*$");
            if (hm.Success)
            {
                FlushPara();
                var lvl = Math.Clamp(hm.Groups[1].Value.Length, 1, 6);
                sb.Append("<h").Append(lvl).Append(" class=\"manuscript-heading manuscript-h").Append(lvl).Append("\">")
                    .Append(System.Net.WebUtility.HtmlEncode(hm.Groups[2].Value.Trim()))
                    .Append("</h").Append(lvl).Append('>');
                continue;
            }

            if (LooksLikeStandaloneHeading(line) && IsPrecededByBreak(lines, i))
            {
                FlushPara();
                sb.Append("<h2 class=\"manuscript-heading manuscript-h2\">")
                    .Append(System.Net.WebUtility.HtmlEncode(line))
                    .Append("</h2>");
                continue;
            }

            if (para.Length > 0) para.Append(' ');
            para.Append(line);
        }

        FlushPara();
        return PromoteHeadingParagraphs(sb.Length > 0 ? sb.ToString() : raw);
    }

    /// <summary>
    /// Turn heading-like <c>&lt;p&gt;</c> blocks into real <c>&lt;h2&gt;</c> so Word/PDF
    /// titles that were not styled still render as headings in preview and export.
    /// </summary>
    public static string PromoteHeadingParagraphs(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return html ?? string.Empty;
        if (!html.Contains("<p", StringComparison.OrdinalIgnoreCase))
            return html;

        return Regex.Replace(
            html,
            @"<p(?:\s[^>]*)?>([\s\S]*?)</p>",
            m =>
            {
                var inner = m.Groups[1].Value;
                if (inner.Contains("<img", StringComparison.OrdinalIgnoreCase))
                    return m.Value;
                var text = System.Net.WebUtility.HtmlDecode(Regex.Replace(inner, "<[^>]+>", string.Empty)).Trim();
                if (!LooksLikeStandaloneHeading(text))
                    return m.Value;
                return "<h2 class=\"manuscript-heading manuscript-h2\">" + inner + "</h2>";
            },
            RegexOptions.IgnoreCase);
    }

    private static List<ImportedChapter> ResplitSingleChapterOnHeadings(ImportedChapter chapter)
    {
        var html = chapter.Body ?? string.Empty;
        if (string.IsNullOrWhiteSpace(html))
            return new List<ImportedChapter> { chapter };

        var rx = new Regex(@"<h([1-2])\b[^>]*>([\s\S]*?)</h\1>", RegexOptions.IgnoreCase);
        var matches = rx.Matches(html);
        if (matches.Count == 0)
            return new List<ImportedChapter> { chapter };

        var splitAt = matches.Cast<Match>().Where(m =>
        {
            var text = System.Net.WebUtility.HtmlDecode(Regex.Replace(m.Groups[2].Value, "<[^>]+>", string.Empty)).Trim();
            if (LooksLikePointHeading(text))
                return false;
            if (IsExplicitChapterMarker(text))
                return true;
            return LooksLikeChapterTitle(text) && m.Index >= 280;
        }).ToList();
        if (splitAt.Count == 0)
            return new List<ImportedChapter> { chapter };

        var result = new List<ImportedChapter>();
        if (splitAt[0].Index > 0)
        {
            var leadHtml = html[..splitAt[0].Index].Trim();
            var preface = FormatChapterBodyHtml(leadHtml, IsFrontMatterMarker(chapter.Title) ? chapter.Title : "Preface");
            if (preface.Length > 0)
                result.Add(new ImportedChapter(1, IsFrontMatterMarker(chapter.Title) ? TruncateSuggestedTitle(chapter.Title) : "Preface", preface));
        }

        for (var i = 0; i < splitAt.Count; i++)
        {
            var title = System.Net.WebUtility.HtmlDecode(
                Regex.Replace(splitAt[i].Groups[2].Value, "<[^>]+>", string.Empty)).Trim();
            var start = splitAt[i].Index + splitAt[i].Length;
            var end = i + 1 < splitAt.Count ? splitAt[i + 1].Index : html.Length;
            if (start > end) continue;
            var resolvedTitle = TruncateSuggestedTitle(string.IsNullOrWhiteSpace(title) ? $"Chapter {result.Count + 1}" : title);
            var body = FormatChapterBodyHtml(html[start..end].Trim(), resolvedTitle);
            if (body.Length == 0 && string.IsNullOrWhiteSpace(title))
                continue;
            result.Add(new ImportedChapter(result.Count + 1, resolvedTitle, body));
        }

        return result.Count >= 2 ? result : new List<ImportedChapter> { chapter };
    }

    internal static bool LooksLikeStandaloneHeading(string line)
    {
        var t = (line ?? string.Empty).Trim();
        if (t.Length is < 2 or > 80)
            return false;
        if (t.EndsWith('.') || t.EndsWith('!') || t.EndsWith('?') || t.EndsWith(','))
            return false;
        if (Regex.IsMatch(t, @"^\d+$"))
            return false;
        var words = Regex.Split(t, @"\s+").Where(w => w.Length > 0).ToArray();
        if (words.Length is < 1 or > 12)
            return false;

        var letters = t.Count(char.IsLetter);
        if (letters < 3)
            return false;

        var upperLetters = t.Count(ch => char.IsLetter(ch) && char.IsUpper(ch));
        var isAllCaps = upperLetters >= letters * 0.85;
        var significant = words.Where(w => w.Length > 3 || char.IsUpper(w[0])).ToArray();
        if (significant.Length == 0) significant = words;
        var titleCase = significant.Count(w => char.IsLetter(w[0]) && char.IsUpper(w[0]))
                        >= Math.Max(1, (int)Math.Ceiling(significant.Length * 0.7));
        return isAllCaps || titleCase || IsChapterHeadingLine(t) || LooksLikePointHeading(t);
    }

    private static bool IsPrecededByBreak(string[] lines, int index)
        => index == 0 || string.IsNullOrWhiteSpace(lines[index - 1]);

    private static bool IsTitleStyleId(string? styleId)
        => string.Equals(styleId, "Title", StringComparison.OrdinalIgnoreCase)
           || string.Equals(styleId, "BookTitle", StringComparison.OrdinalIgnoreCase);

    private static bool IsSubtitleStyleId(string? styleId)
        => string.Equals(styleId, "Subtitle", StringComparison.OrdinalIgnoreCase);

    private static int? ResolveDocxHeadingLevel(Paragraph para, MainDocumentPart mainPart, double bodyFontPt)
    {
        var text = (para.InnerText ?? string.Empty).Trim();
        if (text.Length == 0)
            return null;

        var pPr = para.ParagraphProperties;
        var outline = pPr?.OutlineLevel?.Val?.Value;
        if (outline != null)
        {
            var lvl = outline.Value + 1;
            if (lvl is >= 1 and <= 6)
                return lvl;
        }

        var styleId = pPr?.ParagraphStyleId?.Val?.Value ?? string.Empty;
        var fromStyle = HeadingLevelFromStyleName(styleId);
        if (fromStyle != null)
            return fromStyle;

        fromStyle = HeadingLevelFromStyleDefinitions(styleId, mainPart);
        if (fromStyle != null)
            return fromStyle;

        if (text.Length <= 120 && IsChapterHeadingLine(text))
            return 1;

        var runSize = GetParagraphEffectiveFontSizePt(para, mainPart);
        var isBold = ParagraphIsEffectivelyBold(para, mainPart);
        var isCenter = ParagraphIsCentered(para, mainPart);

        if (runSize.HasValue && bodyFontPt > 0 && text.Length <= 100)
        {
            if (runSize.Value >= bodyFontPt * 1.4) return 1;
            if (runSize.Value >= bodyFontPt * 1.18) return 2;
        }

        if (text.Length <= 80 && LooksLikeStandaloneHeading(text))
        {
            if (isCenter && isBold) return 1;
            if (isCenter || isBold) return 2;
            if (runSize.HasValue && bodyFontPt > 0 && runSize.Value >= bodyFontPt * 1.08)
                return 2;
        }

        if (text.Length is >= 3 and <= 60 && isBold && isCenter)
            return 1;

        if (text.Length is >= 3 and <= 70 && isBold && !EndsLikeSentence(text))
            return 2;

        return null;
    }

    private static bool EndsLikeSentence(string text)
        => text.EndsWith('.') || text.EndsWith('!') || text.EndsWith('?') || text.EndsWith(',');

    private static int? HeadingLevelFromStyleName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;
        var n = name.Trim();
        if (n.Equals("Title", StringComparison.OrdinalIgnoreCase) || n.Equals("BookTitle", StringComparison.OrdinalIgnoreCase))
            return 1;
        if (n.Equals("Subtitle", StringComparison.OrdinalIgnoreCase))
            return 2;
        if (Regex.IsMatch(n, @"^(chapter\s*title|chaptitle|chaphead|chapterhead)$", RegexOptions.IgnoreCase))
            return 1;
        var m = Regex.Match(n, @"(?:heading|überschrift|kop|titulo|titre|nagłówek|заголовок)\s*([1-6])", RegexOptions.IgnoreCase);
        if (m.Success && int.TryParse(m.Groups[1].Value, out var lvl))
            return lvl;
        m = Regex.Match(n, @"heading\s*([1-6])", RegexOptions.IgnoreCase);
        if (m.Success && int.TryParse(m.Groups[1].Value, out lvl))
            return lvl;
        return null;
    }

    private static int? HeadingLevelFromStyleDefinitions(string styleId, MainDocumentPart mainPart)
    {
        if (string.IsNullOrWhiteSpace(styleId))
            return null;
        var styles = mainPart.StyleDefinitionsPart?.Styles;
        if (styles == null)
            return null;

        var style = styles.Elements<Style>()
            .FirstOrDefault(s => string.Equals(s.StyleId?.Value, styleId, StringComparison.OrdinalIgnoreCase));
        if (style == null)
            return null;

        var fromName = HeadingLevelFromStyleName(style.StyleName?.Val?.Value)
                       ?? HeadingLevelFromStyleName(style.StyleId?.Value)
                       ?? HeadingLevelFromStyleName(style.BasedOn?.Val?.Value);
        if (fromName != null)
            return fromName;

        var styleOutline = style.StyleParagraphProperties?.OutlineLevel?.Val?.Value;
        if (styleOutline != null)
        {
            var lvl = styleOutline.Value + 1;
            if (lvl is >= 1 and <= 6)
                return lvl;
        }

        return null;
    }

    private static double? GetFirstRunFontSizePt(Paragraph para)
        => GetParagraphEffectiveFontSizePt(para, null);

    private static double? GetParagraphEffectiveFontSizePt(Paragraph para, MainDocumentPart? mainPart)
    {
        foreach (var run in para.Descendants<Run>())
        {
            var sz = ParseHalfPoints(run.RunProperties?.FontSize?.Val?.Value)
                     ?? ParseHalfPoints(run.RunProperties?.FontSizeComplexScript?.Val?.Value);
            if (sz.HasValue)
                return sz;
        }

        var markSz = ParseHalfPoints(para.ParagraphProperties?.ParagraphMarkRunProperties?.GetFirstChild<FontSize>()?.Val?.Value);
        if (markSz.HasValue)
            return markSz;

        var styleId = para.ParagraphProperties?.ParagraphStyleId?.Val?.Value;
        if (mainPart != null && !string.IsNullOrWhiteSpace(styleId))
        {
            var fromStyle = GetStyleChainValue(mainPart, styleId, style =>
                ParseHalfPoints(style.StyleRunProperties?.FontSize?.Val?.Value)
                ?? ParseHalfPoints(style.StyleRunProperties?.FontSizeComplexScript?.Val?.Value));
            if (fromStyle.HasValue)
                return fromStyle;
        }

        return null;
    }

    private static double? ParseHalfPoints(string? val)
    {
        if (val != null && double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var halfPoints) && halfPoints > 0)
            return halfPoints / 2.0;
        return null;
    }

    private static bool ParagraphIsEffectivelyBold(Paragraph para, MainDocumentPart mainPart)
    {
        var runs = para.Descendants<Run>()
            .Where(r => !string.IsNullOrWhiteSpace(r.InnerText))
            .ToList();
        if (runs.Count == 0)
            return IsOn(para.ParagraphProperties?.ParagraphMarkRunProperties?.GetFirstChild<Bold>());

        var styleId = para.ParagraphProperties?.ParagraphStyleId?.Val?.Value;
        var styleBold = !string.IsNullOrWhiteSpace(styleId)
            && GetStyleChainValue(mainPart, styleId, style =>
            {
                var b = style.StyleRunProperties?.Bold;
                return b == null ? (bool?)null : IsOn(b);
            }) == true;

        var boldRuns = 0;
        var letterRuns = 0;
        foreach (var run in runs)
        {
            if (!run.InnerText.Any(char.IsLetter))
                continue;
            letterRuns++;
            if (IsOn(run.RunProperties?.Bold) || styleBold)
                boldRuns++;
        }
        return letterRuns > 0 && boldRuns >= letterRuns;
    }

    private static bool ParagraphIsCentered(Paragraph para, MainDocumentPart mainPart)
    {
        var jc = para.ParagraphProperties?.Justification?.Val?.Value;
        if (jc == JustificationValues.Center)
            return true;

        var styleId = para.ParagraphProperties?.ParagraphStyleId?.Val?.Value;
        if (string.IsNullOrWhiteSpace(styleId))
            return false;
        return GetStyleChainValue(mainPart, styleId, style =>
        {
            var v = style.StyleParagraphProperties?.Justification?.Val?.Value;
            if (v == null) return (bool?)null;
            return v == JustificationValues.Center;
        }) == true;
    }

    private static bool IsOn(OnOffType? prop)
    {
        if (prop == null) return false;
        if (prop.Val == null) return true;
        return prop.Val.Value;
    }

    private static T? GetStyleChainValue<T>(MainDocumentPart mainPart, string? styleId, Func<Style, T?> getter, int depth = 0)
    {
        if (string.IsNullOrWhiteSpace(styleId) || depth > 8)
            return default;
        var styles = mainPart.StyleDefinitionsPart?.Styles;
        if (styles == null)
            return default;
        var style = styles.Elements<Style>()
            .FirstOrDefault(s => string.Equals(s.StyleId?.Value, styleId, StringComparison.OrdinalIgnoreCase));
        if (style == null)
            return default;
        var value = getter(style);
        if (value != null)
            return value;
        return GetStyleChainValue(mainPart, style.BasedOn?.Val?.Value, getter, depth + 1);
    }

    private static double EstimateDocxBodyFontSizePt(Body body)
    {
        var sizes = new Dictionary<double, int>();
        foreach (var run in body.Descendants<Run>())
        {
            var szVal = run.RunProperties?.FontSize?.Val?.Value;
            if (szVal != null && double.TryParse(szVal, NumberStyles.Float, CultureInfo.InvariantCulture, out var halfPoints))
            {
                var pt = halfPoints / 2.0;
                sizes[pt] = sizes.GetValueOrDefault(pt) + Math.Max(1, run.InnerText?.Length ?? 1);
            }
        }
        return sizes.Count == 0 ? 11.0 : sizes.OrderByDescending(kv => kv.Value).First().Key;
    }

    private static bool LooksLikePdfHeading(string text, double avgFont, double medianFont, bool isBold, bool isCentered, out string mark)
    {
        mark = "#";
        if (text.Length is < 2 or > 100)
            return false;
        if (IsChapterHeadingLine(text) || LooksLikeNumberedChapterBanner(text))
        {
            mark = "#";
            return true;
        }
        // Large isolated chapter number (1–20) sitting above an ALL-CAPS title.
        if (Regex.IsMatch(text, @"^\d{1,2}$") && medianFont > 0 && avgFont >= medianFont * 1.6)
        {
            mark = "#";
            return true;
        }

        var looksLikeTitle = LooksLikeStandaloneHeading(text);
        // Only real chapter banners are H1. ALL-CAPS / large-font section titles stay H2
        // so "STARTUP THINKING" does not become its own chapter.
        if (medianFont > 0 && avgFont >= medianFont * 1.18)
        {
            mark = "##";
            return true;
        }
        if (looksLikeTitle && (isBold || isCentered || avgFont >= medianFont * 1.08 || medianFont <= 0))
        {
            mark = "##";
            return true;
        }
        return false;
    }

    private static List<(string Text, double AvgFontSize, bool IsBold, bool IsCentered, double Top)> BuildPdfReadingOrderLines(UglyToad.PdfPig.Content.Page page)
    {
        var words = page.GetWords()?.ToList() ?? new List<UglyToad.PdfPig.Content.Word>();
        if (words.Count == 0)
            return new List<(string, double, bool, bool, double)>();

        var ordered = words.OrderByDescending(w => w.BoundingBox.Top).ThenBy(w => w.BoundingBox.Left).ToList();
        var lines = new List<List<UglyToad.PdfPig.Content.Word>>();
        const double lineToleranceRatio = 0.4;

        foreach (var word in ordered)
        {
            var wordHeight = Math.Max(1.0, word.BoundingBox.Top - word.BoundingBox.Bottom);
            var line = lines.Count > 0 ? lines[^1] : null;
            if (line != null && Math.Abs(line[0].BoundingBox.Top - word.BoundingBox.Top) < wordHeight * lineToleranceRatio + 2)
                line.Add(word);
            else
                lines.Add(new List<UglyToad.PdfPig.Content.Word> { word });
        }

        var pageWidth = page.Width > 0 ? page.Width : 0;
        var result = new List<(string, double, bool, bool, double)>();
        foreach (var line in lines)
        {
            var sortedLine = line.OrderBy(w => w.BoundingBox.Left).ToList();
            var text = JoinPdfWords(sortedLine);
            var letters = sortedLine.SelectMany(w => w.Letters).ToList();
            var sizes = letters.Select(l => l.FontSize).Where(s => s > 0).ToList();
            var avgSize = sizes.Count > 0 ? sizes.Average() : 0.0;
            var names = letters.Select(l => l.FontName ?? string.Empty).ToList();
            var boldCount = names.Count(n =>
                n.Contains("Bold", StringComparison.OrdinalIgnoreCase)
                || n.Contains("Black", StringComparison.OrdinalIgnoreCase)
                || n.Contains("Heavy", StringComparison.OrdinalIgnoreCase)
                || n.Contains("Semibold", StringComparison.OrdinalIgnoreCase)
                || n.Contains("Demi", StringComparison.OrdinalIgnoreCase));
            var isBold = names.Count > 0 && boldCount >= names.Count * 0.55;
            var left = sortedLine[0].BoundingBox.Left;
            var right = sortedLine[^1].BoundingBox.Right;
            var mid = (left + right) / 2.0;
            var isCentered = pageWidth > 0 && Math.Abs(mid - pageWidth / 2.0) < pageWidth * 0.18
                             && (right - left) < pageWidth * 0.72;
            var top = sortedLine.Max(w => w.BoundingBox.Top);
            result.Add((text, avgSize, isBold, isCentered, top));
        }
        return result;
    }

    private static string JoinPdfWords(List<UglyToad.PdfPig.Content.Word> words)
    {
        if (words.Count == 0)
            return string.Empty;
        var sb = new StringBuilder();
        for (var i = 0; i < words.Count; i++)
        {
            var current = words[i].Text ?? "";
            if (current.Length == 0)
                continue;
            if (sb.Length == 0)
            {
                sb.Append(current);
                continue;
            }

            var prev = sb[^1];
            var prevWord = words[i - 1].Text ?? "";
            var prevSize = AverageFontSize(words[i - 1]);
            var curSize = AverageFontSize(words[i]);

            // Soft hyphen / line-wrap: "billion-" + "dollar" → "billion-dollar"
            if (prev == '-' || prev == '\u00AD')
            {
                sb.Append(current);
                continue;
            }

            // Drop-cap: huge "S" + "TART" → "START"
            if (prevWord.Length == 1 && char.IsLetter(prevWord[0])
                && prevSize > 0 && curSize > 0 && prevSize >= curSize * 1.6)
            {
                sb.Append(current);
                continue;
            }

            sb.Append(' ').Append(current);
        }

        return RepairPdfImportText(sb.ToString());
    }

    private static double AverageFontSize(UglyToad.PdfPig.Content.Word word)
    {
        var sizes = word.Letters?.Select(l => l.FontSize).Where(s => s > 0).ToList();
        return sizes is { Count: > 0 } ? sizes.Average() : 0;
    }

    private static double EstimatePdfMedianFontSize(List<UglyToad.PdfPig.Content.Page> pages)
    {
        var sizes = pages.SelectMany(p => p.Letters ?? Enumerable.Empty<UglyToad.PdfPig.Content.Letter>())
            .Select(l => l.FontSize)
            .Where(s => s > 0)
            .OrderBy(s => s)
            .ToList();
        if (sizes.Count == 0)
            return 0;
        return sizes[sizes.Count / 2];
    }

    private static string TruncateSuggestedTitle(string s, int max = 150)
    {
        if (string.IsNullOrEmpty(s))
            return string.Empty;
        return s.Length <= max ? s : s.Substring(0, max - 3) + "...";
    }

    private static string RemoveInvalidXmlChars(string value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        var sb = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            if (ch == '\t' || ch == '\n' || ch == '\r' || (ch >= 0x20 && ch <= 0xD7FF) || (ch >= 0xE000 && ch <= 0xFFFD))
                sb.Append(ch);
        }
        return sb.ToString();
    }

    private static bool LooksLikeUtf16LeText(byte[] bytes)
    {
        if (bytes.Length < 4 || bytes.Length % 2 != 0)
            return false;
        var zeroCount = 0;
        var check = Math.Min(bytes.Length, 64);
        for (var i = 1; i < check; i += 2)
        {
            if (bytes[i] == 0)
                zeroCount++;
        }
        return zeroCount >= check / 4;
    }

    private static void TryRegisterCodePages()
    {
        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        }
        catch
        {
            /* already registered */
        }
    }
}
