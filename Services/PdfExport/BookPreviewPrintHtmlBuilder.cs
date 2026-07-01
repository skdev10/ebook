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
                  <div class="cover-page cover-page-blank" aria-hidden="true"></div>
                  """
                : $"""
                  <div class="cover-page">
                    <img src="{WebUtility.HtmlEncode(coverSrc)}" alt="" class="cover-img" />
                  </div>
                  <div class="cover-page cover-page-blank" aria-hidden="true"></div>
                  """;

        var metaLines = new StringBuilder();
        if (!string.IsNullOrEmpty(author))
            metaLines.Append(FormattableString.Invariant($"""<p class="title-page-author">{WebUtility.HtmlEncode(author)}</p>"""));
        if (!string.IsNullOrEmpty(genre))
            metaLines.Append(FormattableString.Invariant($"""<p class="title-page-genre">{WebUtility.HtmlEncode(genre)}</p>"""));

        var subtitleBlock = string.IsNullOrWhiteSpace(subtitle)
            ? ""
            : FormattableString.Invariant($"""<p class="subtitle">{WebUtility.HtmlEncode(subtitle.Trim())}</p>""");

        var doc = new StringBuilder();
        doc.AppendLine("<!DOCTYPE html>");
        doc.AppendLine("<html lang=\"en\">");
        doc.AppendLine("<head><meta charset=\"utf-8\"/>");
        doc.AppendLine(InteriorPrintDocumentBuilder.GoogleFontLinks());
        doc.AppendLine(fontCss);
        doc.AppendLine("<style>");
        // Page margins: the interior export (no cover) reserves top/bottom space so Chromium can draw
        // the running head + folio on EVERY page (left/right stay 0 — the horizontal text inset comes
        // from the per-style .book-preview-sheet padding). The cover export is full-bleed (margin 0).
        var pageMargin = includeCoverPage ? "0" : "18mm 0 16mm 0";
        doc.AppendLine(FormattableString.Invariant($"@page {{ size: {pageSizeCss}; margin: {pageMargin}; }}"));
        doc.AppendLine("* { box-sizing: border-box; -webkit-print-color-adjust: exact; print-color-adjust: exact; }");
        doc.AppendLine(FormattableString.Invariant($":root {{ --export-page-bg: {pageBg}; }}"));
        doc.AppendLine(themeCss);
        doc.AppendLine(".cover-page { page-break-after: always; width: 100%; min-height: 100vh; position: relative; margin: 0; padding: 0; background: #1e1b4b; }");
        doc.AppendLine(".cover-page-blank { background: var(--export-page-bg, #fff); min-height: 100vh; }");
        doc.AppendLine(".cover-img { width: 100%; height: 100vh; object-fit: cover; display: block; }");
        doc.AppendLine(".cover-fallback { display: flex; align-items: center; justify-content: center; color: #fafafa; min-height: 100vh; }");
        doc.AppendLine(".cover-fallback-inner { text-align: center; padding: 24mm; }");
        doc.AppendLine(".cover-title { font-size: 28pt; margin: 0 0 8mm; letter-spacing: 0.08em; }");
        doc.AppendLine(".cover-meta { font-size: 11pt; opacity: 0.85; max-width: 140mm; margin: 0 auto; }");
        doc.AppendLine(".title-page { background: var(--export-page-bg, var(--page-bg)); }");
        doc.AppendLine(".title-page h1 { font-size: 28pt; margin: 0 0 12mm; font-weight: 600; letter-spacing: 0.03em; line-height: 1.2; }");
        doc.AppendLine(".title-page .subtitle { margin-bottom: 14mm; }");
        // Read Mode polish — screen-only (Chromium PDF uses print media, so this never affects exports).
        // Gives the on-screen reader a warm backdrop, centered "paper" pages with margins/shadow,
        // and shows the cover as a proper centered PORTRAIT card instead of a cropped full-bleed strip.
        // ── Read Mode (Dashboard "open published book") polish — screen-only, never affects the
        //    print/PDF media. A real book page: centered 680px paper, comfortable Georgia reading
        //    typography, portrait cover, thin scrollbar.
        doc.AppendLine("@media screen {");
        doc.AppendLine("  html, body.book-pdf-body { background: #F5F5F0 !important; }");
        doc.AppendLine("  body.book-pdf-body { margin: 0; padding: 32px 16px 72px; color: #1A1A1A; }");
        doc.AppendLine("  body.book-pdf-body > .title-page, body.book-pdf-body > .copyright-page, body.book-pdf-body > .toc-page, body.book-pdf-body > .manuscript-root { max-width: 680px; margin-left: auto; margin-right: auto; background: #FFFFFF; box-shadow: 0 4px 24px rgba(0,0,0,0.10); border-radius: 8px; margin-bottom: 24px; }");
        doc.AppendLine("  body.book-pdf-body > .copyright-page, body.book-pdf-body > .toc-page, body.book-pdf-body > .manuscript-root { padding: 48px 56px; }");
        doc.AppendLine("  body.book-pdf-body > .title-page { padding: 56px; }");
        // Comfortable reading typography (screen only — the PDF keeps the per-style interior fonts).
        doc.AppendLine("  body.book-pdf-body .manuscript-root, body.book-pdf-body .reader-page-body, body.book-pdf-body .reader-page-body p, body.book-pdf-body .reader-page-body .manuscript-p { font-family: Georgia, 'Times New Roman', serif !important; font-size: 16px !important; line-height: 1.8 !important; color: #1A1A1A !important; }");
        doc.AppendLine("  body.book-pdf-body .reader-page-body p, body.book-pdf-body .reader-page-body .manuscript-p { text-indent: 1.5em; margin: 0 0 12px; }");
        doc.AppendLine("  body.book-pdf-body .reader-page-body p:first-of-type, body.book-pdf-body .reader-page-title + .reader-page-body p:first-of-type, body.book-pdf-body .reader-page-body .manuscript-p:first-of-type { text-indent: 0; }");
        doc.AppendLine("  body.book-pdf-body .reader-page-title, body.book-pdf-body .manuscript-h1, body.book-pdf-body .manuscript-h2 { font-family: Georgia, serif !important; font-size: 22px !important; font-weight: 600 !important; margin-top: 40px; color: #1A1A1A; }");
        // Cover — full 6×9 portrait in read mode (matches trim size: 6in × 96dpi = 576px).
        doc.AppendLine("  body.book-pdf-body > .cover-page { min-height: auto; background: transparent; padding: 0; margin: 0 auto 40px; display: flex; justify-content: center; align-items: flex-start; width: min(100%, 576px); max-width: 100%; }");
        doc.AppendLine("  .cover-page .cover-img { width: 100%; height: auto; aspect-ratio: 2 / 3; max-width: min(576px, 100%); max-height: none; object-fit: cover; margin: 0 auto; border-radius: 4px; box-shadow: 0 8px 32px rgba(0,0,0,0.18); }");
        doc.AppendLine("  .cover-page.cover-fallback { min-height: auto; width: min(100%, 576px); max-width: 100%; }");
        doc.AppendLine("  .cover-fallback .cover-fallback-inner { width: 100%; max-width: 576px; aspect-ratio: 2 / 3; margin: 0 auto; display: flex; flex-direction: column; align-items: center; justify-content: center; background: #1e1b4b; border-radius: 4px; padding: 28px; box-shadow: 0 8px 32px rgba(0,0,0,0.18); }");
        // Thin custom scrollbar.
        doc.AppendLine("  ::-webkit-scrollbar { width: 4px; height: 4px; }");
        doc.AppendLine("  ::-webkit-scrollbar-thumb { background: #D1D5DB; border-radius: 4px; }");
        doc.AppendLine("  ::-webkit-scrollbar-thumb:hover { background: #9CA3AF; }");
        doc.AppendLine("  html { scrollbar-width: thin; scrollbar-color: #D1D5DB transparent; }");
        doc.AppendLine("}");
        // Mobile reading padding.
        doc.AppendLine("@media screen and (max-width: 640px) {");
        doc.AppendLine("  body.book-pdf-body > .title-page, body.book-pdf-body > .copyright-page, body.book-pdf-body > .toc-page, body.book-pdf-body > .manuscript-root { padding: 24px 20px; border-radius: 0; }");
        doc.AppendLine("}");
        // PDF print media: preserve the author's chosen interior typography (per-style fonts,
        // text size, line spacing and justification come from InteriorExportTheme — the same
        // settings the user picked in the formatter), and layer professional print-safety on top
        // so the downloaded file is print-ready: widow/orphan control, headings kept with their
        // following text, and images/tables/blockquotes never split across pages. The print page
        // geometry (trim size, margins, running heads, folios, chapter page breaks) is unchanged.
        doc.AppendLine("@media print {");
        doc.AppendLine("  body.book-pdf-body .manuscript-root, body.book-pdf-body .reader-page-body, body.book-pdf-body .reader-page-body p, body.book-pdf-body .reader-page-body .manuscript-p, body.book-pdf-body .manuscript-p { orphans: 3; widows: 3; }");
        doc.AppendLine("  body.book-pdf-body .reader-page-title, body.book-pdf-body .manuscript-h1, body.book-pdf-body .manuscript-h2, body.book-pdf-body .manuscript-h3 { break-after: avoid; page-break-after: avoid; break-inside: avoid; page-break-inside: avoid; }");
        doc.AppendLine("  body.book-pdf-body img, body.book-pdf-body figure, body.book-pdf-body table, body.book-pdf-body blockquote, body.book-pdf-body pre { break-inside: avoid; page-break-inside: avoid; }");
        doc.AppendLine("  body.book-pdf-body img { max-width: 100%; height: auto; }");
        doc.AppendLine("}");
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
        doc.AppendLine("</body></html>");
        return doc.ToString();
    }
}
