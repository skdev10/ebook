using System.ComponentModel.DataAnnotations;
using EBookDashboard.Services.BookApi;

namespace EBookDashboard.Models.Options;

/// <summary>Typed configuration for the Python FastAPI upstream (see DEPLOYMENT.md for env var names).</summary>
public class ExternalApiOptions
{
    public const string SectionName = "ExternalApi";

    /// <summary>Base URL of the upstream API (no trailing slash required).</summary>
    public string BaseUrl { get; set; } = "";

    /// <summary>API key for <c>X-API-Key</c>. Prefer env <c>ExternalApi__ApiKey</c>; OpenAI key fallback is resolved separately via configuration.</summary>
    public string ApiKey { get; set; } = "";

    public string GenerateUrl { get; set; } = "";
    /// <summary>Optional dedicated whole-book HTML endpoint; falls back to <see cref="GenerateUrl"/>.</summary>
    public string GenerateBookUrl { get; set; } = "";
    public string EditUrl { get; set; } = "";
    public string ApproveUrl { get; set; } = "";
    public string GenerateCoverUrl { get; set; } = "";
    public string GenerateSpineBookCoverUrl { get; set; } = "";
    /// <summary>Split spine/back generation using the saved front cover (Publish step).</summary>
    public string GenerateSpineBookCoverSplitUrl { get; set; } = "";
    public string EditCoverUrl { get; set; } = "";
    public string AudioUrl { get; set; } = "";
    public string QueueDataUrl { get; set; } = "";
    public string BookChaptersNameUrl { get; set; } = "";
    public string RefineCoverPromptUrl { get; set; } = "";
    public string SuggestCoverPromptFromHighlightsUrl { get; set; } = "";

    public string CoverGenerateSize { get; set; } = "1024x1536";
    public string CoverGenerateQuality { get; set; } = "medium";
    public string PrintReadyCoverSize { get; set; } = "1536x1024";
    public string PrintReadyCoverQuality { get; set; } = "medium";
    public string PrintReadyCoverStyle { get; set; } = "";

    public string? AudioMultipartFieldNames { get; set; }
    public bool AudioSendLocalFilePath { get; set; }
    public bool AudioSendEmptyAudioFilePath { get; set; }
    public string? AudioAuthorizationHeader { get; set; }

    [MaxLength(4000)]
    public string? Sql_AlterSettingsValueLongText { get; set; }

    /// <summary>Absolute URL if <paramref name="configuredFullUrl"/> is set; otherwise <see cref="BaseUrl"/> + <paramref name="relativePath"/>.</summary>
    public string ResolveUrl(string? configuredFullUrl, string relativePath)
    {
        if (!string.IsNullOrWhiteSpace(configuredFullUrl))
            return configuredFullUrl.Trim();
        var root = (string.IsNullOrWhiteSpace(BaseUrl) ? BookApiConstants.DefaultUpstreamBaseUrl : BaseUrl).TrimEnd('/');
        relativePath = relativePath.StartsWith('/') ? relativePath : "/" + relativePath;
        return root + relativePath;
    }
}
