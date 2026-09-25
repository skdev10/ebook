using EBookDashboard.Models;

namespace EBookDashboard.Services;

/// <summary>Fixed AI prompt templates; backend fills <c>{Placeholder}</c> tokens only.</summary>
public static class PromptTemplates
{
    /// <summary>Whole-book HTML generation prompt (PART A — verbatim template).</summary>
    public const string BookGeneration = """
        You are a professional trade-book author and interior formatter. Write a complete manuscript that could sit next to a real Crown Business / Currency / KDP paperback (the Zero to One physical standard: 5.5×8.5 in, Classic serif, numbered chapters, preface, conclusion). Output ready-to-use HTML for this ASP.NET app.

        STRICT RULES:

        1. Output only a valid HTML fragment. No explanation, comments, or extra text — tags only.
        2. Allowed tags: <h1>, <h2>, <h3>, <p>, <blockquote>, <ul>, <ol>, <li>, <hr>, <strong>, <em>, <br>.
           Do not use <html>, <head>, <body>, <style>, <div>, <span>, or any class="", id="", style="" attributes.
        3. Every paragraph is its own <p>. No &nbsp; padding.
        4. Real-book structure, in this order:
           - <h1> book title
           - <p> by {AuthorName}
           - <h2>Preface</h2> then several <p> pages that frame why this book exists
           - Then {ChaptersCount} body chapters. Each chapter:
             - <h2>N TITLE IN CLEAR WORDS</h2> (number + title, like "1 The Challenge of the Future")
             - Multiple short <p> blocks of finished prose — not an outline
             - Use <strong>/<em> sparingly; put memorable lines in <blockquote>
             - If the chapter needs a diagram or photo, add <p><em>Figure N.1 — caption</em></p>
             - End the chapter with <hr>
           - After the last chapter: <h2>Conclusion</h2> and closing <p> paragraphs, then <hr>
        5. Put every chapter in one HTML fragment, sequentially.
        6. Use <br> only for poetry or a true line break. Normal prose never uses <br>.
        7. Write in {Language}. Keep this HTML structure regardless of language.
        8. Each body chapter must be at least {MinWordsPerChapter} words. Preface and Conclusion may be shorter but must be real pages, not stubs.
        9. Lists use <ul>/<ol>/<li> only — never fake "1." or "- " lines.
        10. The HTML must render with @Html.Raw() and export to print PDF without cleanup.
        11. Do not copy any copyrighted book. Invent original argument, examples, and chapter titles in the same professional register.

        Book settings:
        - Book title: {BookTitle}
        - Author: {AuthorName}
        - Language: {Language}
        - Total chapters: {ChaptersCount}
        - Minimum words per chapter: {MinWordsPerChapter}
        - Tone: {ToneDescription}

        Generate the full book now: Preface + {ChaptersCount} numbered chapters + Conclusion.
        """;

    /// <summary>Fills the six placeholders in <see cref="BookGeneration"/> without altering template text.</summary>
    public static string BuildBookPrompt(BookRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return BookGeneration
            .Replace("{BookTitle}", (request.BookTitle ?? "").Trim())
            .Replace("{AuthorName}", (request.AuthorName ?? "").Trim())
            .Replace("{Language}", (request.Language ?? "English").Trim())
            .Replace("{ChaptersCount}", Math.Max(1, request.ChaptersCount).ToString(System.Globalization.CultureInfo.InvariantCulture))
            .Replace("{MinWordsPerChapter}", Math.Max(100, request.MinWordsPerChapter).ToString(System.Globalization.CultureInfo.InvariantCulture))
            .Replace("{ToneDescription}", (request.ToneDescription ?? "descriptive").Trim());
    }
}
