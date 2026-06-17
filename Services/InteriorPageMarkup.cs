using System.Globalization;
using System.Net;

namespace EBookDashboard.Services;

/// <summary>
/// Shared chapter page DOM for formatter preview parity and Chromium PDF export.
/// Running heads live in <c>.page-header</c> (never Chromium header/footer templates).
/// </summary>
public static class InteriorPageMarkup
{
    /// <summary>Air between running head and body — matches formatter preview (~12px).</summary>
    public const string PageHeaderBodyGap = "0.75rem";

    /// <summary>Builds one narrative chapter block for print HTML.</summary>
    public static string BuildChapterSection(
        int sectionId,
        string runningHeadTitle,
        string titleHtml,
        string bodyHtml,
        string? bodyExtraClass = null)
    {
        var head = WebUtility.HtmlEncode(TruncateRunningHead(runningHeadTitle));
        var bodyClass = string.IsNullOrEmpty(bodyExtraClass)
            ? "reader-page-body"
            : $"reader-page-body {bodyExtraClass}";

        return FormattableString.Invariant($"""
<section class="chapter" id="ch-{sectionId}">
  <div class="book-preview-sheet">
    <div class="page-header book-page-running-head" aria-hidden="false">{head}</div>
    <div class="page-body">
      <div class="reader-chapter-block" data-chapter-start="1">
        <article class="reader-page-title">{titleHtml}</article>
        <section class="{bodyClass}">{bodyHtml}</section>
      </div>
    </div>
  </div>
</section>
""");
    }

    /// <summary>Fallback when a book has no exportable chapters.</summary>
    public static string BuildEmptyChapterFallback() =>
        BuildChapterSection(0, "Untitled", "Chapter",
            "<p class=\"manuscript-p\">No chapters in this book yet.</p>", null);

    /// <summary>Running head truncation — keeps long titles on one line in the header band.</summary>
    public static string TruncateRunningHead(string? title, int max = 52)
    {
        var s = (title ?? "").Trim();
        if (string.IsNullOrEmpty(s)) return "";
        return s.Length <= max ? s : s.Substring(0, max - 1) + "…";
    }
}
