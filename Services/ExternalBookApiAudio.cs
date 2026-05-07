using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;

namespace EBookDashboard.Services;

/// <summary>Calls the upstream book service <c>POST /api/audio</c> (same contract as MVC <see cref="EBookDashboard.Controllers.AudioController"/>).</summary>
public static class ExternalBookApiAudio
{
    /// <summary>Tries multipart file field names until one succeeds. Configure with <c>ExternalApi:AudioMultipartFieldNames</c> as comma-separated list.</summary>
    public static async Task<string> TranscribeFileAsync(
        HttpClient http,
        IConfiguration configuration,
        string absoluteFilePath,
        int userId,
        int bookId,
        int chapter,
        CancellationToken cancellationToken = default)
    {
        var fieldNamesCsv = configuration["ExternalApi:AudioMultipartFieldNames"]?.Trim();
        string[] fieldNames;
        if (string.IsNullOrEmpty(fieldNamesCsv))
            fieldNames = new[] { "audio", "audio_file", "file" };
        else
            fieldNames = fieldNamesCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        Exception? lastError = null;
        foreach (var field in fieldNames)
        {
            if (string.IsNullOrWhiteSpace(field)) continue;
            try
            {
                return await TranscribeOnceWithMultipartFieldAsync(
                    http,
                    configuration,
                    absoluteFilePath,
                    userId,
                    bookId,
                    chapter,
                    field.Trim(),
                    cancellationToken).ConfigureAwait(false);
            }
            catch (InvalidOperationException ex)
            {
                lastError = ex;
                // Try next upload field shape (many workers expect audio vs audio_file).
                if (!ex.Message.Contains("Audio API error", StringComparison.OrdinalIgnoreCase))
                    throw;
            }
        }

        throw lastError ?? new InvalidOperationException("Audio transcription failed: no multipart field succeeded.");
    }

    private static async Task<string> TranscribeOnceWithMultipartFieldAsync(
        HttpClient http,
        IConfiguration configuration,
        string absoluteFilePath,
        int userId,
        int bookId,
        int chapter,
        string fileFieldName,
        CancellationToken cancellationToken)
    {
        var apiUrl = configuration["ExternalApi:AudioUrl"] ?? "http://162.229.248.26:8001/api/audio";
        var apiKey = (configuration["ExternalApi:ApiKey"] ?? "").Trim();

        var sendLocalPath =
            string.Equals(configuration["ExternalApi:AudioSendLocalFilePath"], "true", StringComparison.OrdinalIgnoreCase)
            || string.Equals(configuration["ExternalApi:AudioSendLocalFilePath"], "1", StringComparison.OrdinalIgnoreCase);

        var sendEmptyPath =
            string.Equals(configuration["ExternalApi:AudioSendEmptyAudioFilePath"], "true", StringComparison.OrdinalIgnoreCase);

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
            _ => "application/octet-stream"
        };

        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(userId.ToString(CultureInfo.InvariantCulture)), "user_id");
        form.Add(new StringContent(bookId.ToString(CultureInfo.InvariantCulture)), "book_id");
        var chapterSafe = chapter < 1 ? 1 : chapter;
        form.Add(new StringContent(chapterSafe.ToString(CultureInfo.InvariantCulture)), "chapter");

        // Some upstream stacks require the key to exist even when unused.
        if (sendLocalPath)
            form.Add(new StringContent(absoluteFilePath), "audio_file_path");
        else if (sendEmptyPath)
            form.Add(new StringContent(""), "audio_file_path");

        var fileBytes = await File.ReadAllBytesAsync(absoluteFilePath, cancellationToken).ConfigureAwait(false);
        var fileContent = new ByteArrayContent(fileBytes);
        fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse(mime);
        form.Add(fileContent, fileFieldName, Path.GetFileName(absoluteFilePath));

        using var req = new HttpRequestMessage(HttpMethod.Post, apiUrl) { Content = form };

        var authHdr = configuration["ExternalApi:AudioAuthorizationHeader"]?.Trim();
        if (!string.IsNullOrEmpty(authHdr))
            req.Headers.TryAddWithoutValidation("Authorization", authHdr);
        else if (!string.IsNullOrEmpty(apiKey))
            req.Headers.TryAddWithoutValidation("X-API-Key", apiKey);

        using var response = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(
                $"Audio API error {(int)response.StatusCode}: {Truncate(json, 400)} (URL: {apiUrl}, field={fileFieldName}, sentLocalPath={sendLocalPath})");

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String)
                return t.GetString() ?? "";
            if (root.TryGetProperty("transcription", out var tr) && tr.ValueKind == JsonValueKind.String)
                return tr.GetString() ?? "";

            // Nested { data: { text } }
            if (root.TryGetProperty("data", out var dataEl) && dataEl.ValueKind == JsonValueKind.Object)
            {
                if (dataEl.TryGetProperty("text", out var dt) && dt.ValueKind == JsonValueKind.String)
                    return dt.GetString() ?? "";
                if (dataEl.TryGetProperty("transcription", out var dtr) && dtr.ValueKind == JsonValueKind.String)
                    return dtr.GetString() ?? "";
            }
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
