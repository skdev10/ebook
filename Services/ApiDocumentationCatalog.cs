namespace EBookDashboard.Services;

/// <summary>Machine-readable catalog of upstream FastAPI endpoints and EbookAI BFF wrappers.</summary>
public static class ApiDocumentationCatalog
{
    public static object Build(string baseUrl, string bffBaseUrl)
    {
        baseUrl = (baseUrl ?? "").TrimEnd('/');
        bffBaseUrl = (bffBaseUrl ?? "").TrimEnd('/');

        return new
        {
            title = "EbookAI — External Book API",
            version = "1.0",
            authentication = new
            {
                header = "X-API-Key",
                note = "Set ExternalApi__ApiKey on the server only. Never commit keys to git."
            },
            upstreamBaseUrl = baseUrl,
            bffBaseUrl,
            markdownDoc = "/docs/EXTERNAL_API.md",
            smokeTests = new
            {
                httpFile = "/smoke-tests.http",
                python = "python Scripts/smoke_test.py --base-url " + baseUrl + " --api-key $API_KEY",
                serverScript = "bash deploy/verify-all-apis.sh"
            },
            upstreamEndpoints = UpstreamEndpoints(baseUrl),
            bffEndpoints = BffEndpoints(bffBaseUrl),
            upstreamTables = UpstreamTables()
        };
    }

    private static object[] UpstreamEndpoints(string upstreamBase)
    {
        return
        [
            Ep("POST", $"{upstreamBase}/api/generate_chapter", "Generate chapter (Temporary_database)", new
            {
                user_id = "string",
                book_id = "string",
                chapter = "string e.g. \"18\"",
                user_input = "string topic/brief"
            }, "2–8+ minutes"),
            Ep("POST", $"{upstreamBase}/api/edit", "Edit draft chapter", new
            {
                user_id = "string",
                book_id = "string",
                chapter = "string",
                changes = "string e.g. replace 8790 with 6789 in heading"
            }, "minutes"),
            Ep("POST", $"{upstreamBase}/api/audio", "Audio → text (audio_transcriptions)", new
            {
                user_id = "string",
                book_id = "string",
                chapter = "number",
                audio_file_path = "string path on upstream server OR multipart file"
            }, "fast–minutes; formats: .mp3 .mp4 .mpeg .mpga .m4a .wav .webm"),
            Ep("POST", $"{upstreamBase}/api/approve", "Confirm chapter → User_confirm", new
            {
                user_id = "string",
                book_id = "string",
                chapter = "number or string",
                approve = true
            }, "fast"),
            Ep("GET", $"{upstreamBase}/api/queue-data", "Queue monitor (running/waiting)", null, "fast"),
            Ep("POST", $"{upstreamBase}/api/generate-cover", "Front cover image", new
            {
                title = "string",
                author_name = "string",
                category = "string",
                cover_style = "string",
                size = "1024x1024 | 1536x1024 | 1024x1536 | auto",
                quality = "low | medium | high | auto"
            }, "minutes"),
            Ep("POST", $"{upstreamBase}/api/edit-cover", "Edit cover image", new
            {
                encoded_image = "base64 PNG/JPEG",
                image_direction = "string prompt",
                size = "valid size"
            }, "minutes"),
            Ep("POST", $"{upstreamBase}/api/generate-spine-book-cover", "Print wrap (spine+back+front)", new
            {
                title = "string",
                author_name = "string",
                page_count = "number",
                Interior_trim_size = "string",
                paper_type = "white | cream"
            }, "minutes"),
            Ep("POST", $"{upstreamBase}/api/book_chapters_name", "Suggest chapter names from highlights", new
            {
                user_id = "string",
                book_id = "string",
                highlights = "array of { chapter_name, detailed_bullet_summary }"
            }, "fast"),
            Ep("POST", $"{upstreamBase}/api/refine_cover_prompt", "Refine cover prompt", new { user_prompt = "string" }, "fast"),
            Ep("POST", $"{upstreamBase}/api/suggest-cover-prompt-from-highlights", "Cover prompt from highlights", new
            {
                user_id = "string",
                book_id = "string",
                highlights = "array"
            }, "fast")
        ];
    }

    private static object[] BffEndpoints(string bff)
    {
        return
        [
            new { method = "POST", url = $"{bff}/Books/AIGenerateBook", upstream = "/api/generate_chapter", auth = "session cookie" },
            new { method = "POST", url = $"{bff}/Books/AIEditBook", upstream = "/api/edit", auth = "session cookie" },
            new { method = "POST", url = $"{bff}/Books/FinalizeChapterAPI", upstream = "/api/approve", auth = "session cookie" },
            new { method = "POST", url = $"{bff}/Books/TranscribeAudio", upstream = "/api/audio", auth = "session cookie" },
            new { method = "GET", url = $"{bff}/Books/GetQueueData", upstream = "/api/queue-data", auth = "session cookie" },
            new { method = "GET", url = $"{bff}/Books/ExternalApiStatus", upstream = "queue probe + config", auth = "anonymous" },
            new { method = "GET", url = $"{bff}/Books/ApiDocumentation", upstream = "this catalog", auth = "anonymous" },
            new { method = "GET", url = $"{bff}/health", upstream = "queue probe", auth = "anonymous" },
            new { method = "POST", url = $"{bff}/Books/GenerateAICoverPreview", upstream = "/api/generate-cover", auth = "session cookie" },
            new { method = "POST", url = $"{bff}/Books/EditAICoverPreview", upstream = "/api/edit-cover", auth = "session cookie" },
            new { method = "POST", url = $"{bff}/Books/BookChaptersName", upstream = "/api/book_chapters_name", auth = "session cookie" }
        ];
    }

    private static object UpstreamTables() => new
    {
        Temporary_database = "Draft chapters before approve (includes suggest_chapter_name)",
        User_confirm = "Approved/finalized chapters",
        audio_transcriptions = "Audio upload transcriptions",
        queue_monitor = "status_running, status_waiting, logs",
        error_logs = "Upstream worker errors"
    };

    private static object Ep(string method, string url, string summary, object? body, string timing)
        => new { method, url, summary, requestBody = body, typicalTiming = timing };
}
