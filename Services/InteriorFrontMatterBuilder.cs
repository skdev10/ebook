using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using EBookDashboard.Models.DTO;
using HtmlAgilityPack;

namespace EBookDashboard.Services;

/// <summary>
/// Title, copyright, and table-of-contents HTML shared by PDF export and formatter preview (same DOM/CSS).
/// </summary>
public static class InteriorFrontMatterBuilder
{
    /// <summary>Copyright page — left-aligned legal block inside padded page.</summary>
    public static string BuildCopyrightPageHtml(string title, string author, string? publisherDisplayName)
    {
        var y = DateTime.UtcNow.Year;
        var sb = new StringBuilder();
        sb.AppendLine("""<div class="front-matter-page copyright-page book-preview-sheet">""");
        sb.AppendLine("""<div class="copyright-block">""");
        sb.AppendLine(CultureInvariant($"""<p class="cr-meta"><strong>{WebUtility.HtmlEncode(title)}</strong></p>"""));
        if (!string.IsNullOrEmpty(author))
            sb.AppendLine(CultureInvariant($"""<p class="cr-meta">{WebUtility.HtmlEncode(author)}</p>"""));
        sb.AppendLine(CultureInvariant(
            $"""<p class="cr-legal">Copyright © {y.ToString(CultureInfo.InvariantCulture)} {WebUtility.HtmlEncode(author)}. All rights reserved.</p>"""));
        if (!string.IsNullOrEmpty(publisherDisplayName))
            sb.AppendLine(CultureInvariant(
                $"""<p class="cr-legal cr-small">Prepared for publication by {WebUtility.HtmlEncode(publisherDisplayName)}.</p>"""));
        sb.AppendLine("</div></div>");
        return sb.ToString();
    }

    /// <summary>Print-style table of contents with dotted leaders and page references.</summary>
    /// <param name="chapterStartPages">Optional 1-based start pages per chapter (formatter preview). Ignored when <paramref name="pdfTargetCounters"/> is true.</param>
    /// <param name="pdfTargetCounters">When true, page numbers are resolved at PDF print time via CSS <c>target-counter</c> (accurate pagination).</param>
    public static string BuildTocHtml(
        IReadOnlyList<ChapterDto> chapters,
        BookManuscriptHtmlFormatter.PlaceholderContext phBase,
        IReadOnlyList<int>? chapterStartPages = null,
        bool pdfTargetCounters = false)
    {
        var sb = new StringBuilder();
        sb.AppendLine("""<div class="front-matter-page toc-page book-preview-sheet">""");
        sb.AppendLine("""<div class="toc-block">""");
        sb.AppendLine("""<h1 class="toc-title">Contents</h1>""");
        sb.AppendLine("""<nav class="toc-nav" aria-label="Table of contents">""");
        sb.AppendLine("""<ol class="toc-list">""");

        var tocNarrative = 0;
        for (var i = 0; i < chapters.Count; i++)
        {
            var ch = chapters[i];
            if (!BookChapterExportHelper.IsFrontMatter(ch.ChapterNumber))
                tocNarrative++;

            var phNum = BookChapterExportHelper.IsFrontMatter(ch.ChapterNumber) ? 1 : tocNarrative;
            var sectionId = i + 1;
            var ph = phBase.WithChapter(ch.Title ?? "", phNum, ch.ChapterNumber > 0 ? ch.ChapterNumber : phNum);
            var chTitleRaw = BookManuscriptHtmlFormatter.ApplyPlaceholders(ch.Title ?? "", ph);
            var chapterLine = BookChapterExportHelper.GetPreviewStyleHeading(chTitleRaw, ch.ChapterNumber, phNum);
            var bodyHtml = BookManuscriptHtmlFormatter.PrepareChapterBodyForExport(ch.Content, ph, chapterLine);
            var subHeadings = ExtractHeadingsFromChapterBodyHtml(bodyHtml);

            sb.AppendLine("""<li class="toc-item">""");
            var refClass = pdfTargetCounters ? "toc-page-ref toc-page-ref--counter" : "toc-page-ref";
            string pageRef;
            string pageRefInner;
            if (pdfTargetCounters)
            {
                pageRef = "";
                pageRefInner = $"""<a href="#ch-{sectionId}" class="{refClass}" aria-hidden="true"></a>""";
            }
            else if (chapterStartPages != null && i < chapterStartPages.Count)
            {
                pageRef = chapterStartPages[i].ToString(CultureInfo.InvariantCulture);
                pageRefInner = $"""<span class="{refClass}">{pageRef}</span>""";
            }
            else
            {
                pageRef = "…";
                pageRefInner = $"""<span class="{refClass}">{pageRef}</span>""";
            }

            sb.AppendLine(CultureInvariant(
                $"""<div class="toc-chapter-line"><span class="toc-entry-text"><a href="#ch-{sectionId}" class="toc-link">{WebUtility.HtmlEncode(chapterLine)}</a></span><span class="toc-leader" aria-hidden="true"></span>{pageRefInner}</div>"""));

            if (subHeadings.Count > 0)
            {
                sb.AppendLine("""<ul class="toc-subheadings">""");
                foreach (var h in subHeadings)
                {
                    sb.AppendLine(CultureInvariant(
                        $"""<li class="toc-subheading-item"><span class="toc-sub-text">{WebUtility.HtmlEncode(h)}</span></li>"""));
                }

                sb.AppendLine("</ul>");
            }

            sb.AppendLine("</li>");
        }

        if (chapters.Count == 0)
            sb.AppendLine("""<li class="toc-item toc-item-empty">No chapters yet.</li>""");

        sb.AppendLine("</ol></nav></div></div>");
        return sb.ToString();
    }

    /// <summary>Pulls in-chapter headings (h1–h6) from formatted body HTML in document order.</summary>
    public static List<string> ExtractHeadingsFromChapterBodyHtml(string bodyHtml)
    {
        var headings = new List<string>();
        if (string.IsNullOrWhiteSpace(bodyHtml)) return headings;
        try
        {
            var doc = new HtmlDocument();
            doc.OptionFixNestedTags = true;
            doc.LoadHtml(bodyHtml);
            var nodes = doc.DocumentNode.SelectNodes("//h1|//h2|//h3|//h4|//h5|//h6");
            if (nodes == null) return headings;
            foreach (var node in nodes)
            {
                var text = HtmlEntity.DeEntitize(node.InnerText ?? "");
                text = Regex.Replace(text.Replace('\n', ' '), @"\s+", " ").Trim();
                if (text.Length == 0) continue;
                headings.Add(text);
            }
        }
        catch
        {
            // Malformed chapter HTML should not block export.
        }

        return headings;
    }

    private static string CultureInvariant(FormattableString fs) => FormattableString.Invariant(fs);
}
