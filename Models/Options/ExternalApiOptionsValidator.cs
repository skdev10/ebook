using Microsoft.Extensions.Configuration;
using EBookDashboard.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace EBookDashboard.Models.Options;

/// <summary>Validates upstream settings at startup (production requires an API key for the book service).</summary>
public sealed class ExternalApiOptionsValidator : IValidateOptions<ExternalApiOptions>
{
    private readonly IConfiguration _configuration;
    private readonly IHostEnvironment _environment;

    public ExternalApiOptionsValidator(IConfiguration configuration, IHostEnvironment environment)
    {
        _configuration = configuration;
        _environment = environment;
    }

    public ValidateOptionsResult Validate(string? name, ExternalApiOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.BaseUrl)
            && !Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out _))
            return ValidateOptionsResult.Fail("ExternalApi:BaseUrl must be an absolute URI when set.");

        if (_environment.IsProduction())
        {
            var key = ExternalApiKeyResolver.Resolve(_configuration);
            if (string.IsNullOrWhiteSpace(key))
            {
                return ValidateOptionsResult.Fail(
                    "ExternalApi:ApiKey is not set. " +
                    "Set environment variable ExternalApi__ApiKey on the server.");
            }
        }

        return ValidateOptionsResult.Success;
    }
}
