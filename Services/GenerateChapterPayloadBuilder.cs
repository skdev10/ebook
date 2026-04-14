using EBookDashboard.Models;

namespace EBookDashboard.Services;

/// <summary>Builds the outbound payload for the external generate_chapter API (shared by BooksController and chapter API).</summary>
public static class GenerateChapterPayloadBuilder
{
    private const string NarrativeHint = """

[Author / generation rules — follow strictly]
- Write the full chapter as polished, immersive literary prose only. Complete the chapter; do not stop mid-scene or summarize early.
- Output nothing except the chapter text: no meta-commentary, no labels, no "Chapter N" headings, no regenerate or UI language, no bullet lists unless the user asked.
- If prior context was included in the prompt, treat it as background only; write only this chapter’s narrative — do not paste or repeat prior chapters.
- Do not tell the reader to wait, or warn about length, or offer to continue — the prose should read as a finished chapter.
- Use natural paragraph breaks and dialogue where appropriate; avoid outline-style or robotic phrasing unless requested.
""";

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
