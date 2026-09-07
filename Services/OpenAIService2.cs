using OpenAI;
using OpenAI.Audio;
using OpenAI.Chat;
using Microsoft.Extensions.Logging;

public class OpenAIService2
{
    private readonly string? _apiKey;
    private readonly AudioClient? _audioClient;
    private readonly ChatClient? _chatClient;
    private readonly ILogger<OpenAIService2>? _logger;

    public OpenAIService2(IConfiguration config, ILogger<OpenAIService2>? logger = null)
    {
        // OpenAI:ApiKey — environment variable OpenAI__ApiKey is bound by default configuration providers.
        _apiKey = config["OpenAI:ApiKey"]?.Trim();
        _logger = logger;
        if (!string.IsNullOrEmpty(_apiKey))
        {
            _audioClient = new AudioClient("whisper-1", _apiKey);
            _chatClient = new ChatClient("gpt-4o-mini", _apiKey);
        }
    }

    /// <summary>True when Whisper can transcribe without the chat formatting step.</summary>
    public bool CanTranscribe => _audioClient != null;

    /// <summary>When false, voice-to-text should use <see cref="ExternalBookApiAudio"/> (same key/URL as chapter generation).</summary>
    public bool IsConfigured => !string.IsNullOrEmpty(_apiKey) && _audioClient != null && _chatClient != null;

    /// <summary>Transcribes a local audio file with Whisper and returns plain text.</summary>
    public async Task<string> TranscribeFileAsync(string audioFilePath, CancellationToken cancellationToken = default)
    {
        if (_audioClient == null)
            throw new InvalidOperationException("OpenAI Whisper is not configured. Set OpenAI:ApiKey.");
        if (!System.IO.File.Exists(audioFilePath))
            throw new FileNotFoundException("Audio file not found.", audioFilePath);

        AudioTranscription transcription = await _audioClient.TranscribeAudioAsync(audioFilePath);
        return (transcription.Text ?? string.Empty).Trim();
    }

    public async Task<string> TranscribeAudioAsync(
        string userId,
        string bookId,
        int chapter,
        string audioFilePath)
    {
        try
        {
            if (_audioClient == null || _chatClient == null)
            {
                throw new InvalidOperationException(
                    "OpenAI is not configured. Set OpenAI:ApiKey (e.g. OpenAI__ApiKey on the server), or rely on ExternalApi:AudioUrl with ExternalApi:ApiKey for voice.");
            }

            if (!System.IO.File.Exists(audioFilePath))
            {
                var errorMsg = $"Audio file not found: {audioFilePath}";
                _logger?.LogError(errorMsg);
                throw new FileNotFoundException(errorMsg);
            }

            _logger?.LogInformation(
                "Starting audio transcription for UserId: {UserId}, BookId: {BookId}, Chapter: {Chapter}, File: {FilePath}",
                userId, bookId, chapter, audioFilePath);

            // 1️⃣ Transcribe audio
            AudioTranscription transcription = await _audioClient.TranscribeAudioAsync(audioFilePath);
            var transcribedText = transcription.Text.Trim();

            if (string.IsNullOrEmpty(transcribedText))
            {
                _logger?.LogWarning("Audio transcription returned empty text");
                return string.Empty;
            }

            _logger?.LogInformation("Audio transcribed successfully. Length: {Length} characters", transcribedText.Length);

            // 2️⃣ Format text into HTML (NO WORD CHANGES). If chat formatting fails (quota, policy, network), return plain transcription so voice input still works.
            var formattingPrompt = $"""
                Convert the following text into readable HTML paragraphs using <p> and <br> tags.
                Keep every single word and character exactly the same — do not fix grammar or punctuation.
                Do NOT escape HTML characters or wrap output in code blocks.
                Only improve structure for visual clarity.

                {transcribedText}
                """;

            try
            {
                ChatCompletion response = await _chatClient.CompleteChatAsync(formattingPrompt);
                var formattedText = response.Content[0].Text.Trim();
                _logger?.LogInformation("Text formatted successfully. Final length: {Length} characters", formattedText.Length);
                return formattedText;
            }
            catch (Exception fmtEx)
            {
                _logger?.LogWarning(fmtEx, "HTML formatting step failed; returning raw transcription text.");
                return "<p>" + System.Net.WebUtility.HtmlEncode(transcribedText).Replace("\n", "</p><p>") + "</p>";
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error transcribing audio: {Message}", ex.Message);
            throw; // Re-throw to let controller handle it
        }
    }
}