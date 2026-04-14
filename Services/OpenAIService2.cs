using OpenAI;
using OpenAI.Audio;
using OpenAI.Chat;
using Microsoft.Extensions.Logging;

public class OpenAIService2
{
    private readonly AudioClient _audioClient;
    private readonly ChatClient _chatClient;
    private readonly ILogger<OpenAIService2>? _logger;

    public OpenAIService2(IConfiguration config, ILogger<OpenAIService2>? logger = null)
    {
        var apiKey = config["OpenAI:ApiKey"]
            ?? throw new InvalidOperationException("OPENAI_API_KEY environment variable is not set.");

        _audioClient = new AudioClient("gpt-4o-mini-transcribe", apiKey);
        _chatClient = new ChatClient("gpt-4o-mini", apiKey);
        _logger = logger;
    }

    public async Task<string> TranscribeAudioAsync(
        string userId,
        string bookId,
        int chapter,
        string audioFilePath)
    {
        try
        {
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

            // 2️⃣ Format text into HTML (NO WORD CHANGES)
            var formattingPrompt = $"""
                Convert the following text into readable HTML paragraphs using <p> and <br> tags.
                Keep every single word and character exactly the same — do not fix grammar or punctuation.
                Do NOT escape HTML characters or wrap output in code blocks.
                Only improve structure for visual clarity.

                {transcribedText}
                """;

            ChatCompletion response = await _chatClient.CompleteChatAsync(formattingPrompt);

            var formattedText = response.Content[0].Text.Trim();
            _logger?.LogInformation("Text formatted successfully. Final length: {Length} characters", formattedText.Length);

            return formattedText;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error transcribing audio: {Message}", ex.Message);
            throw; // Re-throw to let controller handle it
        }
    }
}