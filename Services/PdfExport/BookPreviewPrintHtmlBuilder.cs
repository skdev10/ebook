using System.Globalization;
using System.Net;
using System.Text;
using EBookDashboard.Models.DTO;
using EBookDashboard.Services;

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
        double? contentHeightPx = null,
        string? sourceTitleHtml = null)
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
        if (!string.IsNullOrWhiteSpace(title))
            doc.AppendLine(FormattableString.Invariant($"<title>{WebUtility.HtmlEncode(title.Trim())}</title>"));
        if (!string.IsNullOrWhiteSpace(author))
            doc.AppendLine(FormattableString.Invariant($"<meta name=\"author\" content=\"{WebUtility.HtmlEncode(author.Trim())}\" />"));
        // Print PDFs must use local TTF/OTF only — Google Fonts CSS serves variable/WOFF2 → Type 3.
        doc.AppendLine(fontCss);
        doc.AppendLine("<style>");
        // One margin box, from InteriorSpacingTheme. Sheet padding is cleared in @media print
        // so these insets are not applied a second time. Cover stays full-bleed.
        var pageHeight = InteriorSpacingTheme.PageHeightToken(pageSizeCss);
        doc.AppendLine(FormattableString.Invariant($"@page {{ size: {pageSizeCss}; }}"));
        doc.AppendLine(InteriorSpacingTheme.AtPageMarginRules(opt.InteriorStyle));
        doc.AppendLine("@page cover { margin: 0; }");
        doc.AppendLine("* { box-sizing: border-box; -webkit-print-color-adjust: exact; print-color-adjust: exact; }");
        doc.AppendLine(FormattableString.Invariant(
            $":root {{ --export-page-bg: {pageBg}; {InteriorSpacingTheme.MarginCssVariables(opt.InteriorStyle, pageHeight)} }}"));
        doc.AppendLine(themeCss);
        // Front matter + every chapter start on a new (preferably right-hand) page.
        // Title (recto) → copyright (verso) → contents (recto) → preface → chapter 1 (recto).
        doc.AppendLine(".title-page { break-before: auto; page-break-before: auto; page-break-after: always; break-after: page; }");
        doc.AppendLine(".copyright-page { break-before: left; page-break-before: left; }");
        doc.AppendLine(".toc-page { break-before: right; page-break-before: right; }");
        doc.AppendLine(".manuscript-root > section.chapter { break-before: right; page-break-before: right; }");
        doc.AppendLine(".manuscript-root > section.chapter.front-matter-flow { break-before: auto; page-break-before: auto; }");
        doc.AppendLine(".source-title-page img { max-width: 100%; max-height: var(--text-block-h); width: auto; height: auto; margin: 0 auto; }");
        doc.AppendLine(".book-pdf-body .copyright-page.source-copyright { display: block; text-align: left; justify-content: flex-start; }");
        doc.AppendLine(".book-pdf-body .copyright-page.source-copyright .copyright-block { text-align: left; font-size: 8pt; line-height: 1.35; }");
        doc.AppendLine(".book-pdf-body .copyright-page.source-copyright .copyright-block p, .book-pdf-body .copyright-page.source-copyright .copyright-block h1, .book-pdf-body .copyright-page.source-copyright .copyright-block h2, .book-pdf-body .copyright-page.source-copyright .copyright-block h3 { text-align: left; font-size: 8pt; font-weight: 400; margin: 0 0 0.35em; line-height: 1.35; }");
        doc.AppendLine(".book-pdf-body { hyphens: auto; -webkit-hyphens: auto; }");
        doc.AppendLine(".reader-page-body p, .manuscript-p { hyphens: auto; -webkit-hyphens: auto; text-align: justify; }");
        doc.AppendLine(".manuscript-figure { break-inside: avoid; page-break-inside: avoid; margin: 0.15in 0; max-height: var(--text-block-h); }");
        doc.AppendLine(".manuscript-figure img, .book-pdf-body img { max-width: 100%; max-height: var(--text-block-h); width: auto; height: auto; }");
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
        // Matches AI Writer ebook preview quality: warm stage, tall paper pages, full manuscript content.
        doc.AppendLine("@media screen {");
        doc.AppendLine("  html, body.book-pdf-body { background: #edf0f4 !important; }");
        doc.AppendLine("  body.book-pdf-body { margin: 0; padding: 28px 18px 80px; color: #1c1917; }");
        doc.AppendLine("  body.book-pdf-body > .title-page, body.book-pdf-body > .copyright-page, body.book-pdf-body > .toc-page, body.book-pdf-body > .manuscript-root { width: min(100%, 680px); max-width: 680px; margin-left: auto; margin-right: auto; background: #fffef8; box-shadow: 0 1px 0 rgba(255,255,255,0.95) inset, 0 2px 4px rgba(28,25,23,0.08), 0 18px 40px -10px rgba(28,25,23,0.35), 0 0 0 1px rgba(68,48,36,0.22); border-radius: 3px; margin-bottom: 28px; border: 1px solid rgba(68,48,36,0.18); }");
        doc.AppendLine("  body.book-pdf-body > .copyright-page, body.book-pdf-body > .toc-page, body.book-pdf-body > .manuscript-root { padding: 52px 56px 56px; min-height: 720px; }");
        doc.AppendLine("  body.book-pdf-body > .title-page { padding: 72px 56px; min-height: 720px; display: flex; flex-direction: column; justify-content: center; text-align: center; }");
        doc.AppendLine("  body.book-pdf-body .manuscript-root, body.book-pdf-body .reader-page-body, body.book-pdf-body .reader-page-body p, body.book-pdf-body .reader-page-body .manuscript-p { font-family: Georgia, 'Times New Roman', serif !important; font-size: 16.5px !important; line-height: 1.78 !important; color: #1c1917 !important; }");
        doc.AppendLine("  body.book-pdf-body .reader-page-body p, body.book-pdf-body .reader-page-body .manuscript-p { text-indent: 1.5em; margin: 0 0 0.85em; text-align: justify; hyphens: auto; }");
        doc.AppendLine("  body.book-pdf-body .reader-page-body p:first-of-type, body.book-pdf-body .reader-page-title + .reader-page-body p:first-of-type, body.book-pdf-body .reader-page-body .manuscript-p:first-of-type { text-indent: 0; }");
        doc.AppendLine("  body.book-pdf-body .reader-page-title, body.book-pdf-body .manuscript-h1, body.book-pdf-body .manuscript-h2 { font-family: Georgia, 'Playfair Display', serif !important; font-size: 1.55rem !important; font-weight: 600 !important; margin: 2.25rem 0 1.15rem; color: #1c1917; line-height: 1.3; letter-spacing: 0.01em; }");
        doc.AppendLine("  body.book-pdf-body .manuscript-root > section:first-child .reader-page-title, body.book-pdf-body .manuscript-root > .reader-chapter-block:first-child .reader-page-title { margin-top: 0.35rem; }");
        doc.AppendLine("  body.book-pdf-body img, body.book-pdf-body figure img { max-width: 100% !important; height: auto !important; display: block; margin: 1.1em auto; border-radius: 2px; box-shadow: 0 2px 12px rgba(28,25,23,0.14); }");
        // Cover — full 6×9 portrait in read mode (matches trim size: 6in × 96dpi = 576px).
        doc.AppendLine("  body.book-pdf-body > .cover-page { min-height: auto; background: transparent; padding: 0; margin: 0 auto 36px; display: flex; justify-content: center; align-items: flex-start; width: min(100%, 576px); max-width: 100%; }");
        doc.AppendLine("  .cover-page .cover-img { width: 100%; height: auto; aspect-ratio: 2 / 3; max-width: min(576px, 100%); max-height: none; object-fit: cover; margin: 0 auto; border-radius: 3px; box-shadow: 0 8px 32px rgba(0,0,0,0.18), 0 0 0 1px rgba(68,48,36,0.22); }");
        doc.AppendLine("  .cover-page.cover-fallback { min-height: auto; width: min(100%, 576px); max-width: 100%; }");
        doc.AppendLine("  .cover-fallback .cover-fallback-inner { width: 100%; max-width: 576px; aspect-ratio: 2 / 3; margin: 0 auto; display: flex; flex-direction: column; align-items: center; justify-content: center; background: #1e1b4b; border-radius: 3px; padding: 28px; box-shadow: 0 8px 32px rgba(0,0,0,0.18); }");
        doc.AppendLine("  ::-webkit-scrollbar { width: 5px; height: 5px; }");
        doc.AppendLine("  ::-webkit-scrollbar-thumb { background: #c4b5fd; border-radius: 6px; }");
        doc.AppendLine("  ::-webkit-scrollbar-thumb:hover { background: #a78bfa; }");
        doc.AppendLine("  html { scrollbar-width: thin; scrollbar-color: #c4b5fd transparent; }");
        doc.AppendLine("}");
        // Mobile reading padding.
        doc.AppendLine("@media screen and (max-width: 640px) {");
        doc.AppendLine("  body.book-pdf-body { padding: 12px 0 48px; }");
        doc.AppendLine("  body.book-pdf-body > .title-page, body.book-pdf-body > .copyright-page, body.book-pdf-body > .toc-page, body.book-pdf-body > .manuscript-root { padding: 28px 22px; border-radius: 0; min-height: 0; width: 100%; max-width: 100%; box-shadow: none; border-left: none; border-right: none; }");
        doc.AppendLine("}");
        // PDF print media: preserve the author's chosen interior typography (per-style fonts,
        // text size, line spacing and justification come from InteriorExportTheme — the same
        // settings the user picked in the formatter), and layer professional print-safety on top
        // so the downloaded file is print-ready: widow/orphan control, headings kept with their
        // following text, and images/tables/blockquotes never split across pages. The print page
        // geometry (trim size, margins, running heads, folios, chapter page breaks) is unchanged.
        doc.AppendLine("@media print {");
        doc.AppendLine("  .book-pdf-body .book-preview-sheet { padding: 0 !important; }");
        doc.AppendLine("  .book-pdf-body .title-page, .book-pdf-body .front-matter-page, .book-pdf-body .copyright-page, .book-pdf-body .toc-page { min-height: var(--text-block-h) !important; height: auto !important; }");
        doc.AppendLine("  body.book-pdf-body .manuscript-root, body.book-pdf-body .reader-page-body, body.book-pdf-body .reader-page-body p, body.book-pdf-body .reader-page-body .manuscript-p, body.book-pdf-body .manuscript-p { orphans: 3; widows: 3; }");
        doc.AppendLine("  body.book-pdf-body .reader-page-title, body.book-pdf-body .manuscript-h1, body.book-pdf-body .manuscript-h2, body.book-pdf-body .manuscript-h3 { break-after: avoid; page-break-after: avoid; break-inside: avoid; page-break-inside: avoid; }");
        doc.AppendLine("  body.book-pdf-body table, body.book-pdf-body blockquote, body.book-pdf-body pre { break-inside: avoid; page-break-inside: avoid; }");
        doc.AppendLine("  body.book-pdf-body figure, body.book-pdf-body figure img { break-inside: avoid; page-break-inside: avoid; }");
        doc.AppendLine("  body.book-pdf-body figure { text-align: center; margin: 0.15in 0; max-height: var(--text-block-h); }");
        doc.AppendLine("  body.book-pdf-body img { max-width: 100%; max-height: var(--text-block-h); width: auto; height: auto; }");
        doc.AppendLine("  body.book-pdf-body figure:has(figcaption) img { max-height: calc(var(--text-block-h) - 0.35in); }");
        doc.AppendLine("  body.book-pdf-body sup, body.book-pdf-body sub { font-size: 0.75em; line-height: 0; }");
        doc.AppendLine("  body.book-pdf-body .has-drop-cap > .reader-page-body > p.drop-cap-start::first-letter { initial-letter: 3; font-weight: 700; line-height: 1; margin-right: 0.08em; }");
        doc.AppendLine("}");
        doc.AppendLine("</style></head>");
        doc.AppendLine("<body class=\"book-pdf-body reader-content-wrap " + bodyTemplateClass + " " + previewShellClass + " " + previewWrapClass + "\">");
        doc.Append(coverBlock);
        if (!string.IsNullOrWhiteSpace(sourceTitleHtml))
            doc.AppendLine(sourceTitleHtml);
        else if (!string.IsNullOrWhiteSpace(title) || !string.IsNullOrWhiteSpace(subtitle) || !string.IsNullOrWhiteSpace(author))
        {
            // Generated title: only title / subtitle / author from settings — never invent placeholders.
            doc.AppendLine("<div class=\"title-page book-preview-sheet\">");
            if (!string.IsNullOrWhiteSpace(title))
                doc.Append("<h1>").Append(WebUtility.HtmlEncode(title.Trim())).AppendLine("</h1>");
            doc.AppendLine(subtitleBlock);
            doc.Append(metaLines);
            doc.AppendLine("</div>");
        }

        if (!string.IsNullOrWhiteSpace(copyrightHtml))
            doc.AppendLine(copyrightHtml);
        doc.AppendLine(tocHtml);
        doc.AppendLine("""<div class="manuscript-root">""");
        doc.Append(chapterSections);
        doc.AppendLine("</div>");
        doc.AppendLine("</body></html>");
        return doc.ToString();
    }
}
