using EBookDashboard.Models;

namespace EBookDashboard.Services;

/// <summary>Builds the outbound payload for the external generate_chapter API (shared by BooksController and chapter API).</summary>
public static class GenerateChapterPayloadBuilder
{
    private const string NarrativeHint = """

[Author / generation rules — follow strictly]
- Write this chapter as a finished page from a real published trade book (Crown Business / Currency / KDP 5.5×8.5 hardcover-paperback quality), not a blog post or outline.
- Open with a concrete claim, scene, or question. Then argue or story-tell in short, tight paragraphs a reader can mark up.
- Output only the chapter prose. No meta-commentary, no "Chapter N" banner, no regenerate/UI language, no "in this chapter we will".
- If prior context was included, treat it as background only — do not repeat earlier chapters.
- Do not stop mid-thought, summarize early, or offer to continue.
- Prefer one idea per paragraph. Use dialogue only when it earns the page. Lists only if the user asked.
- When a diagram, table, or photo would appear in a real book, insert a caption line like: Figure 3.1 — short caption. Do not invent fake URLs.
- Close the chapter on a turn or decision that makes the next chapter necessary.
""";

    /// <summary>Upstream FastAPI expects only user_id, book_id, chapter, user_input (chapter as string in docs).</summary>
    public static object BuildUpstreamGeneratePayload(AIBookRequest model, string? augmentedUserInput = null)
    {
        var chapter = model.Chapter > 0 ? model.Chapter : 1;
        var input = augmentedUserInput ?? model.UserInput ?? string.Empty;
        return new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["user_id"] = (model.UserId ?? "").Trim(),
            ["book_id"] = (model.BookId ?? "").Trim(),
            ["chapter"] = chapter.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["user_input"] = string.Concat(input, NarrativeHint)
        };
    }

    public static AIBookRequest CloneForExternalGenerateApi(AIBookRequest model)
    {
        return new AIBookRequest
        {
            ResponseId = model.ResponseId,
            UserId = model.UserId,
            BookId = model.BookId,
            Title = model.Title,
            Chapter = model.Chapter,
            UserInput = string.Concat(model.UserInput ?? "", NarrativeHint),
            PreviewOnly = model.PreviewOnly
        };
    }
}
