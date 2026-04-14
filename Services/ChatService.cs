using EBookDashboard.Interfaces;
using Microsoft.Extensions.Options;
using System.Text;
using System.Text.Json;

namespace EBookDashboard.Services;

public class ChatService : IChatService
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public ChatService(HttpClient httpClient, IOptions<OpenAiOptions> options)
    {
        _httpClient = httpClient;
        _apiKey = options.Value.ApiKey ?? "";
        _httpClient.BaseAddress = new Uri("https://api.openai.com");
        _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_apiKey}");
    }

    public async Task<string> GetResponseAsync(string userMessage, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_apiKey))
            return "Please set your OpenAI API key in appsettings.json (OpenAI:ApiKey) or in User Secrets.";

        var request = new OpenAiChatRequest
        {
            Model = "gpt-3.5-turbo",
            Messages = new List<OpenAiMessage> { new() { Role = "user", Content = userMessage } }
        };

        var json = JsonSerializer.Serialize(request, JsonOptions);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await _httpClient.PostAsync("/v1/chat/completions", content, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
            {
                try
                {
                    var err = JsonSerializer.Deserialize<OpenAiErrorResponse>(errorBody, JsonOptions);
                    if (err?.Error?.Code == "insufficient_quota")
                        return "Your OpenAI account has run out of quota or has no billing set up. Please check your plan and billing at https://platform.openai.com/account/billing and add payment method or wait for quota to reset.";
                }
                catch { /* fall through to generic message */ }
            }
            return $"API error ({(int)response.StatusCode}): {errorBody}";
        }

        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
        var chatResponse = JsonSerializer.Deserialize<OpenAiChatResponse>(responseJson, JsonOptions);

        return chatResponse?.Choices?.FirstOrDefault()?.Message?.Content ?? "No response from the model.";
    }

}

/// <summary>Request body for OpenAI /v1/chat/completions API.</summary>
public class OpenAiChatRequest
{
    public string Model { get; set; } = "";
    public List<OpenAiMessage> Messages { get; set; } = new();
}

public class OpenAiMessage
{
    public string Role { get; set; } = "";
    public string Content { get; set; } = "";
}

/// <summary>Response from OpenAI chat completions API.</summary>
public class OpenAiChatResponse
{
    public List<OpenAiChoice>? Choices { get; set; }
}

public class OpenAiChoice
{
    public OpenAiMessage? Message { get; set; }
}

/// <summary>Error response from OpenAI API.</summary>
public class OpenAiErrorResponse
{
    public OpenAiErrorDetail? Error { get; set; }
}

public class OpenAiErrorDetail
{
    public string? Code { get; set; }
    public string? Message { get; set; }
}

public class OpenAiOptions
{
    public const string SectionName = "OpenAI";
    public string ApiKey { get; set; } = "";
}


