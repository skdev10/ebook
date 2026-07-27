using System.Net;
using System.Text;
using EBookDashboard.Configuration;
using EBookDashboard.Models;
using EBookDashboard.Services;
using Microsoft.AspNetCore.Hosting;

namespace EBookDashboard.Services.Rendering;

/// <summary>Which slice of a manuscript's sections to render into HTML.</summary>
public enum SectionScope
{
    All = 0,
    FrontOnly = 1,
    BodyAndBack = 2
}

/// <summary>Options controlling <see cref="BookHtmlBuilder.BuildHtml"/> output.</summary>
public sealed class BookHtmlBuildOptions
{
    /// <summary>Adds trim-guide overlays and screen-only "page card" styling for the workspace preview iframe.</summary>
    public bool PreviewMode { get; set; }

    /// <summary>Shows a 2-up spread layout in preview mode (ignored for print/export).</summary>
    public bool SpreadPreview { get; set; }

    /// <summary>Embeds invisible "SECMARK{sectionId}" text markers so <see cref="PagedRenderer"/> can locate physical page starts via PdfPig.</summary>
    public bool IncludeSectionMarkers { get; set; }

    public SectionScope Scope { get; set; } = SectionScope.All;
}

/// <summary>
/// Builds the full print/preview HTML for a manuscript version: CSS Paged Media rules (page size,
/// mirrored verso/recto gutter margins, bleed), chapter break rules, typography from the
/// <see cref="LayoutProfile"/>, and (best-effort, Chromium-safe) running headers/page-number styling.
/// True Roman-numeral / restart-at-1 page numbers are finished by <see cref="PagedRenderer"/>, which
/// overlays them after counting physical pages — Chromium's print engine has no CSS hook for that.
/// </summary>
public sealed class BookHtmlBuilder
{
    private readonly KdpCalculationService _calc;
    private readonly IWebHostEnvironment _env;

    public BookHtmlBuilder(KdpCalculationService calc, IWebHostEnvironment env)
    {
        _calc = calc;
        _env = env;
    }

    public string BuildHtml(Project project, ManuscriptVersion version, LayoutProfile profile, BookHtmlBuildOptions? options = null)
    {
        options ??= new BookHtmlBuildOptions();

        var allSections = version.Sections.OrderBy(s => s.OrderIndex).ToList();
        var scoped = options.Scope switch
        {
            SectionScope.FrontOnly => allSections.Where(s => s.MatterType == MatterType.Front).ToList(),
            SectionScope.BodyAndBack => allSections.Where(s => s.MatterType != MatterType.Front).ToList(),
            _ => allSections
        };

        var isEbook = profile.IsEbookProfile;
        var (pageWidthIn, pageHeightIn) = _calc.GetDocumentSizeWithBleedIn(project.TrimWidthIn, project.TrimHeightIn, profile.BleedMode);
        var css = BuildCss(project, profile, pageWidthIn, pageHeightIn, isEbook, options);
        var bodyHtml = BuildBodyHtml(scoped, profile, isEbook, options);

        var previewAttr = options.PreviewMode ? "true" : "false";
        var sb = new StringBuilder();
        sb.Append("<!DOCTYPE html><html lang=\"en\"><head><meta charset=\"utf-8\"/>");
        sb.Append("<title>").Append(WebUtility.HtmlEncode(project.Title)).Append("</title>");
        sb.Append("<style>").Append(css).Append("</style></head>");
        sb.Append("<body class=\"book-html").Append(isEbook ? " is-ebook" : " is-print")
          .Append(options.PreviewMode ? " is-preview" : "").Append("\" data-preview=\"").Append(previewAttr).Append("\">");
        sb.Append(bodyHtml);
        sb.Append("</body></html>");
        return sb.ToString();
    }

    private string BuildCss(Project project, LayoutProfile profile, double pageWidthIn, double pageHeightIn, bool isEbook, BookHtmlBuildOptions options)
    {
        var bleedIn = profile.BleedMode == BleedMode.AllSides ? KdpSpecsAccessor.Current.BleedIn : 0.0;
        var outside = Math.Round(profile.MarginOutsideIn + bleedIn, 4);
        var inside = Math.Round(profile.MarginInsideIn, 4);
        var top = Math.Round(profile.MarginTopIn + bleedIn, 4);
        var bottom = Math.Round(profile.MarginBottomIn + bleedIn, 4);
        var align = ResolveTextAlign(profile.TextAlignment);
        var indent = profile.FirstLineIndentIn;

        var css = new StringBuilder();
        css.Append(BuildFontFaces());

        css.Append($@"
* {{ box-sizing: border-box; }}
html, body {{ margin: 0; padding: 0; }}
body.book-html {{
    font-family: {FontStack(profile.BodyFontFamily)};
    font-size: {profile.BodyFontSizePt}pt;
    line-height: {profile.LineSpacing};
    color: #111827;
    background: #e5e7eb;
}}
h1.section-heading {{
    font-family: {FontStack(profile.H1FontFamily)};
    font-size: {profile.H1FontSizePt}pt;
    margin: 0 0 0.6em 0;
    text-align: center;
}}
h2.subsection-heading {{
    font-family: {FontStack(profile.H2FontFamily)};
    font-size: {profile.H2FontSizePt}pt;
    margin: 1.2em 0 0.5em 0;
}}
.section-body p {{
    margin: 0 0 0.75em 0;
    text-align: {align};
    text-indent: {indent}in;
}}
.section-body p:first-of-type {{ text-indent: {(profile.NoIndentOnFirstPara ? "0" : $"{indent}in")}; }}
.section-body ul {{ margin: 0 0 0.9em 1.2em; padding: 0; }}
.section-body img {{ max-width: 100%; height: auto; }}
.sec-marker {{ color: transparent; font-size: 1px; line-height: 1px; user-select: none; }}
");

        if (isEbook)
        {
            // Reflowable content: no fixed page box, single continuous column.
            css.Append(@"
.book-page { max-width: 42em; margin: 0 auto; padding: 1.25em; }
.section-block { break-before: auto; }
");
            return css.ToString();
        }

        // Print/paginated: CSS Paged Media with mirrored (verso/recto) gutter margins.
        css.Append($@"
@page {{
    size: {Round(pageWidthIn)}in {Round(pageHeightIn)}in;
    margin: {Round(top)}in {Round(outside)}in {Round(bottom)}in {Round(inside)}in;
}}
@page :left {{
    margin-top: {Round(top)}in;
    margin-bottom: {Round(bottom)}in;
    margin-left: {Round(outside)}in;
    margin-right: {Round(inside)}in;
}}
@page :right {{
    margin-top: {Round(top)}in;
    margin-bottom: {Round(bottom)}in;
    margin-left: {Round(inside)}in;
    margin-right: {Round(outside)}in;
}}
.section-block {{ break-before: page; }}
.section-block:first-child {{ break-before: avoid; }}
.section-block.starts-recto {{ break-before: right; }}
.subsection-block {{ break-before: auto; }}
");

        if (!options.PreviewMode)
        {
            // Best-effort running header via Chromium's documented behaviour: `position: fixed`
            // elements repeat on every printed page. Chromium has no CSS Paged-Media margin-box
            // support, so this can only show static text (not an alternating verso/recto title) —
            // true per-page alternation is finished by PagedRenderer's overlay pass.
            css.Append($@"
body.book-html {{ padding-top: 0.3in; }}
.running-header {{
    position: fixed;
    top: 0.08in;
    left: 0;
    right: 0;
    text-align: center;
    font-family: {FontStack(profile.HeaderFooterFontSizePt > 0 ? profile.BodyFontFamily : profile.BodyFontFamily)};
    font-size: {profile.HeaderFooterFontSizePt}pt;
    color: #6b7280;
    letter-spacing: 0.03em;
    text-transform: uppercase;
}}
");
        }

        if (options.PreviewMode)
        {
            css.Append($@"
body.book-html.is-preview {{ background: #94a3b8; padding: 24px; }}
.preview-page-list {{ display: flex; flex-direction: column; align-items: center; gap: 20px; }}
.preview-page-list.is-spread {{ flex-direction: row; flex-wrap: wrap; justify-content: center; align-items: flex-start; }}
.preview-page {{
    width: {Round(pageWidthIn)}in;
    min-height: {Round(pageHeightIn)}in;
    background: #ffffff;
    box-shadow: 0 8px 24px rgba(15,23,42,0.25);
    padding: {Round(top)}in {Round(outside)}in {Round(bottom)}in {Round(inside)}in;
    position: relative;
    overflow: hidden;
}}
.preview-page.is-trim-guide::after {{
    content: '';
    position: absolute; inset: {KdpSpecsAccessor.Current.SafeFromTrimIn}in;
    border: 1px dashed rgba(124,58,237,0.35);
    pointer-events: none;
}}
");
        }

        return css.ToString();
    }

    private string BuildBodyHtml(List<BookSection> sections, LayoutProfile profile, bool isEbook, BookHtmlBuildOptions options)
    {
        var sb = new StringBuilder();

        if (!isEbook && !options.PreviewMode)
            sb.Append("<div class=\"running-header\">").Append(WebUtility.HtmlEncode("")).Append("</div>");

        if (options.PreviewMode)
            sb.Append("<div class=\"preview-page-list").Append(options.SpreadPreview ? " is-spread" : "").Append("\">");

        var byParent = sections.Where(s => s.ParentSectionId != null).ToLookup(s => s.ParentSectionId);
        var topLevel = sections.Where(s => s.ParentSectionId == null).ToList();

        foreach (var section in topLevel)
        {
            var startsRecto = !isEbook && (section.StartsOnRecto || profile.ChaptersStartOnRecto);
            var wrapperClass = options.PreviewMode
                ? "preview-page is-trim-guide"
                : $"section-block{(startsRecto ? " starts-recto" : string.Empty)}";

            sb.Append("<div class=\"").Append(wrapperClass).Append("\" id=\"sec-").Append(section.Id).Append("\" data-section-id=\"").Append(section.Id).Append("\">");
            if (options.IncludeSectionMarkers)
                sb.Append("<span class=\"sec-marker\">SECMARK").Append(section.Id).Append("</span>");

            AppendSectionContent(sb, section);

            foreach (var child in byParent[section.Id].OrderBy(c => c.OrderIndex))
            {
                sb.Append("<div class=\"subsection-block\" id=\"sec-").Append(child.Id).Append("\" data-section-id=\"").Append(child.Id).Append("\">");
                if (options.IncludeSectionMarkers)
                    sb.Append("<span class=\"sec-marker\">SECMARK").Append(child.Id).Append("</span>");
                AppendSectionContent(sb, child);
                sb.Append("</div>");
            }

            sb.Append("</div>");
        }

        if (options.PreviewMode)
            sb.Append("</div>");

        return sb.ToString();
    }

    private static void AppendSectionContent(StringBuilder sb, BookSection section)
    {
        var isChapter = section.ParentSectionId == null;
        var headingTag = isChapter ? "h1" : "h2";
        var headingClass = isChapter ? "section-heading" : "subsection-heading";
        if (!string.IsNullOrWhiteSpace(section.Title))
            sb.Append('<').Append(headingTag).Append(" class=\"").Append(headingClass).Append("\">")
              .Append(WebUtility.HtmlEncode(section.Title)).Append("</").Append(headingTag).Append('>');

        sb.Append("<div class=\"section-body\">").Append(section.ContentHtml).Append("</div>");
    }

    private static string BuildFontFaces()
    {
        // No bundled webfonts ship with the project yet (wwwroot/fonts only holds PDFsharp TTFs
        // used by the legacy PDFsharp exporter) — fall back to well-supported system font stacks.
        return string.Empty;
    }

    private static string FontStack(string family)
    {
        var f = (family ?? string.Empty).Trim();
        if (f.Length == 0) f = "Georgia";
        var lower = f.ToLowerInvariant();
        var fallback = lower.Contains("mono") ? "monospace"
            : lower is "arial" or "helvetica" or "verdana" or "tahoma" or "inter" or "roboto" ? "sans-serif"
            : "serif";
        return $"'{f}', {fallback}";
    }

    private static string ResolveTextAlign(string alignment) => (alignment ?? "Justify").Trim().ToLowerInvariant() switch
    {
        "left" => "left",
        "center" => "center",
        "right" => "right",
        _ => "justify"
    };

    private static string Round(double v) => Math.Round(v, 4).ToString(System.Globalization.CultureInfo.InvariantCulture);
}
