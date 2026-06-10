using System.Globalization;
using System.Net;
using System.Text;
using EBookDashboard.Models.DTO;

namespace EBookDashboard.Services.PdfExport;

/// <summary>
/// Single HTML document for BookPreview = PDF export (same DOM/CSS as formatter preview).
/// </summary>
public static class BookPreviewPrintHtmlBuilder
{
    /// <summary>Builds full print HTML — reuse for Chromium CSS-based PDF (Preview = PDF).</summary>
    public static string Build(
        string title,
        string author,
        string genre,
        string? subtitle,
        string? coverSrc,
        bool includeCoverPage,
        string copyrightHtml,
        string tocHtml,
        string chapterSections,
        BookPdfExportOptions opt,
        string pageSizeCss,
        string bodyTemplateClass,
        string previewShellClass,
        string previewWrapClass,
        string? webRootPath,
        double? contentHeightPx = null)
    {
        var themeCss = InteriorExportTheme.BuildPdfThemeCss(opt);
        var fontCss = InteriorPrintDocumentBuilder.BuildFontStylesForExport(webRootPath);
        var pageBg = opt.ResolvePageBackgroundColor();

        var coverBlock = !includeCoverPage
            ? ""
            : string.IsNullOrEmpty(coverSrc)
                ? """
                  <div class="cover-page cover-fallback">
                    <div class="cover-fallback-inner">
                      <h1 class="cover-title">COVER</h1>
                      <p class="cover-meta">Generate or select a cover in the dashboard for a full graphic cover in export.</p>
                    </div>
                  </div>
                  """
                : $"""
                  <div class="cover-page">
                    <img src="{WebUtility.HtmlEncode(coverSrc)}" alt="" class="cover-img" />
                  </div>
                  """;

        var metaLines = new StringBuilder();
        if (!string.IsNullOrEmpty(author))
            metaLines.Append(FormattableString.Invariant($"""<p class="title-page-author">{WebUtility.HtmlEncode(author)}</p>"""));
        if (!string.IsNullOrEmpty(genre))
            metaLines.Append(FormattableString.Invariant($"""<p class="title-page-genre">{WebUtility.HtmlEncode(genre)}</p>"""));

        var subtitleBlock = string.IsNullOrWhiteSpace(subtitle)
            ? ""
            : FormattableString.Invariant($"""<p class="subtitle">{WebUtility.HtmlEncode(subtitle.Trim())}</p>""");

        // TOC page refs estimate page count from flowed content height: page height minus print margins.
        var pageHeightPx = contentHeightPx is > 0
            ? contentHeightPx.Value
            : (pageSizeCss.Contains("A4", StringComparison.OrdinalIgnoreCase) ? 1122.0 : 864.0);

        var doc = new StringBuilder();
        doc.AppendLine("<!DOCTYPE html>");
        doc.AppendLine("<html lang=\"en\">");
        doc.AppendLine("<head><meta charset=\"utf-8\"/>");
        doc.AppendLine(InteriorPrintDocumentBuilder.GoogleFontLinks());
        doc.AppendLine(fontCss);
        doc.AppendLine("<style>");
        doc.AppendLine(FormattableString.Invariant($"@page {{ size: {pageSizeCss}; margin: 0; }}"));
        doc.AppendLine("* { box-sizing: border-box; -webkit-print-color-adjust: exact; print-color-adjust: exact; }");
        doc.AppendLine(FormattableString.Invariant($":root {{ --export-page-bg: {pageBg}; }}"));
        doc.AppendLine(themeCss);
        doc.AppendLine(".cover-page { page-break-after: always; width: 100%; min-height: 100vh; position: relative; margin: 0; padding: 0; background: #1e1b4b; }");
        doc.AppendLine(".cover-img { width: 100%; height: 100vh; object-fit: cover; display: block; }");
        doc.AppendLine(".cover-fallback { display: flex; align-items: center; justify-content: center; color: #fafafa; min-height: 100vh; }");
        doc.AppendLine(".cover-fallback-inner { text-align: center; padding: 24mm; }");
        doc.AppendLine(".cover-title { font-size: 28pt; margin: 0 0 8mm; letter-spacing: 0.08em; }");
        doc.AppendLine(".cover-meta { font-size: 11pt; opacity: 0.85; max-width: 140mm; margin: 0 auto; }");
        doc.AppendLine(".title-page { background: var(--export-page-bg, var(--page-bg)); }");
        doc.AppendLine(".title-page h1 { font-size: 28pt; margin: 0 0 12mm; font-weight: 600; letter-spacing: 0.03em; line-height: 1.2; }");
        doc.AppendLine(".title-page .subtitle { margin-bottom: 14mm; }");
        doc.AppendLine("</style></head>");
        doc.AppendLine("<body class=\"book-pdf-body reader-content-wrap " + bodyTemplateClass + " " + previewShellClass + " " + previewWrapClass + "\">");
        doc.Append(coverBlock);
        doc.AppendLine("<div class=\"title-page book-preview-sheet\">");
        doc.Append("<h1>").Append(WebUtility.HtmlEncode(title)).AppendLine("</h1>");
        doc.AppendLine(subtitleBlock);
        doc.Append(metaLines);
        doc.AppendLine("</div>");
        doc.AppendLine(copyrightHtml);
        doc.AppendLine(tocHtml);
        doc.AppendLine("""<div class="manuscript-root">""");
        doc.Append(chapterSections);
        doc.AppendLine("</div>");
        doc.AppendLine("<script>");
        doc.AppendLine("(function () {");
        doc.AppendLine("  var pageHeight = " + pageHeightPx.ToString("0.###", CultureInfo.InvariantCulture) + ";");
        doc.AppendLine("  if (!Number.isFinite(pageHeight) || pageHeight <= 0) pageHeight = 864;");
        doc.AppendLine("  var refs = document.querySelectorAll('.toc-page-ref[data-target]');");
        doc.AppendLine("  refs.forEach(function (el) {");
        doc.AppendLine("    var id = el.getAttribute('data-target');");
        doc.AppendLine("    if (!id) return;");
        doc.AppendLine("    var target = document.getElementById(id);");
        doc.AppendLine("    if (!target) return;");
        doc.AppendLine("    var top = target.getBoundingClientRect().top + window.scrollY;");
        doc.AppendLine("    var pageNo = Math.max(1, Math.floor(top / pageHeight) + 1);");
        doc.AppendLine("    el.textContent = String(pageNo);");
        doc.AppendLine("  });");
        doc.AppendLine("})();");
        doc.AppendLine("</script>");
        doc.AppendLine("</body></html>");
        return doc.ToString();
    }
}
