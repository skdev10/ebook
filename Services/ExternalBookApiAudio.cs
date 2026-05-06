using System.Net.Http.Headers;
using System.Text.Json;

namespace EBookDashboard.Services;

/// <summary>Calls the upstream book service <c>POST /api/audio</c> (same contract as MVC <see cref="EBookDashboard.Controllers.AudioController"/>).</summary>
public static class ExternalBookApiAudio
{
    public static async Task<string> TranscribeFileAsync(
        HttpClient http,
        IConfiguration configuration,
        string absoluteFilePath,
        int userId,
        int bookId,
        int chapter,
        CancellationToken cancellationToken = default)
    {
        var apiUrl = configuration["ExternalApi:AudioUrl"] ?? "http://162.229.248.26:8001/api/audio";
        var apiKey = (configuration["ExternalApi:ApiKey"] ?? "").Trim();

        var extension = Path.GetExtension(absoluteFilePath).ToLowerInvariant();
        var mime = extension switch
        {
            ".mp3" => "audio/mpeg",
            ".mp4" => "audio/mp4",
            ".mpeg" => "audio/mpeg",
            ".mpga" => "audio/mpeg",
            ".m4a" => "audio/mp4",
            ".wav" => "audio/wav",
            ".webm" => "audio/webm",
            _ => "audio/mpeg"
        };

        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(userId.ToString()), "user_id");
        form.Add(new StringContent(bookId.ToString()), "book_id");
        form.Add(new StringContent(chapter.ToString()), "chapter");
        form.Add(new StringContent(absoluteFilePath), "audio_file_path");

        var fileBytes = await File.ReadAllBytesAsync(absoluteFilePath, cancellationToken);
        var fileContent = new ByteArrayContent(fileBytes);
        fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse(mime);
        form.Add(fileContent, "audio_file", Path.GetFileName(absoluteFilePath));

        using var req = new HttpRequestMessage(HttpMethod.Post, apiUrl) { Content = form };
        if (!string.IsNullOrEmpty(apiKey))
            req.Headers.TryAddWithoutValidation("X-API-Key", apiKey);

        using var response = await http.SendAsync(req, HttpCompletionOption.ResponseContentRead, cancellationToken);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Audio API error {(int)response.StatusCode}: {Truncate(json, 400)}");

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String)
                return t.GetString() ?? "";
            if (root.TryGetProperty("transcription", out var tr) && tr.ValueKind == JsonValueKind.String)
                return tr.GetString() ?? "";
        }
        catch (JsonException)
        {
            // fall through
        }

        return json.Trim();
    }

    private static string Truncate(string s, int max)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Length <= max ? s : s[..max] + "…";
    }
}
