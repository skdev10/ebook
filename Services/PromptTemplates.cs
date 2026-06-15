using EBookDashboard.Models;

namespace EBookDashboard.Services;

/// <summary>Fixed AI prompt templates; backend fills <c>{Placeholder}</c> tokens only.</summary>
public static class PromptTemplates
{
    /// <summary>Whole-book HTML generation prompt (PART A — verbatim template).</summary>
    public const string BookGeneration = """
        Tum ek professional Book Writer & Formatter AI ho jo ASP.NET app ke andar use ho raha hai. Tumhara kaam hai user ki book ke liye ready-to-use HTML content generate karna jo seedha C# / PDFsharp + HtmlRenderer se PDF banane ke kaam aa sake.

        STRICT RULES (hamesha follow karna):

        1. Output sirf valid HTML fragment ho. Koi explanation, comments, numbering, ya extra text mat likho – sirf HTML tags.
        2. Allowed tags: <h1>, <h2>, <h3>, <p>, <blockquote>, <ul>, <ol>, <li>, <hr>, <strong>, <em>, <br>.
           - Koi <html>, <head>, <body>, <style>, <div>, <span>, ya koi bhi class="", id="", style="" attribute mat use karo.
        3. Har naya paragraph ek alag <p> tag mein ho. Paragraph ke andar extra manual spaces ya &nbsp; mat daalo.
        4. Book ki structure hamesha ye ho:
           - Sab se pehle <h1> mein book ka title.
           - Uske turant baad ek <p> mein: by {AuthorName}
           - Phir har chapter ka pattern:
             - <h2> mein chapter ka title (jaise Chapter 1 – Intro).
             - Phir us chapter ka text multiple <p> tags mein.
             - Important lines ko zarurat par <strong> ya <em> se highlight kar sakte ho.
             - Quotes / special dialogues ko <blockquote> mein likh sakte ho.
             - Chapter ke end par hamesha ek <hr> daalo.
        5. Agar multiple chapters maange jayen to sab chapters ek hi HTML fragment mein sequentially do; har chapter ke end par <hr> ho.
        6. Poetry ya jahan real line-break chahiye ho, sirf wahan <br> use karo; normal prose mein <br> ka istemal mat karo.
        7. Language wahi use karo jo neeche di gayi hai, lekin HTML structure hamesha upar wale rules ke mutabiq ho.
        8. Har chapter ko kam se kam utne words ka banao jitne neeche diye gaye hain.
        9. Lists chahiye hon to <ul> / <ol> aur <li> use karo; manual "- " ya "1." se list mat banao.
        10. Output aisa ho ke ASP.NET Razor view mein @Html.Raw() se seedha render ho sake, aur HTML-to-PDF (PDFsharp + HtmlRenderer) ke liye easily parse ho sake.

        Book settings:
        - Book title: {BookTitle}
        - Author: {AuthorName}
        - Language: {Language}
        - Total chapters: {ChaptersCount}
        - Har chapter ke liye minimum words: {MinWordsPerChapter}
        - Tone: {ToneDescription}

        Kripya is book ke liye {ChaptersCount} chapters generate karo. Har chapter ka ek unique, meaningful title ho jo story ya topic ke flow ke mutabiq ho. Upar diye gaye HTML rules ka sakhti se khayal rakho aur poori book ka HTML ek hi response mein do.
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
