#pragma warning disable CA1848

using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;

using Google.Apis.Auth;

using SRNSMudApp.Models.Unions;

namespace SRNSMudApp.Services.Auth;

public class ExternalTokenVerificationService(HttpClient httpClient, ILogger<ExternalTokenVerificationService> logger)
    : IExternalTokenVerificationService
{
    private readonly HttpClient _httpClient = httpClient;
    private readonly ILogger<ExternalTokenVerificationService> _logger = logger;

    public async Task<Result<ExternalTokenPayload>> VerifyTokenAsync(string provider, string token)
    {
        return await (provider.ToUpperInvariant() switch
        {
            "GOOGLE" => VerifyGoogleTokenAsync(token),
            "LINE" => VerifyLineTokenAsync(token),
            "GITHUB" => VerifyGithubTokenAsync(token),
            _ => Task.FromResult<Result<ExternalTokenPayload>>(new Failure("Unsupported provider"))
        });
    }

    private async Task<Result<ExternalTokenPayload>> VerifyGoogleTokenAsync(string idToken)
    {
        try
        {
            GoogleJsonWebSignature.Payload payload = await GoogleJsonWebSignature.ValidateAsync(idToken);
            return new Success<ExternalTokenPayload>(new ExternalTokenPayload(payload.Email, payload.Subject));
        }
        catch (InvalidJwtException ex)
        {
            _logger.LogWarning(ex, "Invalid Google ID token");
            return new Failure("Invalid Google token");
        }
    }

    private async Task<Result<ExternalTokenPayload>> VerifyLineTokenAsync(string idToken)
    {
        try
        {
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", idToken);
            HttpResponseMessage response = await _httpClient.GetAsync(new Uri("https://api.line.me/v2/profile"));

            return await (response.IsSuccessStatusCode switch
            {
                false => Task.FromResult<Result<ExternalTokenPayload>>(LogAndReturnFailure("Invalid LINE token", null)),
                true => ProcessLineResponseAsync(response)
            });
        }
        catch (HttpRequestException ex)
        {
            return LogAndReturnFailure("HTTP Error verifying LINE token", ex);
        }
        catch (JsonException ex)
        {
            return LogAndReturnFailure("JSON Error verifying LINE token", ex);
        }
    }

    private static async Task<Result<ExternalTokenPayload>> ProcessLineResponseAsync(HttpResponseMessage response)
    {
        var content = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(content);
        var userId = doc.RootElement.GetProperty("userId").GetString();
        return new Success<ExternalTokenPayload>(new ExternalTokenPayload(null, userId));
    }

    private async Task<Result<ExternalTokenPayload>> VerifyGithubTokenAsync(string codeOrToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("https://api.github.com/user"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", codeOrToken);
            request.Headers.UserAgent.ParseAdd("SRNSMudApp");

            HttpResponseMessage response = await _httpClient.SendAsync(request);

            return await (response.IsSuccessStatusCode switch
            {
                false => Task.FromResult<Result<ExternalTokenPayload>>(LogAndReturnFailure("Invalid GitHub token", null)),
                true => ProcessGithubResponseAsync(response)
            });
        }
        catch (HttpRequestException ex)
        {
            return LogAndReturnFailure("HTTP Error verifying GitHub token", ex);
        }
        catch (JsonException ex)
        {
            return LogAndReturnFailure("JSON Error verifying GitHub token", ex);
        }
    }

    private static async Task<Result<ExternalTokenPayload>> ProcessGithubResponseAsync(HttpResponseMessage response)
    {
        var content = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(content);

        var id = doc.RootElement.GetProperty("id").GetInt64().ToString(CultureInfo.InvariantCulture);
        var email = doc.RootElement.TryGetProperty("email", out JsonElement emailProp) switch
        {
            true when emailProp.ValueKind != JsonValueKind.Null => emailProp.GetString(),
            _ => null
        };

        return new Success<ExternalTokenPayload>(new ExternalTokenPayload(email, id));
    }

    private Failure LogAndReturnFailure(string message, Exception? ex)
    {
        _ = ex switch
        {
            null => Task.Run(() => _logger.LogWarning("{Message}", message)),
            _ => Task.Run(() => _logger.LogError(ex, "{Message}", message))
        };
        return new Failure(message);
    }
}
#pragma warning restore CA1848